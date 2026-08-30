using System.Reflection;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// Every document line that becomes a stock movement has to be able to say which variant.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the gap it checks for was shipped. Variants were added to Inventory as an
/// opt-in per item, which contained them neatly inside that module and not at all across the
/// boundary: a purchase order line had nowhere to record a colour, so the first attempt to receive
/// a variant item was refused at the goods-in door on an order that should never have been
/// raisable. Sales and the till had the same hole.
/// </para>
/// <para>
/// The failure mode is what makes it worth a test rather than a note. Nothing breaks until somebody
/// turns variants on for an item that one of these documents handles, and then that document stops
/// working entirely -- with a message about inventory, raised from a screen that has no field to
/// fix it with.
/// </para>
/// <para>
/// So: any entity representing a line that carries an item number into the item ledger must also
/// carry a variant code. A new document type gets this test failing on the day it is written rather
/// than on the day a customer stocks their first shirt.
/// </para>
/// </remarks>
public sealed class VariantReachesEveryStockPathTests
{
    /// <summary>The line types that end up as stock movements.</summary>
    /// <remarks>
    /// Named rather than discovered, because "has an ItemNo" is true of plenty of things that never
    /// move stock, and a test that guessed would either miss the real ones or complain about
    /// reports. Adding a document here is the deliberate act of saying it moves stock.
    /// </remarks>
    private static readonly (string Assembly, string TypeName)[] StockCarryingLines =
    [
        ("ASAP.Modules.Purchasing", "ASAP.Modules.Purchasing.Orders.PurchaseOrderLine"),
        ("ASAP.Modules.Sales", "ASAP.Modules.Sales.Orders.SalesOrderLine"),
        ("ASAP.Modules.Pos", "ASAP.Modules.Pos.Receipts.PosReceiptLine"),
    ];

    [Fact]
    public void Every_document_line_that_moves_stock_can_say_which_variant()
    {
        var missing = new List<string>();

        foreach (var (assemblyName, typeName) in StockCarryingLines)
        {
            var type = Assembly.Load(assemblyName).GetType(typeName);

            type.ShouldNotBeNull($"{typeName} was not found in {assemblyName}.");

            var carriesItem = type.GetProperty("ItemNo") is not null;
            var carriesVariant = type.GetProperty("VariantCode") is not null;

            if (carriesItem && !carriesVariant)
            {
                missing.Add($"{typeName} has an ItemNo and no VariantCode");
            }
        }

        missing.ShouldBeEmpty(
            "a document line that carries an item into the item ledger but cannot say which "
            + "variant stops working the day somebody stocks that item in two colours, and the "
            + "refusal arrives from Inventory on a screen with no field to answer it:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void The_stock_movement_itself_still_takes_one()
    {
        // The thing all of the above feed. If this ever loses the parameter the others are
        // pointless, and the compiler would not say so because they pass it by name.
        var request = Assembly.Load("ASAP.Modules.Inventory")
            .GetType("ASAP.Modules.Inventory.Posting.StockMovementRequest");

        request.ShouldNotBeNull();
        request.GetProperty("VariantCode").ShouldNotBeNull();
    }
}
