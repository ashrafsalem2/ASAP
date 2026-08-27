using ASAP.Modules.Pos.Receipts;
using ASAP.Modules.Pos.Sessions;
using Shouldly;

namespace ASAP.Modules.Pos.Tests;

/// <summary>
/// Covers what should be in the drawer, and what the difference means when it is not.
/// </summary>
/// <remarks>
/// Every figure here is one somebody counts by hand at the end of a shift and is answerable for.
/// The arithmetic being right is the difference between a cashier being trusted and a cashier
/// being asked to explain a shortfall the system invented.
/// </remarks>
public sealed class CashDrawerTests
{
    private static PosSession Session(
        decimal openingFloat = 500m,
        decimal cashTendered = 0m,
        decimal changeGiven = 0m,
        decimal cashRefunded = 0m,
        decimal cardTaken = 0m,
        decimal? declaredCash = null)
        => new()
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            No = "Z-0001",
            StationCode = "JED-T1",
            OpeningFloat = openingFloat,
            CashTendered = cashTendered,
            ChangeGiven = changeGiven,
            CashRefunded = cashRefunded,
            CardTaken = cardTaken,
            DeclaredCash = declaredCash,
        };

    [Fact]
    public void An_untouched_drawer_holds_exactly_its_float()
    {
        Session().ExpectedCash.ShouldBe(500m);
    }

    [Fact]
    public void Card_takings_are_not_in_the_drawer()
    {
        // The mistake this guards against makes every till look short by the day's card sales,
        // which is both alarming and entirely the system's fault.
        var session = Session(cashTendered: 200m, cardTaken: 3_000m);

        session.ExpectedCash.ShouldBe(700m);
    }

    [Fact]
    public void Change_handed_back_leaves_the_drawer_again()
    {
        // A hundred-riyal note for forty riyals of shopping puts 100 in and takes 60 out. The
        // drawer keeps 40, and counting the note would say it keeps 100.
        var session = Session(openingFloat: 0m, cashTendered: 100m, changeGiven: 60m);

        session.ExpectedCash.ShouldBe(40m);
    }

    [Fact]
    public void A_refund_paid_in_cash_takes_money_out()
    {
        var session = Session(cashTendered: 300m, cashRefunded: 75m);

        session.ExpectedCash.ShouldBe(725m);
    }

    [Fact]
    public void A_drawer_that_agrees_has_no_variance()
    {
        var session = Session(cashTendered: 1_200m, changeGiven: 200m, declaredCash: 1_500m);

        session.ExpectedCash.ShouldBe(1_500m);
        session.Variance.ShouldBe(0m);
    }

    [Fact]
    public void Short_is_negative_and_over_is_positive()
    {
        // Both are worth seeing. A till repeatedly over is not an honest till that got lucky, it
        // is one where somebody is mis-keying, and a sign that hid the direction would lose that.
        Session(cashTendered: 1_000m, declaredCash: 1_460m).Variance.ShouldBe(-40m);
        Session(cashTendered: 1_000m, declaredCash: 1_530m).Variance.ShouldBe(30m);
    }

    [Fact]
    public void An_open_drawer_has_no_variance_yet()
    {
        // Null rather than zero. Nobody has counted it, which is not the same as counting it and
        // finding it right, and a screen showing a confident zero would be lying.
        Session(cashTendered: 900m).Variance.ShouldBeNull();
    }

    [Fact]
    public void Gross_sales_are_what_the_customers_paid()
    {
        var session = Session();
        session.NetSales = 1_000m;
        session.TaxAmount = 150m;

        session.GrossSales.ShouldBe(1_150m);
    }

    [Fact]
    public void A_closed_session_takes_no_more_receipts()
    {
        var session = Session();
        session.IsOpen.ShouldBeTrue();

        session.Status = PosSessionStatus.Closed;
        session.IsOpen.ShouldBeFalse();
    }
}

/// <summary>
/// Covers what a receipt comes to and what comes back.
/// </summary>
public sealed class ReceiptArithmeticTests
{
    private static PosReceiptLine Line(
        decimal quantity = 2m,
        decimal unitPrice = 50m,
        decimal discountPercent = 0m)
        => new()
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            LineNo = 10,
            Type = PosLineType.Item,
            ItemNo = "ITEM-1001",
            Description = "Desk lamp",
            Quantity = quantity,
            UnitPrice = unitPrice,
            DiscountPercent = discountPercent,
        };

    private static PosReceipt Receipt(
        decimal netAmount = 100m,
        decimal taxAmount = 15m,
        decimal roundingAmount = 0m,
        params PosTender[] tenders)
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
            NetAmount = netAmount,
            TaxAmount = taxAmount,
            RoundingAmount = roundingAmount,
        };

        foreach (var tender in tenders)
        {
            receipt.Tenders.Add(tender);
        }

        return receipt;
    }

    private static PosTender Tender(TenderKind kind, decimal amount)
        => new()
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            LineNo = 1,
            Kind = kind,
            Amount = amount,
        };

    [Fact]
    public void A_line_with_no_discount_is_quantity_times_price()
    {
        var line = Line();

        line.NetUnitPrice.ShouldBe(50m);
        line.LineAmount.ShouldBe(100m);
        line.DiscountAmount.ShouldBe(0m);
    }

    [Fact]
    public void A_discount_is_still_reportable_after_it_is_given()
    {
        var line = Line(discountPercent: 20m);

        line.LineAmount.ShouldBe(80m);
        line.DiscountAmount.ShouldBe(20m);

        // And the two halves reconstruct the shelf price, which is what the revenue and discount
        // accounts are posted from.
        (line.LineAmount + line.DiscountAmount).ShouldBe(100m);
    }

    [Fact]
    public void A_returned_line_is_a_negative_quantity_and_nothing_else()
    {
        // The whole reason a return is not its own document: the arithmetic already works.
        var line = Line(quantity: -2m, discountPercent: 20m);

        line.LineAmount.ShouldBe(-80m);
        line.DiscountAmount.ShouldBe(-20m);
    }

    [Fact]
    public void The_total_includes_tax_and_rounding()
    {
        Receipt(netAmount: 100m, taxAmount: 15m, roundingAmount: 0.02m)
            .TotalAmount.ShouldBe(115.02m);
    }

    [Fact]
    public void Paying_exactly_leaves_nothing_outstanding_and_nothing_back()
    {
        var receipt = Receipt(tenders: Tender(TenderKind.Cash, 115m));

        receipt.TenderedAmount.ShouldBe(115m);
        receipt.OutstandingAmount.ShouldBe(0m);
    }

    [Fact]
    public void Overpaying_shows_as_a_negative_outstanding_which_is_the_change()
    {
        // Not clamped at zero. A till that could not see this could not work out what to hand
        // back, which is the one arithmetic question a cashier cannot do without.
        var receipt = Receipt(tenders: Tender(TenderKind.Cash, 200m));

        receipt.OutstandingAmount.ShouldBe(-85m);
    }

    [Fact]
    public void Several_tenders_add_up()
    {
        // A sixty-riyal gift card and the rest in cash. A single-tender model turns this ordinary
        // transaction into two receipts and a story.
        var receipt = Receipt(
            tenders:
            [
                Tender(TenderKind.Voucher, 60m),
                Tender(TenderKind.Cash, 55m),
            ]);

        receipt.TenderedAmount.ShouldBe(115m);
        receipt.OutstandingAmount.ShouldBe(0m);
    }

    [Fact]
    public void A_receipt_that_takes_goods_back_knows_it()
    {
        var sale = Receipt();
        sale.Lines.Add(Line());
        sale.IsReturn.ShouldBeFalse();

        var refund = Receipt();
        refund.Lines.Add(Line(quantity: -2m));
        refund.IsReturn.ShouldBeTrue();
    }

    [Fact]
    public void A_posted_receipt_cannot_be_changed()
    {
        var receipt = Receipt();
        receipt.IsEditable.ShouldBeTrue("it is still parked");

        receipt.Status = PosReceiptStatus.Posted;
        receipt.IsEditable.ShouldBeFalse("the goods have gone and the money is accounted for");
    }
}
