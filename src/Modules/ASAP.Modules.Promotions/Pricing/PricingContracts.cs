using ASAP.Modules.Promotions.Offers;

namespace ASAP.Modules.Promotions.Pricing;

/// <summary>One line of a basket being priced.</summary>
/// <param name="LineNo">Its position, so a discount can be reported against the right row.</param>
/// <param name="ItemNo">What is being sold.</param>
/// <param name="CategoryId">Its category, for category-scoped offers.</param>
/// <param name="Quantity">How many.</param>
/// <param name="UnitPrice">The list price before anything is taken off.</param>
/// <param name="UnitCost">
/// What the goods cost, as the costing engine says today. This is what margin protection is
/// measured against, and it is passed in rather than looked up because the caller has already
/// resolved it and a second lookup could disagree with the first.
/// </param>
/// <param name="ManualDiscountPercent">
/// A discount the person keying it has already applied. Offers are worked out on top of this, not
/// instead of it, because a cashier's discretion and a company's promotion are different
/// decisions and both were made.
/// </param>
public readonly record struct BasketLine(
    int LineNo,
    string ItemNo,
    Guid? CategoryId,
    decimal Quantity,
    decimal UnitPrice,
    decimal UnitCost,
    decimal ManualDiscountPercent = 0m);

/// <summary>Everything the engine needs to know about the sale, besides what is in it.</summary>
/// <param name="On">The day, for the offer window.</param>
/// <param name="At">The time of day, for a happy hour.</param>
/// <param name="Channel">Where the sale is being made.</param>
/// <param name="BranchId">Which branch, for branch-limited offers.</param>
/// <param name="CustomerGroup">The customer's group, for group-limited offers.</param>
/// <param name="CouponCodes">Coupons the customer produced.</param>
public readonly record struct BasketContext(
    DateOnly On,
    TimeOnly At,
    SalesChannel Channel,
    Guid? BranchId = null,
    string? CustomerGroup = null,
    IReadOnlyCollection<string>? CouponCodes = null);

/// <summary>What an offer took off one line.</summary>
/// <param name="LineNo">The line.</param>
/// <param name="OfferCode">Which offer.</param>
/// <param name="OfferName">What it is called, for the receipt.</param>
/// <param name="Amount">
/// How much came off the line in total, always positive. Held as an amount rather than a
/// percentage because a buy-three-get-one-free is not a percentage of anything a customer would
/// recognise, and a receipt that claimed it was would be lying about a real number.
/// </param>
public readonly record struct AppliedDiscount(
    int LineNo,
    string OfferCode,
    string OfferName,
    decimal Amount);

/// <summary>What the engine made of a basket.</summary>
/// <param name="Discounts">What came off, per line and per offer.</param>
/// <param name="TotalDiscount">Everything the offers gave away.</param>
public readonly record struct PricedBasket(
    IReadOnlyList<AppliedDiscount> Discounts,
    decimal TotalDiscount)
{
    /// <summary>What came off one line, across every offer that applied to it.</summary>
    /// <param name="lineNo">The line.</param>
    /// <returns>The total taken off that line.</returns>
    public decimal DiscountOn(int lineNo)
        => Discounts.Where(d => d.LineNo == lineNo).Sum(static d => d.Amount);
}
