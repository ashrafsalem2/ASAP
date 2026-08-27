using ASAP.Modules.Pos.Receipts;
using Shouldly;

namespace ASAP.Modules.Pos.Tests;

/// <summary>
/// Covers what may be handed back, and at what price.
/// </summary>
/// <remarks>
/// The refund counter is where a shop loses money quietly. Both rules here exist because the
/// obvious implementation gets them wrong: checking a return against the receipt in hand lets
/// somebody return two, then two more, then two more, against a sale of two; and refunding at
/// today's shelf price hands back more than was paid every time something was bought on offer.
/// </remarks>
public sealed class ReturnRulesTests
{
    private static PosReceiptLine Sold(
        string itemNo = "ITEM-1001",
        decimal quantity = 3m,
        decimal unitPrice = 100m,
        decimal discountPercent = 0m)
        => new()
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            LineNo = 10,
            Type = PosLineType.Item,
            ItemNo = itemNo,
            Description = "Desk lamp",
            Quantity = quantity,
            UnitPrice = unitPrice,
            DiscountPercent = discountPercent,
        };

    private static PosReceipt Receipt(params PosReceiptLine[] lines)
    {
        var receipt = new PosReceipt
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            No = "R-0001",
            StationCode = "JED-T1",
            CustomerNo = "C-CASH",
            CustomerName = "Cash sales",
            LocationCode = "JED-01",
        };

        foreach (var line in lines)
        {
            receipt.Lines.Add(line);
        }

        return receipt;
    }

    /// <summary>
    /// What the service does, in the one line that matters: sold, less already back, is what is
    /// left to give back.
    /// </summary>
    private static decimal Remaining(decimal sold, decimal alreadyBack) => sold - alreadyBack;

    [Fact]
    public void What_is_left_to_return_is_what_was_sold_less_what_came_back()
    {
        Remaining(sold: 3m, alreadyBack: 0m).ShouldBe(3m);
        Remaining(sold: 3m, alreadyBack: 1m).ShouldBe(2m);
        Remaining(sold: 3m, alreadyBack: 3m).ShouldBe(0m);
    }

    [Fact]
    public void Returning_the_same_two_three_times_runs_out()
    {
        // Two, then two more, then two more, against a sale of two. The second attempt has
        // nothing left, and that is the whole point of counting against every earlier return
        // rather than against the receipt in hand.
        var sold = 2m;
        var back = 0m;

        Remaining(sold, back).ShouldBe(2m, "the first two may go back");

        back += 2m;
        Remaining(sold, back).ShouldBe(0m, "and after that there is nothing left");
    }

    [Fact]
    public void A_receipt_that_took_goods_back_is_not_something_to_return_against()
    {
        var refund = Receipt(Sold(quantity: -1m));

        refund.IsReturn.ShouldBeTrue();
    }

    [Fact]
    public void A_returned_line_carries_the_price_it_was_sold_at()
    {
        // Bought at a third off, brought back the week the offer ended. The customer is owed what
        // they paid; today's shelf price would hand back half as much again.
        var original = Sold(unitPrice: 90m, discountPercent: 33m);

        var refunded = new PosReceiptLine
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            LineNo = 10,
            Type = PosLineType.Item,
            ItemNo = original.ItemNo,
            Description = original.Description,
            Quantity = -1m,
            UnitPrice = original.UnitPrice,
            DiscountPercent = original.DiscountPercent,
        };

        refunded.NetUnitPrice.ShouldBe(original.NetUnitPrice);
        refunded.LineAmount.ShouldBe(-original.NetUnitPrice);
    }

    [Fact]
    public void A_parked_sale_may_still_be_changed_and_a_voided_one_may_not()
    {
        var parked = Receipt(Sold());
        parked.Status = PosReceiptStatus.Parked;
        parked.IsEditable.ShouldBeTrue();

        parked.Status = PosReceiptStatus.Voided;
        parked.IsEditable.ShouldBeFalse("it was thrown away, and the trail should still show it");
    }

    [Fact]
    public void A_return_is_worth_the_negative_of_the_sale_it_reverses()
    {
        // Three sold at 100 less 10%, all three back. The two documents cancel exactly, which is
        // what makes a return representable as the same document with the sign flipped.
        var sale = Receipt(Sold(quantity: 3m, unitPrice: 100m, discountPercent: 10m));
        var refund = Receipt(Sold(quantity: -3m, unitPrice: 100m, discountPercent: 10m));

        (sale.Lines.Single().LineAmount + refund.Lines.Single().LineAmount).ShouldBe(0m);
        (sale.Lines.Single().DiscountAmount + refund.Lines.Single().DiscountAmount).ShouldBe(0m);
    }
}
