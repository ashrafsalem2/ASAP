namespace ASAP.Modules.Purchasing.Orders;

/// <summary>
/// How much of a return has a debt behind it.
/// </summary>
/// <remarks>
/// <para>
/// Goods can go back to a vendor before their invoice ever turns up -- rejecting a faulty delivery
/// at the door is the ordinary case. So a return covers two different situations at once, and only
/// one of them involves money:
/// </para>
/// <list type="bullet">
/// <item>
/// goods that were <em>invoiced</em> have a debt to reverse, and a credit memo reduces it;
/// </item>
/// <item>
/// goods that were only <em>received</em> have no debt at all, and sending them back simply
/// unwinds the accrual the receipt raised.
/// </item>
/// </list>
/// <para>
/// Held apart from the service because it is the whole judgment of the feature expressed as
/// arithmetic, and arithmetic that decides how much money moves is worth being able to check on
/// its own.
/// </para>
/// </remarks>
public static class PurchaseReturnCrediting
{
    /// <summary>
    /// How much of what is going back now can be credited.
    /// </summary>
    /// <remarks>
    /// Returns come off the invoiced quantity <em>first</em>. Where five arrived, three were
    /// invoiced and two go back, those two are treated as invoiced ones and credited; a later
    /// return of two more credits only the remaining one. The alternative -- crediting the
    /// uninvoiced goods first -- leaves the accrual correct only once everything has gone back,
    /// and wrong at every step in between.
    /// </remarks>
    /// <param name="quantityInvoiced">How much of the line has been invoiced.</param>
    /// <param name="alreadyReturned">How much has already gone back.</param>
    /// <param name="goingBack">How much is going back now.</param>
    /// <returns>The quantity a credit memo should cover.</returns>
    public static decimal CreditableQuantity(
        decimal quantityInvoiced,
        decimal alreadyReturned,
        decimal goingBack)
    {
        var creditedSoFar = Math.Min(alreadyReturned, quantityInvoiced);
        var creditedAfter = Math.Min(alreadyReturned + goingBack, quantityInvoiced);

        return Math.Max(0m, creditedAfter - creditedSoFar);
    }
}
