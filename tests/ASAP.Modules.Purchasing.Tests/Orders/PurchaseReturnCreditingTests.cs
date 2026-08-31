using ASAP.Modules.Purchasing.Orders;
using Shouldly;

namespace ASAP.Modules.Purchasing.Tests.Orders;

/// <summary>
/// How much of a return has a debt behind it.
/// </summary>
/// <remarks>
/// The whole judgment of a purchase return, expressed as arithmetic. Goods can go back before
/// their invoice ever turns up, so a return covers two situations at once and only one of them
/// involves money -- getting the split wrong leaves the goods-received-not-invoiced accrual
/// carrying a balance for goods that are no longer in the building, which is exactly the kind of
/// thing nobody notices until a year end.
/// </remarks>
public sealed class PurchaseReturnCreditingTests
{
    /// <summary>Nothing invoiced means nothing to credit — only the accrual unwinds.</summary>
    [Fact]
    public void Goods_never_invoiced_are_credited_for_nothing()
        => PurchaseReturnCrediting.CreditableQuantity(
            quantityInvoiced: 0m,
            alreadyReturned: 0m,
            goingBack: 5m)
            .ShouldBe(0m);

    /// <summary>Everything invoiced means everything going back is credited.</summary>
    [Fact]
    public void Goods_fully_invoiced_are_credited_in_full()
        => PurchaseReturnCrediting.CreditableQuantity(
            quantityInvoiced: 10m,
            alreadyReturned: 0m,
            goingBack: 4m)
            .ShouldBe(4m);

    /// <summary>
    /// Where part of a delivery was invoiced, returns come off the invoiced part first.
    /// </summary>
    /// <remarks>
    /// Five arrived, three were invoiced, two go back: those two are treated as invoiced ones. The
    /// alternative leaves the accrual correct only once everything has gone back, and wrong at
    /// every step in between.
    /// </remarks>
    [Fact]
    public void A_partly_invoiced_delivery_credits_the_invoiced_part_first()
        => PurchaseReturnCrediting.CreditableQuantity(
            quantityInvoiced: 3m,
            alreadyReturned: 0m,
            goingBack: 2m)
            .ShouldBe(2m);

    /// <summary>A second return credits only what is left of the invoiced quantity.</summary>
    [Fact]
    public void A_second_return_credits_only_what_is_left_of_the_invoice()
    {
        // Three invoiced. Two already went back and were credited.
        PurchaseReturnCrediting.CreditableQuantity(
            quantityInvoiced: 3m,
            alreadyReturned: 2m,
            goingBack: 2m)
            .ShouldBe(1m, "only one invoiced unit remains to credit");
    }

    /// <summary>Once the invoiced quantity is used up, nothing further is credited.</summary>
    [Fact]
    public void Beyond_the_invoiced_quantity_nothing_more_is_credited()
        => PurchaseReturnCrediting.CreditableQuantity(
            quantityInvoiced: 3m,
            alreadyReturned: 3m,
            goingBack: 2m)
            .ShouldBe(0m);

    /// <summary>
    /// Returning the whole delivery credits exactly what was invoiced, however it is split up.
    /// </summary>
    /// <remarks>
    /// The property that matters: the pieces add up to the same total whatever order they come
    /// back in, so the accrual nets to nothing once everything has gone.
    /// </remarks>
    [Fact]
    public void The_pieces_add_up_to_what_was_invoiced()
    {
        const decimal received = 10m;
        const decimal invoiced = 6m;

        var returnedSoFar = 0m;
        var credited = 0m;

        foreach (var chunk in new[] { 3m, 1m, 4m, 2m })
        {
            credited += PurchaseReturnCrediting.CreditableQuantity(invoiced, returnedSoFar, chunk);
            returnedSoFar += chunk;
        }

        returnedSoFar.ShouldBe(received);
        credited.ShouldBe(invoiced, "the whole invoice was credited and no more");
    }
}
