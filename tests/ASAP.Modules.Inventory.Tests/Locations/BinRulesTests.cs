using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Locations;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using Shouldly;

namespace ASAP.Modules.Inventory.Tests.Locations;

/// <summary>
/// Covers what a bin says when the shelf it names has not got the goods.
/// </summary>
/// <remarks>
/// The rule the whole design turns on: a bin is a refinement of a location, never a substitute.
/// The location still has the stock, so nothing about the valuation is in doubt -- what is wrong
/// is the record of which shelf it is standing on. That is a warning, not a refusal, because
/// blocking it would stop a picker holding the goods in their hand.
/// </remarks>
public sealed class BinRulesTests
{
    private static StockAvailability Availability()
        => new(new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All]));

    private static AsapMessage Find(Result result, string code)
    {
        var match = result.Messages.FirstOrDefault(m => m.Code.Value == code);

        match.ShouldNotBeNull(
            $"Expected {code}. Raised: "
            + string.Join(", ", result.Messages.Select(static m => m.Code.Value)));

        return match;
    }

    private static ItemView Item()
        => new("ITEM-1001", "Widget", ItemKind.Inventory, CostingMethod.Fifo, false, true, 12.00m, 0m);

    private static LocationView Where() => new("RUH-WH", "Riyadh warehouse", false, true);

    private static Bin Shelf(string code, int pickOrder = 0)
        => new() { Code = code, LocationId = Guid.Empty, PickOrder = pickOrder };

    private static MovementView Move(
        decimal quantity,
        decimal onHand,
        Bin? bin = null,
        decimal inBin = 0m,
        IReadOnlyList<string>? elsewhere = null)
        => new(1, Item(), Where(), quantity, onHand)
        {
            Bin = bin,
            BinQuantityOnHand = inBin,
            BinsHoldingIt = elsewhere ?? [],
        };

    [Fact]
    public void Taking_from_a_shelf_that_has_it_says_nothing()
    {
        var result = Availability().Check(
            [Move(-3m, onHand: 40m, bin: Shelf("A-01"), inBin: 10m)],
            companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();
        result.Messages.ShouldNotContain(static m => m.Code.Value.StartsWith("INV.BIN.", StringComparison.Ordinal));
    }

    [Fact]
    public void Taking_more_than_the_shelf_has_says_where_it_is_instead()
    {
        // The location has forty. The shelf has two. Nothing is short except the paperwork about
        // which shelf, so the picker is told where to walk rather than stopped.
        var result = Availability().Check(
            [Move(-5m, onHand: 40m, bin: Shelf("A-01"), inBin: 2m, elsewhere: ["B-02 (30)", "C-04 (8)"])],
            companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();

        var warning = Find(result, "INV.BIN.SHORT");
        warning.Severity.ShouldBe(MessageSeverity.Warning);
        warning.Detail.ShouldNotBeNull().ShouldContain("B-02 (30)");
    }

    [Fact]
    public void A_short_shelf_does_not_stop_the_movement()
    {
        // A refusal here would stop somebody who is holding the goods. The stock is at the
        // location; only the shelf record is wrong.
        var result = Availability().Check(
            [Move(-5m, onHand: 40m, bin: Shelf("A-01"), inBin: 0m, elsewhere: ["B-02 (40)"])],
            companyAllowsNegative: false);

        result.Failed.ShouldBeFalse();
    }

    [Fact]
    public void Stock_that_was_never_put_away_says_so_rather_than_pointing_nowhere()
    {
        // Received before the location started tracking bins. Every entry has a location and no
        // bin, so no shelf can be named and "look on these shelves" would list nothing.
        var result = Availability().Check(
            [Move(-5m, onHand: 40m, bin: Shelf("A-01"), inBin: 0m, elsewhere: [])],
            companyAllowsNegative: false);

        var warning = Find(result, "INV.BIN.NOT_PUT_AWAY");
        warning.Severity.ShouldBe(MessageSeverity.Warning);
        warning.Resolution.ShouldNotBeNull().ShouldContain("bins say where, not how much");
    }

    [Fact]
    public void Putting_goods_onto_a_shelf_is_never_short()
    {
        var result = Availability().Check(
            [Move(20m, onHand: 40m, bin: Shelf("A-01"), inBin: 0m)],
            companyAllowsNegative: false);

        result.Messages.ShouldNotContain(static m => m.Code.Value.StartsWith("INV.BIN.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_location_without_bins_is_not_asked_about_shelves()
    {
        var result = Availability().Check(
            [Move(-5m, onHand: 40m, bin: null)],
            companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();
        result.Messages.ShouldNotContain(static m => m.Code.Value.StartsWith("INV.BIN.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_shelf_being_short_is_a_different_question_from_the_location_being_short()
    {
        // Both wrong at once: the location has three and five were taken, and the shelf has none.
        // Two separate messages, because they have two separate answers -- order more, and go to
        // the right shelf.
        var result = Availability().Check(
            [Move(-5m, onHand: 3m, bin: Shelf("A-01"), inBin: 0m, elsewhere: ["B-02 (3)"])],
            companyAllowsNegative: true);

        Find(result, "INV.BIN.SHORT").ShouldNotBeNull();
        Find(result, "INV.STOCK.WENT_NEGATIVE").ShouldNotBeNull();
    }
}
