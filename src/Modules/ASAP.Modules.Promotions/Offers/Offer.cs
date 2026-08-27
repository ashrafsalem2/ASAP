using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Promotions.Offers;

/// <summary>What shape an offer takes.</summary>
/// <remarks>
/// Every one of these is a rule for working out a discount from a basket, and they are held as
/// one enum rather than as a class hierarchy because the thing that varies between them is
/// arithmetic, not behaviour. A shop manager setting one up chooses from a list; making that list
/// a set of types would mean a new deployment every time somebody wants a new kind of Tuesday.
/// </remarks>
public enum OfferKind
{
    /// <summary>A percentage off each qualifying line.</summary>
    Percentage = 0,

    /// <summary>A fixed amount off each qualifying unit.</summary>
    AmountPerUnit = 1,

    /// <summary>
    /// Buy a number, get a number free or reduced. Three for two is <c>Buy 3, free 1</c>.
    /// </summary>
    BuyXGetY = 2,

    /// <summary>
    /// Spend past a threshold and the discount applies. The threshold is measured across the
    /// qualifying lines only, not the whole basket, or a bag of crisps would unlock furniture.
    /// </summary>
    Threshold = 3,

    /// <summary>
    /// A fixed price for the qualifying quantity, whatever the lines add up to. Meal deals.
    /// </summary>
    FixedPrice = 4,
}

/// <summary>What an offer applies to.</summary>
public enum OfferScope
{
    /// <summary>Named items.</summary>
    Item = 0,

    /// <summary>Everything in a category.</summary>
    Category = 1,

    /// <summary>Everything, which is what a store-wide sale is.</summary>
    Everything = 2,
}

/// <summary>What happens when more than one offer could apply.</summary>
/// <remarks>
/// The rule people expect is "the customer gets the best one", and it is usually right. It is not
/// always right, which is why this is a choice: a manufacturer funding a promotion may require
/// that theirs applies, and a clearance line may be excluded from everything.
/// </remarks>
public enum StackingRule
{
    /// <summary>May combine with other offers that also allow it.</summary>
    Stacks = 0,

    /// <summary>
    /// Applies alone on a line. Where several exclusive offers compete, the one worth most to the
    /// customer wins, and priority breaks a tie.
    /// </summary>
    Exclusive = 1,

    /// <summary>
    /// Applies alone and stops anything else applying to the basket at all. For an offer whose
    /// funding depends on it being the only one.
    /// </summary>
    Blocking = 2,
}

/// <summary>Where a sale is being made.</summary>
[Flags]
public enum SalesChannel
{
    /// <summary>Nowhere, which no offer should be.</summary>
    None = 0,

    /// <summary>A till in a shop.</summary>
    PointOfSale = 1,

    /// <summary>A sales order taken by a person.</summary>
    Sales = 2,

    /// <summary>Every channel.</summary>
    All = PointOfSale | Sales,
}

/// <summary>
/// A reason to charge less than the price list says.
/// </summary>
/// <remarks>
/// <para>
/// An offer is a rule, not a price. It is evaluated against a basket at the moment of sale, which
/// is the only moment that knows what is in the basket, what it costs today, who is buying, where
/// and when. A promotions system that rewrote price lists instead would be one that could not
/// answer "why was this cheaper" a month later.
/// </para>
/// <para>
/// The discount an offer produces posts to its own account rather than to the ordinary sales
/// discount. Both are money given away; only one of them is a decision somebody made deliberately
/// and should be able to see the total cost of.
/// </para>
/// </remarks>
public sealed class Offer : CompanyEntity
{
    /// <summary>The offer code, for example <c>SUMMER-25</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called on a receipt.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called on a receipt, in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>What shape it takes.</summary>
    public OfferKind Kind { get; set; }

    /// <summary>What it applies to.</summary>
    public OfferScope Scope { get; set; }

    /// <summary>
    /// The number that means something to this kind: the percentage, the amount per unit, the
    /// threshold, or the fixed price.
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>On a buy-X-get-Y, how many must be bought.</summary>
    public decimal BuyQuantity { get; set; }

    /// <summary>On a buy-X-get-Y, how many are then free or reduced.</summary>
    public decimal GetQuantity { get; set; }

    /// <summary>
    /// On a buy-X-get-Y, what percentage off the free ones get. A hundred is free.
    /// </summary>
    public decimal GetDiscountPercent { get; set; } = 100m;

    /// <summary>The first day it runs.</summary>
    public DateOnly StartsOn { get; set; }

    /// <summary>The last day it runs, or null for open-ended.</summary>
    public DateOnly? EndsOn { get; set; }

    /// <summary>The first minute of the day it runs, for a happy hour.</summary>
    public TimeOnly? StartsAt { get; set; }

    /// <summary>The last minute of the day it runs.</summary>
    public TimeOnly? EndsAt { get; set; }

    /// <summary>
    /// Which days it runs, as a bit per day of week, or null for every day.
    /// </summary>
    /// <remarks>
    /// Stored as a mask rather than as rows because it is read on every basket, and a join per
    /// line to answer "is today a Tuesday" is a join nobody should pay for at a till.
    /// </remarks>
    public int? DaysOfWeek { get; set; }

    /// <summary>Where it applies.</summary>
    public SalesChannel Channels { get; set; } = SalesChannel.All;

    /// <summary>The branch it is limited to, or null for every branch.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>The customer group it is limited to, or null for everybody.</summary>
    public string? CustomerGroup { get; set; }

    /// <summary>The coupon that unlocks it, or null when nothing does.</summary>
    public string? CouponCode { get; set; }

    /// <summary>What happens when more than one offer could apply.</summary>
    public StackingRule Stacking { get; set; } = StackingRule.Stacks;

    /// <summary>
    /// Which offer wins a tie. Higher first.
    /// </summary>
    /// <remarks>
    /// Only ever a tiebreak. An offer that won on priority while being worth less to the customer
    /// would be a shop quietly choosing the cheaper discount, which is the sort of thing that ends
    /// up in a newspaper.
    /// </remarks>
    public int Priority { get; set; }

    /// <summary>Whether it may be applied at all.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>How many times it has been applied, for the uptake report.</summary>
    public int TimesApplied { get; set; }

    /// <summary>What it has given away in total.</summary>
    public decimal TotalGivenAway { get; set; }

    /// <summary>What it applies to, when the scope is not everything.</summary>
    public ICollection<OfferTarget> Targets { get; set; } = [];

    /// <summary>Whether it runs on the given day, ignoring everything else.</summary>
    /// <param name="on">The day.</param>
    /// <returns>True when the day is inside the window and on an allowed weekday.</returns>
    public bool RunsOn(DateOnly on)
    {
        if (on < StartsOn || (EndsOn is { } ends && on > ends))
        {
            return false;
        }

        return DaysOfWeek is not { } mask || (mask & (1 << (int)on.DayOfWeek)) != 0;
    }

    /// <summary>Whether it runs at the given time of day.</summary>
    /// <param name="at">The time.</param>
    /// <returns>True when no time window is set, or the time falls inside it.</returns>
    /// <remarks>
    /// A window that ends before it starts crosses midnight, which is how a late-night offer is
    /// written. Treating that as an empty window would silently switch off every such offer.
    /// </remarks>
    public bool RunsAt(TimeOnly at)
    {
        if (StartsAt is not { } from || EndsAt is not { } to)
        {
            return true;
        }

        return from <= to
            ? at >= from && at <= to
            : at >= from || at <= to;
    }
}

/// <summary>One thing an offer applies to.</summary>
public sealed class OfferTarget : CompanyEntity
{
    /// <summary>The offer it belongs to.</summary>
    public Guid OfferId { get; set; }

    /// <summary>The offer, when loaded.</summary>
    public Offer? Offer { get; set; }

    /// <summary>The item, on an item-scoped offer.</summary>
    public string? ItemNo { get; set; }

    /// <summary>The category, on a category-scoped offer.</summary>
    public Guid? CategoryId { get; set; }
}
