using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Locations;

/// <summary>Where a bin movement stands.</summary>
public enum BinMovementStatus
{
    /// <summary>Being prepared. Nothing has moved.</summary>
    Draft = 0,

    /// <summary>The goods have been moved and the bins say so.</summary>
    Posted = 1,

    /// <summary>Abandoned before anything moved.</summary>
    Cancelled = 2,
}

/// <summary>
/// Goods moved from one shelf to another inside one place.
/// </summary>
/// <remarks>
/// <para>
/// A document of its own rather than an adjustment pair, because it is not an adjustment. Nothing
/// is gained and nothing is lost: the quantity at the location is the same before and after, and
/// so is what the stock is worth. Recording it as a negative adjustment and a positive one would
/// put two entries through costing, fragmenting the cost layers and moving the valuation for a
/// change that moved nothing but a box.
/// </para>
/// <para>
/// It is a document rather than a bare action because somebody restocking a shelf moves eleven
/// things at once, and eleven separate acts is eleven chances to record ten. The whole sheet
/// posts or none of it does.
/// </para>
/// </remarks>
public sealed class BinMovement : CompanyEntity
{
    /// <summary>The movement number.</summary>
    public required string No { get; set; }

    /// <summary>Where it happened. Both bins are in this location, always.</summary>
    public required string LocationCode { get; set; }

    /// <summary>The location's key.</summary>
    public Guid LocationId { get; set; }

    /// <summary>When it happened.</summary>
    public DateOnly MovementDate { get; set; }

    /// <summary>Where it stands.</summary>
    public BinMovementStatus Status { get; set; } = BinMovementStatus.Draft;

    /// <summary>Why the goods were moved, where anybody said.</summary>
    public string? Note { get; set; }

    /// <summary>Who recorded it.</summary>
    public string? RecordedByUserName { get; set; }

    /// <summary>The transaction the entries posted under.</summary>
    public long? TransactionNo { get; set; }

    /// <summary>What was moved.</summary>
    public ICollection<BinMovementLine> Lines { get; set; } = [];

    /// <summary>Whether it may still be changed.</summary>
    public bool IsEditable => Status is BinMovementStatus.Draft;
}

/// <summary>One thing moved from one shelf to another.</summary>
public sealed class BinMovementLine : CompanyEntity
{
    /// <summary>The movement this belongs to.</summary>
    public Guid BinMovementId { get; set; }

    /// <summary>The movement, for loading.</summary>
    public BinMovement? BinMovement { get; set; }

    /// <summary>Position on the sheet.</summary>
    public int LineNo { get; set; }

    /// <summary>What was moved.</summary>
    public required string ItemNo { get; set; }

    /// <summary>Which variant, on an item that has them.</summary>
    public string? VariantCode { get; set; }

    /// <summary>The shelf it came off.</summary>
    public required string FromBinCode { get; set; }

    /// <summary>The shelf it went onto.</summary>
    public required string ToBinCode { get; set; }

    /// <summary>How much moved.</summary>
    public decimal Quantity { get; set; }
}
