using ASAP.Modules.Purchasing.Orders;
using Shouldly;

namespace ASAP.Modules.Purchasing.Tests;

/// <summary>
/// Covers the arithmetic the three-way match turns on, without a database.
/// </summary>
/// <remarks>
/// <para>
/// Goods and paperwork arrive on their own schedules. A lorry brings eight of the ten ordered; the
/// invoice follows a fortnight later for all ten; a credit note follows that. Every one of these
/// tests is a state a real order passes through, and the point of holding quantity received and
/// quantity invoiced separately on the line is that all of them can be represented.
/// </para>
/// <para>
/// The figures here are what the accrual, the payable and every refusal are computed from, so
/// getting them wrong is not a display problem.
/// </para>
/// </remarks>
public sealed class ThreeWayMatchTests
{
    private static PurchaseOrderLine Line(decimal ordered, decimal received = 0m, decimal invoiced = 0m)
        => new()
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            LineNo = 10,
            Type = PurchaseLineType.Item,
            ItemNo = "ITEM-1001",
            Description = "Desk lamp",
            Quantity = ordered,
            DirectUnitCost = 12.50m,
            QuantityReceived = received,
            QuantityInvoiced = invoiced,
        };

    private static PurchaseOrder Order(params PurchaseOrderLine[] lines)
    {
        var order = new PurchaseOrder
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            No = "PO-0001",
            VendorNo = "V-0001",
            VendorName = "Gulf Office Supplies",
        };

        foreach (var line in lines)
        {
            order.Lines.Add(line);
        }

        return order;
    }

    [Fact]
    public void A_fresh_line_is_entirely_outstanding()
    {
        var line = Line(ordered: 10);

        line.OutstandingToReceive.ShouldBe(10m);
        line.ReceivedNotInvoiced.ShouldBe(0m);
        line.LineAmount.ShouldBe(125m);
    }

    [Fact]
    public void A_part_delivery_leaves_the_rest_outstanding_and_the_delivered_part_to_invoice()
    {
        // Eight of ten arrive. Two are still owed by the vendor; eight are owed to them.
        var line = Line(ordered: 10, received: 8);

        line.OutstandingToReceive.ShouldBe(2m);
        line.ReceivedNotInvoiced.ShouldBe(8m, "the company owes for these from the moment they land");
    }

    [Fact]
    public void Invoicing_what_arrived_clears_the_accrual_without_closing_the_order()
    {
        var line = Line(ordered: 10, received: 8, invoiced: 8);

        line.ReceivedNotInvoiced.ShouldBe(0m, "nothing is accrued once it is a real payable");
        line.OutstandingToReceive.ShouldBe(2m, "two are still to come");
    }

    [Fact]
    public void An_invoice_arriving_before_the_rest_of_the_goods_is_visible_as_such()
    {
        // The vendor billed for all ten and sent eight. The two are a negative accrual, which is
        // exactly right: the company has paid for goods it does not have.
        var line = Line(ordered: 10, received: 8, invoiced: 10);

        line.ReceivedNotInvoiced.ShouldBe(-2m);
    }

    [Fact]
    public void An_order_is_finished_only_when_everything_is_both_received_and_invoiced()
    {
        Order(Line(ordered: 10)).HasOutstandingReceipt.ShouldBeTrue();
        Order(Line(ordered: 10, received: 10)).HasOutstandingReceipt.ShouldBeFalse();

        Order(Line(ordered: 10, received: 10)).HasOutstandingInvoice.ShouldBeTrue();
        Order(Line(ordered: 10, received: 10, invoiced: 10)).HasOutstandingInvoice.ShouldBeFalse();
    }

    [Fact]
    public void One_line_still_waiting_holds_the_whole_order_open()
    {
        // Orders are judged line by line. An order closed because most of it arrived is an order
        // whose remaining line nobody ever chases.
        var order = Order(
            Line(ordered: 10, received: 10, invoiced: 10),
            Line(ordered: 5, received: 2));

        order.HasOutstandingReceipt.ShouldBeTrue();
        order.HasOutstandingInvoice.ShouldBeTrue();
    }

    [Fact]
    public void An_order_stops_being_editable_the_moment_goods_arrive()
    {
        // What has arrived is a fact about the world, not a figure the order gets to restate.
        // Editing a line goods have landed against would silently change what was received.
        Order().Status = PurchaseOrderStatus.Open;

        foreach (var status in new[] { PurchaseOrderStatus.Open, PurchaseOrderStatus.Released })
        {
            var order = Order();
            order.Status = status;
            order.IsEditable.ShouldBeTrue($"{status} is still being prepared or awaited");
        }

        foreach (var status in new[]
                 {
                     PurchaseOrderStatus.PartiallyReceived,
                     PurchaseOrderStatus.Received,
                     PurchaseOrderStatus.Invoiced,
                     PurchaseOrderStatus.Cancelled,
                 })
        {
            var order = Order();
            order.Status = status;
            order.IsEditable.ShouldBeFalse($"{status} has already moved goods or money");
        }
    }

    [Fact]
    public void A_cost_line_carries_no_item_and_still_totals()
    {
        // Rent, fees, a subscription. It reaches the general ledger directly and never touches
        // the item ledger, so it has no location and needs none.
        var line = new PurchaseOrderLine
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            LineNo = 10,
            Type = PurchaseLineType.GlAccount,
            AccountNo = "6200",
            Description = "Office rent, August",
            Quantity = 1m,
            DirectUnitCost = 12_000m,
        };

        line.ItemNo.ShouldBeNull();
        line.LineAmount.ShouldBe(12_000m);
        line.OutstandingToReceive.ShouldBe(1m);
    }
}
