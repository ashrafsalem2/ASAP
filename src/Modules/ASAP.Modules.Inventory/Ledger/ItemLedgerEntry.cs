using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Ledger;

/// <summary>What caused stock to move.</summary>
public enum ItemLedgerEntryType
{
    /// <summary>Goods received from a vendor.</summary>
    Purchase = 0,

    /// <summary>Goods sold to a customer.</summary>
    Sale = 1,

    /// <summary>Stock added by a count or correction.</summary>
    PositiveAdjustment = 2,

    /// <summary>Stock removed by a count, a write-off or breakage.</summary>
    NegativeAdjustment = 3,

    /// <summary>Stock leaving one location for another.</summary>
    TransferOut = 4,

    /// <summary>Stock arriving at a location from another.</summary>
    TransferIn = 5,

    /// <summary>Goods returned by a customer.</summary>
    SalesReturn = 6,

    /// <summary>Goods returned to a vendor.</summary>
    PurchaseReturn = 7,
}

/// <summary>
/// One movement of stock: a quantity, at a location, on a date.
/// </summary>
/// <remarks>
/// <para>
/// Quantity and cost are deliberately kept in separate tables. An item ledger entry says what
/// moved; a <see cref="ValueEntry"/> says what it was worth. They are separate because one
/// movement can be valued more than once: goods sold before they were received are valued at an
/// estimate, and valued again when the receipt finally arrives and the real cost is known.
/// </para>
/// <para>
/// Collapsing the two into one row is the shortcut that makes negative stock corrupt costing. It
/// leaves nowhere to record that a cost was later corrected, so the correction either overwrites
/// history or never happens.
/// </para>
/// </remarks>
public sealed class ItemLedgerEntry : LedgerEntity
{
    /// <summary>The item that moved.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Item number, copied so a report needs no join and history survives renumbering.</summary>
    public required string ItemNo { get; set; }

    /// <summary>What caused the movement.</summary>
    public ItemLedgerEntryType EntryType { get; set; }

    /// <summary>The date the movement is reported in.</summary>
    public DateOnly PostingDate { get; set; }

    /// <summary>The location it moved at.</summary>
    public Guid LocationId { get; set; }

    /// <summary>Location code, copied for the same reason as the item number.</summary>
    public required string LocationCode { get; set; }

    /// <summary>The bin it moved at, where the location tracks them.</summary>
    public Guid? BinId { get; set; }

    /// <summary>
    /// Bin code, copied for the same reason as the location code. Null where the location does
    /// not track bins, which is most of them.
    /// </summary>
    /// <remarks>
    /// The quantity on this entry belongs to the location whatever the bin says. A bin is a
    /// refinement of a location, not a second answer to how much there is, so summing by location
    /// gives the same figure whether bins are used or not.
    /// </remarks>
    public string? BinCode { get; set; }

    /// <summary>
    /// The signed quantity. Positive is stock coming in, negative going out.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// How much of an inbound entry has not yet been consumed by an outbound one.
    /// </summary>
    /// <remarks>
    /// This is what FIFO walks. A receipt of 100 with 40 remaining has had 60 sold against it, and
    /// the next issue takes from the oldest entry with anything left. Zero on an outbound entry
    /// once it has been fully applied to the receipts covering it.
    /// </remarks>
    public decimal RemainingQuantity { get; set; }

    /// <summary>Whether every unit on this entry has been matched to its opposite.</summary>
    public bool IsApplied { get; set; }

    /// <summary>The document that caused it, for example <c>INV-2026-00042</c>.</summary>
    public string? DocumentNo { get; set; }

    /// <summary>The transaction grouping every entry written by one posting.</summary>
    public long TransactionNo { get; set; }

    /// <summary>Serial number, for a serial-tracked item.</summary>
    public string? SerialNo { get; set; }

    /// <summary>Lot or batch number, for a lot-tracked item.</summary>
    public string? LotNo { get; set; }

    /// <summary>When the goods expire, for perishable stock.</summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>Branch the movement happened at.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>Where the movement came from, for example <c>POS</c> or <c>PURCH</c>.</summary>
    public required string SourceCode { get; set; }

    /// <summary>The dimension combination the movement carries.</summary>
    public Guid? DimensionSetId { get; set; }

    /// <summary>
    /// True when this entry took stock that was not there.
    /// </summary>
    /// <remarks>
    /// Flagged at the time rather than inferred later, because by the time the goods arrive the
    /// stock level no longer shows that it was ever negative. This flag is what the cost
    /// adjustment routine looks for.
    /// </remarks>
    public bool WentNegative { get; set; }

    /// <summary>Whether stock came in rather than went out.</summary>
    public bool IsInbound => Quantity > 0;
}
