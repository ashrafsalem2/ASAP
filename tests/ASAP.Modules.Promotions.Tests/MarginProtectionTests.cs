using ASAP.Modules.Promotions.Offers;
using ASAP.Modules.Promotions.Pricing;
using Shouldly;

namespace ASAP.Modules.Promotions.Tests;

/// <summary>
/// Covers the floor an offer cannot go below.
/// </summary>
/// <remarks>
/// This is the part of promotions that pays for itself. An offer is written weeks before it runs,
/// against costs that were true then, and suppliers put prices up. Checking the margin once at
/// design time is how a shop runs a loss-making fortnight and reports it afterwards.
/// </remarks>
public sealed class MarginProtectionTests
{
    private static BasketLine Line(
        decimal quantity = 1m,
        decimal unitPrice = 100m,
        decimal unitCost = 60m)
        => new(1, "ITEM-1001", null, quantity, unitPrice, unitCost);

    [Fact]
    public void Margin_is_measured_against_what_the_customer_pays()
    {
        // A gross margin, which is what every report the company already runs will compare it to.
        // Measuring against cost instead would report 66.67% on this and agree with nothing.
        MarginGuard.MarginPercent(unitPrice: 100m, unitCost: 60m).ShouldBe(40m);
    }

    [Fact]
    public void Selling_below_cost_is_a_negative_margin_rather_than_a_small_one()
    {
        MarginGuard.MarginPercent(unitPrice: 50m, unitCost: 60m).ShouldBe(-20m);
    }

    [Fact]
    public void Giving_something_away_is_a_total_loss_and_not_an_infinite_one()
    {
        // Dividing by a selling price of nothing has no answer, and reporting one would put a
        // figure on a screen that no arithmetic produced.
        MarginGuard.MarginPercent(unitPrice: 0m, unitCost: 60m).ShouldBe(-100m);
        MarginGuard.MarginPercent(unitPrice: 0m, unitCost: 0m).ShouldBe(0m);
    }

    [Fact]
    public void The_floor_price_is_the_margin_rearranged_not_the_cost_marked_up()
    {
        // Twenty per cent on a cost of eighty is a price of a hundred, not ninety-six. Working it
        // the other way is the mistake that makes a shop think it holds a margin it does not.
        MarginGuard.FloorPrice(unitCost: 80m, floorPercent: 20m).ShouldBe(100m);
        MarginGuard.MarginPercent(unitPrice: 96m, unitCost: 80m).ShouldNotBe(20m);
    }

    [Fact]
    public void A_floor_of_nothing_means_never_below_cost()
    {
        // The setting most shops want, and the one that ships.
        MarginGuard.FloorPrice(unitCost: 60m, floorPercent: 0m).ShouldBe(60m);

        MarginGuard.Check(Line(unitPrice: 60m), discount: 0m, floorPercent: 0m)
            .IsAcceptable.ShouldBeTrue("exactly at cost clears a floor of nothing");

        MarginGuard.Check(Line(unitPrice: 60m), discount: 0.01m, floorPercent: 0m)
            .IsAcceptable.ShouldBeFalse("a halala below does not");
    }

    [Fact]
    public void A_floor_of_everything_cannot_be_met_rather_than_dividing_by_nothing()
    {
        MarginGuard.FloorPrice(unitCost: 60m, floorPercent: 100m).ShouldBe(decimal.MaxValue);
    }

    [Fact]
    public void The_shortfall_says_how_big_the_hole_is_in_money()
    {
        // A percentage tells somebody there is a problem. Money tells them how big it is, and
        // that is the figure a decision gets made on.
        var check = MarginGuard.Check(Line(unitPrice: 100m, unitCost: 60m), discount: 50m, floorPercent: 0m);

        check.OfferUnitPrice.ShouldBe(50m);
        check.MarginPercent.ShouldBe(-20m);
        check.ShortfallPerUnit.ShouldBe(10m, "the floor price is 60 and the offer charges 50");
        check.IsAcceptable.ShouldBeFalse();
    }

    [Fact]
    public void A_shortfall_is_reported_per_unit_however_many_are_on_the_line()
    {
        // Somebody reading it is deciding about the offer, not about this basket. Per unit is
        // the figure that survives the basket it was noticed in.
        var check = MarginGuard.Check(
            Line(quantity: 10m, unitPrice: 100m, unitCost: 60m),
            discount: 500m,
            floorPercent: 0m);

        check.OfferUnitPrice.ShouldBe(50m);
        check.ShortfallPerUnit.ShouldBe(10m);
    }

    [Fact]
    public void An_offer_that_leaves_exactly_the_floor_is_accepted()
    {
        // Boundaries are where these get argued about, so the rule is stated: at the floor is
        // acceptable, below it is not.
        var check = MarginGuard.Check(
            Line(unitPrice: 100m, unitCost: 60m),
            discount: 25m,
            floorPercent: 20m);

        check.OfferUnitPrice.ShouldBe(75m);
        check.MarginPercent.ShouldBe(20m);
        check.IsAcceptable.ShouldBeTrue();
    }

    [Fact]
    public void A_generous_offer_on_a_high_margin_item_is_fine()
    {
        // Margin protection must not be a general objection to discounting. Half off something
        // that costs a fifth of its price is a perfectly good offer.
        var check = MarginGuard.Check(
            Line(unitPrice: 100m, unitCost: 20m),
            discount: 50m,
            floorPercent: 20m);

        check.MarginPercent.ShouldBe(60m);
        check.IsAcceptable.ShouldBeTrue();
    }

    [Fact]
    public void The_arguments_a_refusal_carries_name_every_figure()
    {
        // The message is only useful if it says what the item is, what it costs, what the offer
        // would charge and how far under that leaves it. Four figures, and all four have to be
        // there or the reader has to go and look them up.
        var offer = new Offer
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            Code = "SUMMER-25",
            Name = "Summer sale",
            Kind = OfferKind.Percentage,
            Scope = OfferScope.Everything,
            Value = 50m,
            StartsOn = new DateOnly(2026, 6, 1),
        };

        var check = MarginGuard.Check(Line(unitPrice: 100m, unitCost: 60m), 50m, floorPercent: 0m);
        var arguments = MarginGuard.Arguments(check, offer, "Desk lamp");

        arguments["OfferCode"].ShouldBe("SUMMER-25");
        arguments["ItemNo"].ShouldBe("ITEM-1001");
        arguments["Description"].ShouldBe("Desk lamp");
        arguments["UnitCost"].ShouldBe(60m);
        arguments["OfferPrice"].ShouldBe(50m);
        arguments["Shortfall"].ShouldBe(10m);
    }
}
