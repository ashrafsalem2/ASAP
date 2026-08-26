using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using Shouldly;

namespace ASAP.Modules.Inventory.Tests.Costing;

/// <summary>
/// Covers the rule the whole negative-stock design turns on: selling what is not there is a
/// business choice, but never a silent one.
/// </summary>
public sealed class StockAvailabilityTests
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

    private static ItemView Item(
        bool blocked = false,
        bool? allowNegative = null,
        ItemKind kind = ItemKind.Inventory,
        decimal unitCost = 12.00m,
        decimal reorderPoint = 0m)
        => new("ITEM-1001", "Widget", kind, CostingMethod.Fifo, blocked, allowNegative, unitCost, reorderPoint);

    private static LocationView Where(bool blocked = false, bool sellable = true)
        => new("RUH-SHOP", "Riyadh shop floor", blocked, sellable);

    private static MovementView Move(decimal quantity, decimal onHand, ItemView? item = null, LocationView? at = null)
        => new(1, item ?? Item(), at ?? Where(), quantity, onHand);

    [Fact]
    public void A_sale_covered_by_stock_passes_without_comment()
    {
        var result = Availability().Check([Move(-5, onHand: 20)], companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();
        result.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void A_receipt_never_needs_stock()
    {
        var result = Availability().Check([Move(10, onHand: 0)], companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Selling_more_than_is_there_is_refused_when_the_company_forbids_it()
    {
        var result = Availability().Check([Move(-30, onHand: 10)], companyAllowsNegative: false);

        result.Failed.ShouldBeTrue();

        var refusal = Find(result, "INV.STOCK.NEGATIVE_BLOCKED");
        refusal.Severity.ShouldBe(MessageSeverity.Blocked);
        refusal.Detail.ShouldNotBeNull().ShouldContain("30");
        refusal.Detail.ShouldContain("10");
        refusal.Resolution.ShouldNotBeNull().ShouldContain("Reduce the quantity to 10");
    }

    [Fact]
    public void Selling_more_than_is_there_is_permitted_when_the_company_allows_it()
    {
        // The requirement in full: the sale goes through, and the fact that part of its cost is an
        // estimate is stated rather than hidden.
        var result = Availability().Check([Move(-30, onHand: 10)], companyAllowsNegative: true);

        result.Succeeded.ShouldBeTrue();

        var warning = Find(result, "INV.STOCK.WENT_NEGATIVE");
        warning.Severity.ShouldBe(MessageSeverity.Warning);
        warning.Detail.ShouldNotBeNull().ShouldContain("-20");
        warning.Detail.ShouldContain("20");
        warning.Resolution.ShouldNotBeNull().ShouldContain("settle the estimate");
    }

    [Fact]
    public void An_item_can_allow_negative_stock_when_the_company_does_not()
    {
        // A shop may be happy to sell loose produce it can see on the shelf while refusing to do
        // the same for a serialised appliance, so the answer belongs on the item as well.
        var result = Availability().Check(
            [Move(-30, onHand: 10, item: Item(allowNegative: true))],
            companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();
        Find(result, "INV.STOCK.WENT_NEGATIVE");
    }

    [Fact]
    public void An_item_can_forbid_negative_stock_when_the_company_allows_it()
    {
        var result = Availability().Check(
            [Move(-30, onHand: 10, item: Item(allowNegative: false))],
            companyAllowsNegative: true);

        result.Failed.ShouldBeTrue();
        Find(result, "INV.STOCK.NEGATIVE_BLOCKED");
    }

    [Fact]
    public void Someone_with_the_override_can_push_past_the_block()
    {
        var result = Availability().Check(
            [Move(-30, onHand: 10)],
            companyAllowsNegative: false,
            heldOverridePermissions: new HashSet<string> { "Inventory.Stock.Override" });

        result.Succeeded.ShouldBeTrue();
        Find(result, "INV.STOCK.NEGATIVE_BLOCKED").Severity.ShouldBe(MessageSeverity.Warning);
    }

    [Fact]
    public void Stock_at_a_quarantine_location_cannot_be_sold()
    {
        // The goods exist and are counted in the valuation, but must not be promised to a customer
        // until they are checked. Refused separately from the quantity, which is fine here.
        var result = Availability().Check(
            [Move(-5, onHand: 100, at: Where(sellable: false))],
            companyAllowsNegative: true);

        result.Failed.ShouldBeTrue();
        Find(result, "INV.LOCATION.NOT_SELLABLE");
    }

    [Fact]
    public void Stock_can_still_be_transferred_out_of_a_quarantine_location()
    {
        // The rule is about selling, not about stock leaving. Transferring goods out of quarantine
        // is exactly how they legitimately leave it, and refusing that would strand every transfer
        // half way through its journey.
        var result = Availability().Check(
            [
                new MovementView(
                    1,
                    Item(),
                    Where(sellable: false),
                    -5,
                    100,
                    ASAP.Modules.Inventory.Ledger.ItemLedgerEntryType.TransferOut),
            ],
            companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_blocked_item_is_refused()
    {
        var result = Availability().Check([Move(-5, onHand: 100, item: Item(blocked: true))], false);

        result.Failed.ShouldBeTrue();
        Find(result, "INV.ITEM.BLOCKED");
    }

    [Fact]
    public void A_blocked_location_is_refused()
    {
        var result = Availability().Check([Move(-5, onHand: 100, at: Where(blocked: true))], false);

        result.Failed.ShouldBeTrue();
        Find(result, "INV.LOCATION.BLOCKED");
    }

    [Fact]
    public void A_movement_with_no_quantity_is_refused()
    {
        var result = Availability().Check([Move(0, onHand: 100)], false);

        result.Failed.ShouldBeTrue();
        Find(result, "INV.MOVEMENT.QUANTITY_ZERO");
    }

    [Fact]
    public void A_service_has_no_stock_to_run_out_of()
    {
        // Selling labour a hundred times over is not a stock problem, and warning about it would
        // be noise on every service line ever posted.
        var result = Availability().Check(
            [Move(-100, onHand: 0, item: Item(kind: ItemKind.Service))],
            companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();
        result.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void Crossing_the_reorder_point_warns_once()
    {
        var result = Availability().Check(
            [Move(-15, onHand: 20, item: Item(reorderPoint: 10))],
            companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();
        Find(result, "INV.ITEM.BELOW_REORDER_POINT").Detail.ShouldNotBeNull().ShouldContain("5");
    }

    [Fact]
    public void Selling_again_from_stock_already_below_the_point_does_not_warn_again()
    {
        // Warning on every subsequent sale would train people to ignore the warning entirely,
        // which costs more than not showing it.
        var result = Availability().Check(
            [Move(-2, onHand: 8, item: Item(reorderPoint: 10))],
            companyAllowsNegative: false);

        result.Messages.ShouldNotContain(m => m.Code.Value == "INV.ITEM.BELOW_REORDER_POINT");
    }

    [Fact]
    public void Every_problem_across_every_line_is_reported_at_once()
    {
        var result = Availability().Check(
            [
                new MovementView(1, Item(blocked: true), Where(), -5, 100),
                new MovementView(2, Item(), Where(), -30, 10),
                new MovementView(3, Item(), Where(), 0, 100),
            ],
            companyAllowsNegative: false);

        var codes = result.Failures.Select(static m => m.Code.Value).ToList();

        codes.ShouldContain("INV.ITEM.BLOCKED");
        codes.ShouldContain("INV.STOCK.NEGATIVE_BLOCKED");
        codes.ShouldContain("INV.MOVEMENT.QUANTITY_ZERO");
    }

    [Fact]
    public void Each_message_points_at_the_line_that_caused_it()
    {
        var result = Availability().Check(
            [
                new MovementView(1, Item(), Where(), -5, 100),
                new MovementView(2, Item(), Where(), -30, 10),
            ],
            companyAllowsNegative: false);

        Find(result, "INV.STOCK.NEGATIVE_BLOCKED").Target.Field.ShouldBe("Lines[2]");
    }
}
