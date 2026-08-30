using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Items;

/// <summary>
/// A unit things are counted, weighed or measured in.
/// </summary>
/// <remarks>
/// A company-wide list — <c>EACH</c>, <c>KG</c>, <c>BOX</c>, <c>CASE</c>, <c>M</c> — and nothing
/// more. What a box of a particular item actually holds is a fact about that item, not about
/// boxes, so it lives on <see cref="ItemUnit"/>. Boxes differ; the word does not.
/// </remarks>
public sealed class UnitOfMeasure : CompanyEntity
{
    /// <summary>Short stable code, for example <c>KG</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>
    /// How many decimal places a quantity in this unit may carry.
    /// </summary>
    /// <remarks>
    /// None for things counted, three for things weighed. It is not decoration: a till that
    /// accepts 2.5 of something sold one at a time has taken an order nobody can pick, and a
    /// scale that reports 1.234 kg against a whole-number unit loses 234 grams every sale.
    /// </remarks>
    public int DecimalPlaces { get; set; }

    /// <summary>Whether it may still be used on a new item.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One unit a particular item may be handled in, and what it comes to in the base unit.
/// </summary>
/// <remarks>
/// <para>
/// The rule the whole design rests on: <b>everything is stored in the base unit</b>. Stock,
/// costing, the ledger, every report — all of it in the unit the item is counted in. A unit is a
/// way of entering and showing a quantity, never a way of storing one.
/// </para>
/// <para>
/// That is not a preference. A stock figure with mixed units in it cannot be added up, and an
/// item ledger holding "3" where three might mean three eaches or three cases is an item ledger
/// that answers nothing. Converting at the edge costs one multiplication; not converting costs
/// the ability to say how much there is.
/// </para>
/// <para>
/// Each unit may carry its own barcode, which is where units and scanning meet: a case of twelve
/// has a different barcode from a single, and scanning it should add twelve rather than one.
/// </para>
/// </remarks>
public sealed class ItemUnit : CompanyEntity
{
    /// <summary>The item this belongs to.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Navigation to the item.</summary>
    public Item? Item { get; set; }

    /// <summary>The unit, matching a <see cref="UnitOfMeasure.Code"/>.</summary>
    public required string UnitCode { get; set; }

    /// <summary>
    /// How many base units one of these is.
    /// </summary>
    /// <remarks>
    /// One for the base unit itself, twelve for a box of twelve. Never nought: a factor of nought
    /// makes every quantity in that unit nought, which is a stock figure that reads as a clean
    /// zero rather than as an error.
    /// </remarks>
    public decimal QuantityPerUnit { get; set; } = 1m;

    /// <summary>The barcode for this unit, when it has one of its own.</summary>
    public string? Barcode { get; set; }

    /// <summary>Whether this unit may still be chosen.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Whether this is the unit everything is stored in.</summary>
    public bool IsBase => QuantityPerUnit == 1m;

    /// <summary>
    /// Turns a quantity in this unit into base units.
    /// </summary>
    /// <param name="quantity">How many of this unit.</param>
    /// <returns>The same amount, counted in the base unit.</returns>
    public decimal ToBase(decimal quantity) => quantity * QuantityPerUnit;

    /// <summary>
    /// Turns a quantity in base units into this one.
    /// </summary>
    /// <param name="baseQuantity">How many base units.</param>
    /// <returns>The same amount, counted in this unit.</returns>
    /// <remarks>
    /// Not rounded here. Seven eaches expressed in boxes of twelve is 0.5833 of a box, and
    /// rounding it at this point would lose stock — the caller decides how to show it, and the
    /// figure it shows is never the figure it stores.
    /// </remarks>
    public decimal FromBase(decimal baseQuantity)
        => QuantityPerUnit == 0m ? 0m : baseQuantity / QuantityPerUnit;
}
