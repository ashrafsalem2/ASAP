using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Sales.Pricing;

/// <summary>
/// A set of agreed prices, and who gets them.
/// </summary>
/// <remarks>
/// <para>
/// Without one, everybody pays what is on the item. That is fine for a shop and useless for anyone
/// selling to trade, where the whole commercial arrangement is that this customer pays less than
/// that one and both pay less than the counter.
/// </para>
/// <para>
/// A list belongs to whoever is assigned it. A customer with none falls back to the item's own
/// price, which is the counter price and the right answer for a walk-in.
/// </para>
/// </remarks>
public sealed class PriceList : CompanyEntity
{
    /// <summary>Its code, for example <c>TRADE</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>The first day it applies, or null for always.</summary>
    public DateOnly? ValidFrom { get; set; }

    /// <summary>The last day it applies, or null for indefinitely.</summary>
    /// <remarks>
    /// A campaign list that expires on its own is the point of this: a price agreed for a quarter
    /// should stop at the end of it without anybody remembering to switch it off, because the one
    /// nobody remembers is the one still being honoured two years later.
    /// </remarks>
    public DateOnly? ValidTo { get; set; }

    /// <summary>Whether it may still be used.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The prices on it.</summary>
    public ICollection<PriceListLine> Lines { get; set; } = [];

    /// <summary>Whether the list is in force on a given day.</summary>
    /// <param name="on">The day.</param>
    /// <returns>True when it applies.</returns>
    public bool AppliesOn(DateOnly on)
        => IsActive
            && (ValidFrom is not { } from || on >= from)
            && (ValidTo is not { } to || on <= to);
}

/// <summary>One agreed price.</summary>
/// <remarks>
/// <para>
/// A line can be as specific as it needs to be: an item, or an item in one colour, or an item in
/// one colour bought a hundred at a time. The most specific line that fits wins, which is what lets
/// a general trade price sit alongside a break for volume without either having to know about the
/// other.
/// </para>
/// <para>
/// Two lines equally specific is not a tie to be broken. It is a contradiction somebody entered by
/// accident, and picking one would make the price depend on which row the database reached first.
/// </para>
/// </remarks>
public sealed class PriceListLine : CompanyEntity
{
    /// <summary>The list it belongs to.</summary>
    public Guid PriceListId { get; set; }

    /// <summary>The list, when loaded.</summary>
    public PriceList? PriceList { get; set; }

    /// <summary>The item this price is for.</summary>
    public required string ItemNo { get; set; }

    /// <summary>One variant, or null for every variant of the item.</summary>
    public string? VariantCode { get; set; }

    /// <summary>One unit of measure, or null for the item's base unit.</summary>
    public string? UnitCode { get; set; }

    /// <summary>
    /// The least somebody has to buy for this price.
    /// </summary>
    /// <remarks>
    /// Nought means any quantity. A hundred means this price applies from a hundred up, and the
    /// line below it still covers smaller orders -- which is how a volume break is expressed
    /// without either line knowing the other exists.
    /// </remarks>
    public decimal MinimumQuantity { get; set; }

    /// <summary>What one costs the customer.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>A discount off that price, held as a percentage so it stays reportable.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>The first day this line applies, or null for whenever the list does.</summary>
    public DateOnly? ValidFrom { get; set; }

    /// <summary>The last day this line applies, or null for whenever the list does.</summary>
    public DateOnly? ValidTo { get; set; }

    /// <summary>Whether this line is in force on a given day.</summary>
    /// <param name="on">The day.</param>
    /// <returns>True when it applies.</returns>
    public bool AppliesOn(DateOnly on)
        => (ValidFrom is not { } from || on >= from)
            && (ValidTo is not { } to || on <= to);

    /// <summary>
    /// How particular this line is, for deciding which of two matching lines wins.
    /// </summary>
    /// <remarks>
    /// A variant is more specific than an item, a unit more specific than none, and a quantity
    /// break more specific than any quantity. Equal scores mean two lines say different things
    /// about the same sale, which is a contradiction rather than a choice.
    /// </remarks>
    public int Specificity
        => (VariantCode is { Length: > 0 } ? 4 : 0)
            + (UnitCode is { Length: > 0 } ? 2 : 0)
            + (MinimumQuantity > 0m ? 1 : 0);
}

/// <summary>Which price list a customer is on.</summary>
/// <remarks>
/// Held here rather than on the customer, because a customer belongs to Finance and what they pay
/// for goods is a sales arrangement. Finance has no business knowing about price lists, and Sales
/// has no business adding columns to the party ledger.
/// </remarks>
public sealed class CustomerPriceList : CompanyEntity
{
    /// <summary>The customer number.</summary>
    public required string CustomerNo { get; set; }

    /// <summary>The price list they are on.</summary>
    public required string PriceListCode { get; set; }
}
