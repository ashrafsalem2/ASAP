using ASAP.Modules.Sales.Orders;
using Shouldly;

namespace ASAP.Modules.Sales.Tests;

/// <summary>
/// Covers the figures a sales order stands on.
/// </summary>
/// <remarks>
/// Every one of these is read by something that matters: the discount by the account that makes it
/// visible, the net by the tax engine, and the shipped-but-unbilled figure by whoever is wondering
/// why the month looks thin.
/// </remarks>
public sealed class SalesLineTests
{
    private static SalesOrderLine Line(
        decimal quantity = 10m,
        decimal unitPrice = 100m,
        decimal discountPercent = 0m,
        decimal shipped = 0m,
        decimal invoiced = 0m)
        => new()
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            LineNo = 10,
            Type = SalesLineType.Item,
            ItemNo = "ITEM-1001",
            Description = "Desk lamp",
            Quantity = quantity,
            UnitPrice = unitPrice,
            DiscountPercent = discountPercent,
            QuantityShipped = shipped,
            QuantityInvoiced = invoiced,
        };

    private static SalesOrder Order(params SalesOrderLine[] lines)
    {
        var order = new SalesOrder
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            No = "SO-0001",
            CustomerNo = "C-0001",
            CustomerName = "Al Faisaliah Trading",
        };

        foreach (var line in lines)
        {
            order.Lines.Add(line);
        }

        return order;
    }

    [Fact]
    public void A_line_with_no_discount_is_simply_quantity_times_price()
    {
        var line = Line();

        line.NetUnitPrice.ShouldBe(100m);
        line.LineAmount.ShouldBe(1_000m);
        line.DiscountAmount.ShouldBe(0m);
    }

    [Fact]
    public void A_discount_reduces_the_line_and_is_still_reportable_on_its_own()
    {
        // The point of keeping the percentage rather than folding it into the price: the company
        // can answer how much it discounted, which a netted-down price cannot.
        var line = Line(discountPercent: 15m);

        line.NetUnitPrice.ShouldBe(85m);
        line.LineAmount.ShouldBe(850m);
        line.DiscountAmount.ShouldBe(150m);

        // And the two halves still add back to what was quoted at list.
        (line.LineAmount + line.DiscountAmount).ShouldBe(1_000m);
    }

    [Fact]
    public void A_full_discount_gives_the_goods_away_without_breaking_the_arithmetic()
    {
        var line = Line(discountPercent: 100m);

        line.NetUnitPrice.ShouldBe(0m);
        line.LineAmount.ShouldBe(0m);
        line.DiscountAmount.ShouldBe(1_000m);
    }

    [Fact]
    public void A_part_shipment_leaves_the_rest_to_go_and_the_sent_part_to_bill()
    {
        var line = Line(shipped: 6m);

        line.OutstandingToShip.ShouldBe(4m);
        line.ShippedNotInvoiced.ShouldBe(6m, "the customer has these and has not been asked to pay");
    }

    [Fact]
    public void Invoicing_what_went_leaves_the_order_open_for_the_rest()
    {
        var line = Line(shipped: 6m, invoiced: 6m);

        line.ShippedNotInvoiced.ShouldBe(0m);
        line.OutstandingToShip.ShouldBe(4m);
    }

    [Fact]
    public void Billing_ahead_of_despatch_shows_as_a_negative()
    {
        // The customer has been charged for goods they do not have. Worth seeing rather than
        // clamping to zero, which would make it look like nothing was wrong.
        var line = Line(shipped: 6m, invoiced: 10m);

        line.ShippedNotInvoiced.ShouldBe(-4m);
    }

    [Fact]
    public void An_order_is_finished_only_when_everything_has_gone_and_been_billed()
    {
        Order(Line()).HasOutstandingShipment.ShouldBeTrue();
        Order(Line(shipped: 10m)).HasOutstandingShipment.ShouldBeFalse();

        Order(Line(shipped: 10m)).HasOutstandingInvoice.ShouldBeTrue();
        Order(Line(shipped: 10m, invoiced: 10m)).HasOutstandingInvoice.ShouldBeFalse();
    }

    [Fact]
    public void One_line_still_waiting_holds_the_whole_order_open()
    {
        var order = Order(
            Line(shipped: 10m, invoiced: 10m),
            Line(quantity: 5m, shipped: 2m));

        order.HasOutstandingShipment.ShouldBeTrue();
        order.HasOutstandingInvoice.ShouldBeTrue();
    }

    [Fact]
    public void An_order_stops_being_editable_the_moment_anything_ships()
    {
        // What left the building is a fact. An order that could restate it afterwards would let
        // somebody quietly change what a customer received.
        foreach (var status in new[] { SalesOrderStatus.Open, SalesOrderStatus.Released })
        {
            var order = Order();
            order.Status = status;
            order.IsEditable.ShouldBeTrue($"{status} is still being prepared");
        }

        foreach (var status in new[]
                 {
                     SalesOrderStatus.PartiallyShipped,
                     SalesOrderStatus.Shipped,
                     SalesOrderStatus.Invoiced,
                     SalesOrderStatus.Cancelled,
                 })
        {
            var order = Order();
            order.Status = status;
            order.IsEditable.ShouldBeFalse($"{status} has already moved goods or money");
        }
    }

    [Fact]
    public void Discounts_survive_awkward_percentages()
    {
        // A third off an odd quantity. The two halves must still reconstruct the list value,
        // because that is what the revenue and discount accounts are posted from.
        foreach (var percent in new[] { 3m, 7.5m, 12.345m, 33.33m })
        {
            var line = Line(quantity: 7m, unitPrice: 19.99m, discountPercent: percent);

            (line.LineAmount + line.DiscountAmount)
                .ShouldBe(7m * 19.99m, $"{percent}% off must not lose value");
        }
    }
}
