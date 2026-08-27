using ASAP.Modules.Promotions.Offers;
using ASAP.Modules.Promotions.Pricing;
using Shouldly;

namespace ASAP.Modules.Promotions.Tests;

/// <summary>
/// Covers what each kind of offer takes off.
/// </summary>
/// <remarks>
/// Every figure here is one a customer can check on a receipt, and several of them are ones a
/// customer will check. Three for two is the obvious example: getting it wrong by one unit is a
/// complaint at a counter, and getting it wrong in the shop's favour is a complaint somewhere
/// worse than that.
/// </remarks>
public sealed class OfferCalculatorTests
{
    private static BasketLine Line(
        decimal quantity = 1m,
        decimal unitPrice = 100m,
        decimal unitCost = 60m,
        decimal manualDiscountPercent = 0m)
        => new(1, "ITEM-1001", null, quantity, unitPrice, unitCost, manualDiscountPercent);

    private static Offer Offer(
        OfferKind kind,
        decimal value = 0m,
        decimal buy = 0m,
        decimal get = 0m,
        decimal getDiscountPercent = 100m)
        => new()
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            Code = "OFFER-1",
            Name = "Test offer",
            Kind = kind,
            Scope = OfferScope.Everything,
            Value = value,
            BuyQuantity = buy,
            GetQuantity = get,
            GetDiscountPercent = getDiscountPercent,
            StartsOn = new DateOnly(2026, 1, 1),
        };

    private static decimal Discount(Offer offer, BasketLine line)
        => OfferCalculator.DiscountFor(offer, line, OfferCalculator.NetAmount(line));

    [Fact]
    public void A_percentage_comes_off_the_line()
    {
        Discount(Offer(OfferKind.Percentage, 25m), Line(quantity: 4m)).ShouldBe(100m);
    }

    [Fact]
    public void A_percentage_applies_after_a_discount_the_cashier_already_gave()
    {
        // Both decisions were made. A promotion that ignored the cashier's would overcharge, and
        // one that replaced it would silently overrule a person who was standing there.
        var line = Line(quantity: 2m, manualDiscountPercent: 10m);

        OfferCalculator.NetAmount(line).ShouldBe(180m);
        Discount(Offer(OfferKind.Percentage, 50m), line).ShouldBe(90m);
    }

    [Fact]
    public void An_amount_off_applies_to_every_unit()
    {
        Discount(Offer(OfferKind.AmountPerUnit, 15m), Line(quantity: 3m)).ShouldBe(45m);
    }

    [Fact]
    public void Three_for_two_gives_one_free_in_every_three()
    {
        var offer = Offer(OfferKind.BuyXGetY, buy: 2m, get: 1m);

        Discount(offer, Line(quantity: 3m)).ShouldBe(100m, "one of the three is free");
        Discount(offer, Line(quantity: 6m)).ShouldBe(200m, "two complete deals");
    }

    [Fact]
    public void A_part_deal_is_not_a_part_discount()
    {
        // Four items on a three-for-two is one deal and one at full price, not one and a third
        // deals. Prorating here is the mistake that gives away a third of an item on every basket.
        var offer = Offer(OfferKind.BuyXGetY, buy: 2m, get: 1m);

        Discount(offer, Line(quantity: 4m)).ShouldBe(100m);
        Discount(offer, Line(quantity: 2m)).ShouldBe(0m, "not enough for a deal at all");
    }

    [Fact]
    public void A_half_price_second_one_is_the_same_shape()
    {
        // Buy one, get the second half price. The machinery does not need a new kind for it.
        var offer = Offer(OfferKind.BuyXGetY, buy: 1m, get: 1m, getDiscountPercent: 50m);

        Discount(offer, Line(quantity: 2m)).ShouldBe(50m);
    }

    [Fact]
    public void A_threshold_applies_only_once_the_qualifying_lines_reach_it()
    {
        var offer = Offer(OfferKind.Threshold, value: 500m, getDiscountPercent: 10m);
        var line = Line(quantity: 3m);

        OfferCalculator.DiscountFor(offer, line, qualifyingAmount: 400m)
            .ShouldBe(0m, "the basket has not reached the threshold");

        OfferCalculator.DiscountFor(offer, line, qualifyingAmount: 600m)
            .ShouldBe(30m, "ten per cent of this line, once the threshold is met");
    }

    [Fact]
    public void A_fixed_price_takes_off_whatever_the_difference_is()
    {
        // A meal deal: the qualifying quantity costs this, whatever the lines add up to.
        Discount(Offer(OfferKind.FixedPrice, value: 250m), Line(quantity: 3m)).ShouldBe(50m);
    }

    [Fact]
    public void A_fixed_price_above_the_line_takes_nothing_off()
    {
        // And certainly does not add to it.
        Discount(Offer(OfferKind.FixedPrice, value: 400m), Line(quantity: 3m)).ShouldBe(0m);
    }

    [Fact]
    public void No_offer_can_take_a_line_below_nothing()
    {
        // An offer that took a line negative would be paying the customer to shop. No offer means
        // that, and every rounding error eventually tries it.
        Discount(Offer(OfferKind.AmountPerUnit, 500m), Line(quantity: 2m)).ShouldBe(200m);
        Discount(Offer(OfferKind.Percentage, 100m), Line(quantity: 2m)).ShouldBe(200m);
    }

    [Fact]
    public void A_line_with_nothing_on_it_discounts_nothing()
    {
        Discount(Offer(OfferKind.Percentage, 50m), Line(quantity: 0m)).ShouldBe(0m);
        Discount(Offer(OfferKind.Percentage, 50m), Line(unitPrice: 0m)).ShouldBe(0m);
    }

    [Fact]
    public void An_incomplete_buy_get_deal_does_nothing_rather_than_dividing_by_nothing()
    {
        Discount(Offer(OfferKind.BuyXGetY, buy: 0m, get: 1m), Line(quantity: 5m)).ShouldBe(0m);
        Discount(Offer(OfferKind.BuyXGetY, buy: 2m, get: 0m), Line(quantity: 5m)).ShouldBe(0m);
    }
}
