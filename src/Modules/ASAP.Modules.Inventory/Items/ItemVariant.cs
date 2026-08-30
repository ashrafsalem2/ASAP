using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Items;

/// <summary>
/// One version of an item that is stocked and counted separately: a colour, a size, a flavour.
/// </summary>
/// <remarks>
/// <para>
/// A variant is not a bin. A bin says where the same goods are standing and never touches a
/// quantity or a cost; a variant is a different physical thing. A blue shirt in medium and a red
/// one in large are not interchangeable, cannot be picked for one another, and may not even have
/// cost the same.
/// </para>
/// <para>
/// Which is the whole difficulty. Stock, cost layers and valuation are all per item and location
/// today, and a variant splits every one of them again. A query that forgets the variant does not
/// fail: it costs a blue shirt against a red receipt, and the only symptom is a margin that is
/// quietly wrong on both.
/// </para>
/// <para>
/// So variants are opt-in per item. An item with <see cref="Item.HasVariants"/> off behaves
/// exactly as it always did, every entry carries no variant, and none of the arithmetic changes.
/// An item with it on refuses a movement that does not say which variant, because guessing is the
/// one thing that must never happen here.
/// </para>
/// </remarks>
public sealed class ItemVariant : CompanyEntity
{
    /// <summary>The item it is a version of.</summary>
    public Guid ItemId { get; set; }

    /// <summary>The item, when loaded.</summary>
    public Item? Item { get; set; }

    /// <summary>Its code, for example <c>BLUE-M</c>. Unique within its item.</summary>
    /// <remarks>
    /// Within the item, not the company: two items both having a <c>RED</c> is ordinary, and
    /// forcing them apart would put the item number in every variant code twice.
    /// </remarks>
    public required string Code { get; set; }

    /// <summary>What this version is called.</summary>
    public required string Description { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? DescriptionArabic { get; set; }

    /// <summary>
    /// Its own barcode.
    /// </summary>
    /// <remarks>
    /// Usually the important one. A shop scans a garment and gets the size on the label, not the
    /// style; an item barcode shared across sizes would make every scan ambiguous at exactly the
    /// moment nobody has time to resolve it.
    /// </remarks>
    public string? Barcode { get; set; }

    /// <summary>Where this version sits in a list, so sizes read in size order.</summary>
    /// <remarks>
    /// A number, because <c>S</c>, <c>M</c>, <c>L</c>, <c>XL</c> do not sort alphabetically into
    /// anything a person recognises and neither do most colours.
    /// </remarks>
    public int SortOrder { get; set; }

    /// <summary>
    /// What one of this variant last actually cost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept per variant because the item's own figure stops meaning anything once variants exist:
    /// it becomes whichever variant was received most recently. Estimating a blue shirt at fifty
    /// because a red one cost fifty is a worse guess than the blue one's own history, and the
    /// estimate is what a sale made before its receipt is valued at.
    /// </para>
    /// <para>
    /// Nought until this variant has been received once, and then the item's figure is used
    /// instead -- a first sale of something never bought has nothing better to go on.
    /// </para>
    /// </remarks>
    public decimal LastDirectCost { get; set; }

    /// <summary>Whether it may still be bought or sold.</summary>
    /// <remarks>
    /// Blocked rather than deleted. Stock already recorded against a variant keeps pointing at it,
    /// and a report covering last season still has to name it.
    /// </remarks>
    public bool IsBlocked { get; set; }
}
