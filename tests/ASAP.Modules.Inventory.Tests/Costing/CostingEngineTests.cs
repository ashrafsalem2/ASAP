using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using Shouldly;

namespace ASAP.Modules.Inventory.Tests.Costing;

/// <summary>
/// Covers what leaving stock costs.
///
/// Costing is the part of an ERP that goes wrong quietly. Nothing errors, the numbers drift, and
/// someone finds the discrepancy at year end unable to say when it began. So every rule is pinned
/// here rather than left to inspection.
/// </summary>
public sealed class CostingEngineTests
{
    private static readonly Guid FirstReceipt = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid SecondReceipt = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid ThirdReceipt = Guid.Parse("33333333-0000-0000-0000-000000000003");

    private static InboundLayer Layer(Guid id, string date, decimal quantity, decimal unitCost)
        => new(id, DateOnly.Parse(date), quantity, unitCost);

    // ---- FIFO ----

    [Fact]
    public void Fifo_takes_from_the_oldest_receipt_first()
    {
        var outcome = CostingEngine.ApplyOutbound(
            quantity: 30,
            layers:
            [
                Layer(SecondReceipt, "2026-02-01", 100, 13.00m),
                Layer(FirstReceipt, "2026-01-01", 100, 12.00m),
            ],
            CostingMethod.Fifo,
            fallbackUnitCost: 0m);

        var application = outcome.Applications.ShouldHaveSingleItem();
        application.InboundEntryId.ShouldBe(FirstReceipt);
        application.UnitCost.ShouldBe(12.00m);
        outcome.TotalCost.ShouldBe(-360.00m);
        outcome.WentNegative.ShouldBeFalse();
    }

    [Fact]
    public void Fifo_spans_receipts_when_one_is_not_enough()
    {
        // The case that makes the breakdown worth keeping: this sale cost two different prices,
        // and a report should be able to say which forty came from where.
        var outcome = CostingEngine.ApplyOutbound(
            quantity: 50,
            layers:
            [
                Layer(FirstReceipt, "2026-01-08", 40, 12.50m),
                Layer(SecondReceipt, "2026-01-12", 60, 13.00m),
            ],
            CostingMethod.Fifo,
            fallbackUnitCost: 0m);

        outcome.Applications.Count.ShouldBe(2);
        outcome.Applications[0].Quantity.ShouldBe(40);
        outcome.Applications[0].CostAmount.ShouldBe(500.00m);
        outcome.Applications[1].Quantity.ShouldBe(10);
        outcome.Applications[1].CostAmount.ShouldBe(130.00m);
        outcome.TotalCost.ShouldBe(-630.00m);
    }

    [Fact]
    public void Fifo_breaks_a_same_day_tie_the_same_way_every_time()
    {
        // Two receipts on one day would otherwise be consumed in whatever order the database
        // happened to return, so the same sale could cost two different amounts on two runs and
        // nobody could explain either.
        List<InboundLayer> layers =
        [
            Layer(ThirdReceipt, "2026-01-01", 10, 30.00m),
            Layer(FirstReceipt, "2026-01-01", 10, 10.00m),
            Layer(SecondReceipt, "2026-01-01", 10, 20.00m),
        ];

        var first = CostingEngine.ApplyOutbound(15, layers, CostingMethod.Fifo, 0m);
        var second = CostingEngine.ApplyOutbound(15, [.. layers.AsEnumerable().Reverse()], CostingMethod.Fifo, 0m);

        first.TotalCost.ShouldBe(second.TotalCost);
        first.Applications[0].InboundEntryId.ShouldBe(FirstReceipt);
    }

    [Fact]
    public void Fifo_ignores_a_receipt_that_has_been_fully_consumed()
    {
        var outcome = CostingEngine.ApplyOutbound(
            quantity: 5,
            layers:
            [
                Layer(FirstReceipt, "2026-01-01", 0, 12.00m),
                Layer(SecondReceipt, "2026-02-01", 10, 13.00m),
            ],
            CostingMethod.Fifo,
            fallbackUnitCost: 0m);

        outcome.Applications.ShouldHaveSingleItem().InboundEntryId.ShouldBe(SecondReceipt);
    }

    // ---- Negative stock ----

    [Fact]
    public void Selling_with_nothing_on_hand_is_allowed_and_marked_as_an_estimate()
    {
        // ASAP permits this on purpose: a shop that can see the goods on the shelf should not be
        // stopped from selling them by paperwork that has not caught up.
        var outcome = CostingEngine.ApplyOutbound(
            quantity: 10,
            layers: [],
            CostingMethod.Fifo,
            fallbackUnitCost: 12.00m);

        outcome.WentNegative.ShouldBeTrue();
        outcome.ShortfallQuantity.ShouldBe(10);

        var application = outcome.Applications.ShouldHaveSingleItem();
        application.IsEstimate.ShouldBeTrue();
        application.InboundEntryId.ShouldBeNull();
        application.UnitCost.ShouldBe(12.00m);
        outcome.TotalCost.ShouldBe(-120.00m);
    }

    [Fact]
    public void A_partly_covered_sale_costs_the_real_price_for_what_existed_and_estimates_the_rest()
    {
        var outcome = CostingEngine.ApplyOutbound(
            quantity: 30,
            layers: [Layer(FirstReceipt, "2026-01-01", 10, 12.00m)],
            CostingMethod.Fifo,
            fallbackUnitCost: 15.00m);

        outcome.ShortfallQuantity.ShouldBe(20);
        outcome.Applications.Count.ShouldBe(2);

        outcome.Applications[0].IsEstimate.ShouldBeFalse();
        outcome.Applications[0].CostAmount.ShouldBe(120.00m);

        outcome.Applications[1].IsEstimate.ShouldBeTrue();
        outcome.Applications[1].CostAmount.ShouldBe(300.00m);

        // Only the estimated part is held back from the general ledger. The 120.00 that came from
        // real stock is settled and posts immediately.
        outcome.EstimatedCost.ShouldBe(300.00m);
        outcome.TotalCost.ShouldBe(-420.00m);
    }

    [Fact]
    public void Settling_an_estimate_posts_only_the_difference()
    {
        // Estimated at 12.00, actually cost 13.50, ten units. The books already carry 120.00, so
        // the correction is the 15.00 that was missing -- not the whole 135.00 again.
        var correction = CostingEngine.SettleEstimate(
            estimatedUnitCost: 12.00m,
            actualUnitCost: 13.50m,
            quantity: 10);

        correction.ShouldBe(-15.00m);
    }

    [Fact]
    public void Settling_costs_less_than_estimated_gives_value_back()
    {
        var correction = CostingEngine.SettleEstimate(
            estimatedUnitCost: 15.00m,
            actualUnitCost: 12.00m,
            quantity: 10);

        correction.ShouldBe(30.00m);
    }

    [Fact]
    public void An_estimate_that_was_right_needs_no_correction_at_all()
    {
        // Worth asserting rather than assuming: a settlement routine that posts a zero-value entry
        // for every accurate estimate fills the ledger with rows that say nothing.
        CostingEngine.SettleEstimate(12.00m, 12.00m, 10).ShouldBe(0m);
    }

    // ---- Average ----

    [Fact]
    public void Average_values_everything_at_the_weighted_average_of_what_is_on_hand()
    {
        // 100 at 10.00 and 100 at 12.00 average 11.00, not the 10.00 a naive first-in reading gives.
        var outcome = CostingEngine.ApplyOutbound(
            quantity: 50,
            layers:
            [
                Layer(FirstReceipt, "2026-01-01", 100, 10.00m),
                Layer(SecondReceipt, "2026-02-01", 100, 12.00m),
            ],
            CostingMethod.Average,
            fallbackUnitCost: 0m);

        outcome.Applications.ShouldAllBe(a => a.UnitCost == 11.00m);
        outcome.TotalCost.ShouldBe(-550.00m);
    }

    [Fact]
    public void Average_weights_by_quantity_rather_than_treating_receipts_equally()
    {
        // 90 at 10.00 and 10 at 20.00 average 11.00. Averaging the two prices instead would give
        // 15.00 and overstate the cost of every sale by a third.
        CostingEngine
            .AverageUnitCost(
            [
                Layer(FirstReceipt, "2026-01-01", 90, 10.00m),
                Layer(SecondReceipt, "2026-02-01", 10, 20.00m),
            ])
            .ShouldBe(11.00m);
    }

    [Fact]
    public void Average_falls_back_to_the_item_cost_when_nothing_is_on_hand()
    {
        var outcome = CostingEngine.ApplyOutbound(
            quantity: 5,
            layers: [],
            CostingMethod.Average,
            fallbackUnitCost: 9.75m);

        outcome.Applications.ShouldHaveSingleItem().UnitCost.ShouldBe(9.75m);
        outcome.WentNegative.ShouldBeTrue();
    }

    [Fact]
    public void Average_of_nothing_is_zero_rather_than_a_division_by_zero()
    {
        CostingEngine.AverageUnitCost([]).ShouldBe(0m);
    }

    // ---- Standard ----

    [Fact]
    public void Standard_uses_the_fixed_cost_and_never_looks_at_the_receipts()
    {
        var outcome = CostingEngine.ApplyOutbound(
            quantity: 10,
            layers: [Layer(FirstReceipt, "2026-01-01", 100, 99.00m)],
            CostingMethod.Standard,
            fallbackUnitCost: 50.00m,
            standardCost: 12.00m);

        outcome.TotalCost.ShouldBe(-120.00m);
        outcome.Applications.ShouldHaveSingleItem().UnitCost.ShouldBe(12.00m);
    }

    [Fact]
    public void Standard_costing_never_produces_an_estimate_even_with_no_stock()
    {
        // The cost of a sale at standard is known before anything is bought, so selling into
        // negative stock is exact rather than a guess waiting to be settled.
        var outcome = CostingEngine.ApplyOutbound(
            quantity: 10,
            layers: [],
            CostingMethod.Standard,
            fallbackUnitCost: 0m,
            standardCost: 12.00m);

        outcome.WentNegative.ShouldBeFalse();
        outcome.EstimatedCost.ShouldBe(0m);
    }

    // ---- Rounding ----

    [Fact]
    public void Rounds_money_away_from_zero_rather_than_to_even()
    {
        // 3 at 0.125 is 0.375, which .NET's default banker's rounding turns into 0.38 here but
        // 0.12 for 0.125 alone. Accounting expects away-from-zero throughout; rounding half the
        // half-fils downwards accumulates a bias nobody can account for.
        var outcome = CostingEngine.ApplyOutbound(
            quantity: 1,
            layers: [Layer(FirstReceipt, "2026-01-01", 10, 0.125m)],
            CostingMethod.Fifo,
            fallbackUnitCost: 0m);

        outcome.Applications.ShouldHaveSingleItem().CostAmount.ShouldBe(0.13m);
    }

    [Fact]
    public void Keeps_five_decimals_on_a_unit_cost_that_divides_awkwardly()
    {
        // A price per thousand divides down to fractions of a fils, and truncating the unit cost
        // to two decimals there loses real money once it is multiplied back up by the quantity.
        CostingEngine
            .AverageUnitCost(
            [
                Layer(FirstReceipt, "2026-01-01", 3, 10.00m),
                Layer(SecondReceipt, "2026-02-01", 4, 11.00m),
            ])
            .ShouldBe(10.57143m);
    }

    // ---- Guards ----

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Refuses_an_outbound_quantity_that_is_not_positive(decimal quantity)
    {
        // The sign is applied when the entry is written, so a negative here means a caller has
        // already applied it once and would otherwise have it applied twice.
        Should.Throw<ArgumentOutOfRangeException>(
            () => CostingEngine.ApplyOutbound(quantity, [], CostingMethod.Fifo, 10m));
    }

    [Fact]
    public void Refuses_a_settlement_quantity_that_is_not_positive()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CostingEngine.SettleEstimate(10m, 12m, 0));
    }
}
