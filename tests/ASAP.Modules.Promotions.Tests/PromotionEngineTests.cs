using ASAP.Modules.Promotions;
using ASAP.Modules.Promotions.Offers;
using ASAP.Modules.Promotions.Pricing;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Messaging;
using Shouldly;

namespace ASAP.Modules.Promotions.Tests;

/// <summary>
/// Covers which offer wins when several could apply, and when none may.
/// </summary>
/// <remarks>
/// The rule people expect is that the customer gets the best available offer, and it is nearly
/// always right. Where it is not — a manufacturer funding a promotion that must apply alone, a
/// clearance line excluded from everything — the exception has to be stated rather than emerge
/// from whichever offer happened to be evaluated first.
/// </remarks>
public sealed class PromotionEngineTests
{
    private static readonly Guid Furniture = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Jeddah = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    private static PromotionEngine Engine()
        => new(new MessageCatalog([.. PlatformMessages.All, .. PromotionsMessages.All]));

    private static BasketLine Line(
        int lineNo = 1,
        string itemNo = "ITEM-1001",
        Guid? categoryId = null,
        decimal quantity = 1m,
        decimal unitPrice = 100m,
        decimal unitCost = 20m)
        => new(lineNo, itemNo, categoryId, quantity, unitPrice, unitCost);

    private static Offer Offer(
        string code,
        OfferKind kind = OfferKind.Percentage,
        decimal value = 10m,
        OfferScope scope = OfferScope.Everything,
        StackingRule stacking = StackingRule.Stacks,
        int priority = 0,
        string? itemNo = null,
        Guid? categoryId = null,
        string? couponCode = null,
        Guid? branchId = null,
        SalesChannel channels = SalesChannel.All,
        TimeOnly? startsAt = null,
        TimeOnly? endsAt = null,
        int? daysOfWeek = null,
        bool isActive = true)
    {
        var offer = new Offer
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            Code = code,
            Name = code,
            Kind = kind,
            Scope = scope,
            Value = value,
            GetDiscountPercent = 100m,
            Stacking = stacking,
            Priority = priority,
            CouponCode = couponCode,
            BranchId = branchId,
            Channels = channels,
            StartsOn = new DateOnly(2026, 1, 1),
            StartsAt = startsAt,
            EndsAt = endsAt,
            DaysOfWeek = daysOfWeek,
            IsActive = isActive,
        };

        if (itemNo is not null || categoryId is not null)
        {
            offer.Targets.Add(new OfferTarget
            {
                TenantId = Guid.Empty,
                CompanyId = Guid.Empty,
                ItemNo = itemNo,
                CategoryId = categoryId,
            });
        }

        return offer;
    }

    private static BasketContext Context(
        TimeOnly? at = null,
        SalesChannel channel = SalesChannel.PointOfSale,
        Guid? branchId = null,
        string? customerGroup = null,
        params string[] coupons)
        => new(
            new DateOnly(2026, 8, 27),
            at ?? new TimeOnly(14, 0),
            channel,
            branchId,
            customerGroup,
            coupons.Length > 0 ? coupons : null);

    private static PricedBasket Price(
        PromotionEngine engine,
        IReadOnlyList<BasketLine> lines,
        IReadOnlyList<Offer> offers,
        BasketContext context,
        decimal floor = -1000m,
        List<AsapMessage>? found = null)
        => engine.Price(lines, offers, context, floor, null, found ?? []);

    [Fact]
    public void One_offer_applies_to_the_lines_it_covers()
    {
        var priced = Price(
            Engine(),
            [Line(1, "ITEM-1001"), Line(2, "ITEM-1002")],
            [Offer("TEN", scope: OfferScope.Item, itemNo: "ITEM-1001")],
            Context());

        priced.DiscountOn(1).ShouldBe(10m);
        priced.DiscountOn(2).ShouldBe(0m, "the offer does not name it");
    }

    [Fact]
    public void A_category_offer_covers_everything_in_the_category()
    {
        var priced = Price(
            Engine(),
            [Line(1, categoryId: Furniture), Line(2, "ITEM-2002")],
            [Offer("FURN", scope: OfferScope.Category, categoryId: Furniture, value: 20m)],
            Context());

        priced.DiscountOn(1).ShouldBe(20m);
        priced.DiscountOn(2).ShouldBe(0m);
    }

    [Fact]
    public void Stacking_offers_both_apply()
    {
        var priced = Price(
            Engine(),
            [Line()],
            [Offer("A", value: 10m), Offer("B", kind: OfferKind.AmountPerUnit, value: 5m)],
            Context());

        priced.Discounts.Count.ShouldBe(2);
        priced.TotalDiscount.ShouldBe(15m);
    }

    [Fact]
    public void The_exclusive_offer_worth_most_to_the_customer_wins()
    {
        // Priority is a tiebreak and nothing else. An offer that won on priority while being
        // worth less would be a shop quietly choosing the cheaper discount.
        var priced = Price(
            Engine(),
            [Line()],
            [
                Offer("SMALL", value: 10m, stacking: StackingRule.Exclusive, priority: 99),
                Offer("BIG", value: 30m, stacking: StackingRule.Exclusive, priority: 0),
            ],
            Context());

        priced.Discounts.Single().OfferCode.ShouldBe("BIG");
        priced.TotalDiscount.ShouldBe(30m);
    }

    [Fact]
    public void Priority_only_breaks_a_tie()
    {
        var priced = Price(
            Engine(),
            [Line()],
            [
                Offer("QUIET", value: 20m, stacking: StackingRule.Exclusive, priority: 1),
                Offer("LOUD", value: 20m, stacking: StackingRule.Exclusive, priority: 5),
            ],
            Context());

        priced.Discounts.Single().OfferCode.ShouldBe("LOUD");
    }

    [Fact]
    public void An_exclusive_offer_loses_to_a_better_combination()
    {
        // It applies alone, so it has to beat what the stackable ones would have come to
        // together. Otherwise the customer is worse off for the shop having run more offers.
        var priced = Price(
            Engine(),
            [Line()],
            [
                Offer("ALONE", value: 15m, stacking: StackingRule.Exclusive),
                Offer("A", value: 10m),
                Offer("B", value: 10m),
            ],
            Context());

        priced.TotalDiscount.ShouldBe(20m);
        priced.Discounts.Select(static d => d.OfferCode).ShouldBe(["A", "B"], ignoreOrder: true);
    }

    [Fact]
    public void A_blocking_offer_switches_everything_else_off()
    {
        // For an offer whose funding depends on being the only one. Settled before anything is
        // worked out per line, because it cannot be discovered to be one of several afterwards.
        var priced = Price(
            Engine(),
            [Line(1), Line(2, "ITEM-1002")],
            [
                Offer("FUNDED", value: 5m, stacking: StackingRule.Blocking),
                Offer("GENEROUS", value: 40m),
            ],
            Context());

        priced.Discounts.Select(static d => d.OfferCode).Distinct().ShouldBe(["FUNDED"]);
        priced.TotalDiscount.ShouldBe(10m, "five per cent of each line and nothing else");
    }

    [Fact]
    public void An_offer_outside_its_hours_does_not_apply()
    {
        var happyHour = Offer("HAPPY", value: 50m, startsAt: new TimeOnly(16, 0), endsAt: new TimeOnly(18, 0));

        Price(Engine(), [Line()], [happyHour], Context(at: new TimeOnly(15, 0)))
            .TotalDiscount.ShouldBe(0m);

        Price(Engine(), [Line()], [happyHour], Context(at: new TimeOnly(17, 0)))
            .TotalDiscount.ShouldBe(50m);
    }

    [Fact]
    public void An_offer_that_crosses_midnight_runs_at_both_ends_of_it()
    {
        // A window that ends before it starts crosses midnight, which is how a late-night offer
        // is written. Treating it as empty would silently switch every such offer off.
        var lateNight = Offer("NIGHT", value: 50m, startsAt: new TimeOnly(22, 0), endsAt: new TimeOnly(2, 0));

        Price(Engine(), [Line()], [lateNight], Context(at: new TimeOnly(23, 30))).TotalDiscount.ShouldBe(50m);
        Price(Engine(), [Line()], [lateNight], Context(at: new TimeOnly(1, 0))).TotalDiscount.ShouldBe(50m);
        Price(Engine(), [Line()], [lateNight], Context(at: new TimeOnly(12, 0))).TotalDiscount.ShouldBe(0m);
    }

    [Fact]
    public void An_offer_limited_to_certain_days_keeps_to_them()
    {
        // 27 August 2026 is a Thursday.
        var thursday = 1 << (int)DayOfWeek.Thursday;
        var monday = 1 << (int)DayOfWeek.Monday;

        Price(Engine(), [Line()], [Offer("T", value: 25m, daysOfWeek: thursday)], Context())
            .TotalDiscount.ShouldBe(25m);

        Price(Engine(), [Line()], [Offer("M", value: 25m, daysOfWeek: monday)], Context())
            .TotalDiscount.ShouldBe(0m);
    }

    [Fact]
    public void A_coupon_offer_is_off_until_somebody_produces_the_coupon()
    {
        // Which is what a coupon is.
        var offer = Offer("COUPON", value: 30m, couponCode: "EID26");

        Price(Engine(), [Line()], [offer], Context()).TotalDiscount.ShouldBe(0m);
        Price(Engine(), [Line()], [offer], Context(coupons: "EID26")).TotalDiscount.ShouldBe(30m);
        Price(Engine(), [Line()], [offer], Context(coupons: "eid26")).TotalDiscount.ShouldBe(30m, "case is not the point");
    }

    [Fact]
    public void An_offer_limited_to_a_branch_or_a_channel_keeps_to_it()
    {
        Price(Engine(), [Line()], [Offer("JED", value: 20m, branchId: Jeddah)], Context())
            .TotalDiscount.ShouldBe(0m, "no branch on this basket");

        Price(Engine(), [Line()], [Offer("JED", value: 20m, branchId: Jeddah)], Context(branchId: Jeddah))
            .TotalDiscount.ShouldBe(20m);

        Price(Engine(), [Line()], [Offer("WEB", value: 20m, channels: SalesChannel.Sales)], Context())
            .TotalDiscount.ShouldBe(0m, "this is a till, not a sales order");
    }

    [Fact]
    public void An_offer_switched_off_does_nothing()
    {
        Price(Engine(), [Line()], [Offer("OFF", value: 50m, isActive: false)], Context())
            .TotalDiscount.ShouldBe(0m);
    }

    [Fact]
    public void An_offer_that_would_break_the_floor_is_left_out_and_says_why()
    {
        // The whole point of the phase. Half off something costing 60 leaves a negative margin,
        // and the offer does not apply.
        //
        // A warning rather than a refusal, and the distinction cost a shop a morning to find: a
        // blocked message here stopped the till selling water at all because somebody had
        // misconfigured a promotion on it last week. The refusal belongs where it can be acted
        // on, which is the screen the offer was written on.
        var found = new List<AsapMessage>();

        var priced = Price(
            Engine(),
            [Line(unitPrice: 100m, unitCost: 60m)],
            [Offer("HALF", value: 50m)],
            Context(),
            floor: 0m,
            found);

        priced.TotalDiscount.ShouldBe(0m, "the offer did not apply");

        var told = found.Single();
        told.Code.Value.ShouldBe("PRM.OFFER.NOT_APPLIED");
        told.IsFailure.ShouldBeFalse("the shop keeps trading");
        told.Detail.ShouldNotBeNull().ShouldContain("HALF");
        told.Detail.ShouldContain("ITEM-1001");
        told.Target.Field.ShouldBe("Lines[1]");
    }

    [Fact]
    public void An_offer_that_clears_the_floor_applies_normally()
    {
        // Margin protection must not become a general objection to discounting.
        var found = new List<AsapMessage>();

        Price(
            Engine(),
            [Line(unitPrice: 100m, unitCost: 20m)],
            [Offer("HALF", value: 50m)],
            Context(),
            floor: 0m,
            found).TotalDiscount.ShouldBe(50m);

        found.ShouldBeEmpty();
    }

    [Fact]
    public void Stacked_offers_are_measured_against_the_floor_together()
    {
        // Two offers that each clear the floor alone can break it between them. Checking each
        // against the undiscounted price is exactly how that gets missed.
        //
        // Cost 60, price 100, floor 10%. Twenty off leaves 80 and a margin of 25%, which fits.
        // Twenty more leaves 60 and a margin of nothing, which does not.
        var found = new List<AsapMessage>();

        var priced = Price(
            Engine(),
            [Line(unitPrice: 100m, unitCost: 60m)],
            [Offer("A", value: 20m, priority: 2), Offer("B", value: 20m, priority: 1)],
            Context(),
            floor: 10m,
            found);

        priced.TotalDiscount.ShouldBe(20m, "the first fits, the second would go under");
        priced.Discounts.Single().OfferCode.ShouldBe("A");
        found.Count.ShouldBe(1);
    }

    [Fact]
    public void An_empty_basket_or_no_offers_produces_nothing()
    {
        Price(Engine(), [], [Offer("A")], Context()).TotalDiscount.ShouldBe(0m);
        Price(Engine(), [Line()], [], Context()).TotalDiscount.ShouldBe(0m);
    }
}
