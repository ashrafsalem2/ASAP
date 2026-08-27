using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Counting;

/// <summary>Where a count has got to.</summary>
public enum StockCountStatus
{
    /// <summary>Being counted. Lines may still be entered and changed.</summary>
    Open = 0,

    /// <summary>Counted and posted. The differences are now movements.</summary>
    Posted = 1,

    /// <summary>Abandoned. Nothing was posted.</summary>
    Cancelled = 2,
}

/// <summary>
/// A physical count of what is actually on the shelves.
/// </summary>
/// <remarks>
/// <para>
/// The point of a count is not to find out what the system thinks. It is to find out what is
/// there, and then to make the system agree. The difference between the two is the only figure
/// on the sheet that matters, and it is a real loss or a real gain — theft, breakage, a delivery
/// signed for and never put away, a sale keyed twice.
/// </para>
/// <para>
/// A count sheet records what the system said at the moment the sheet was made, so the comparison
/// is against a fixed figure rather than a moving one. Otherwise a sale rung up while somebody is
/// walking the aisles turns into a discrepancy that nobody can explain and everybody stops
/// trusting the count.
/// </para>
/// </remarks>
public sealed class StockCount : CompanyEntity
{
    /// <summary>The count number.</summary>
    public required string No { get; set; }

    /// <summary>The location being counted.</summary>
    public required string LocationCode { get; set; }

    /// <summary>The day the count is reported on.</summary>
    public DateOnly CountDate { get; set; }

    /// <summary>Where it has got to.</summary>
    public StockCountStatus Status { get; set; } = StockCountStatus.Open;

    /// <summary>What the count is for, in the words of whoever started it.</summary>
    public string? Description { get; set; }

    /// <summary>When the sheet was made, which is what the system quantities are as at.</summary>
    public DateTime SheetTakenAtUtc { get; set; }

    /// <summary>Who posted it.</summary>
    public Guid? PostedBy { get; set; }

    /// <summary>When it was posted.</summary>
    public DateTime? PostedAtUtc { get; set; }

    /// <summary>The transaction the adjustments were posted under.</summary>
    public long? TransactionNo { get; set; }

    /// <summary>What is on the sheet.</summary>
    public ICollection<StockCountLine> Lines { get; set; } = [];

    /// <summary>Whether lines may still be entered.</summary>
    public bool IsEditable => this.Status is StockCountStatus.Open;

    /// <summary>Lines somebody has actually counted.</summary>
    public IEnumerable<StockCountLine> Counted => this.Lines.Where(static l => l.CountedQuantity.HasValue);

    /// <summary>Lines nobody has counted yet.</summary>
    public int NotCounted => this.Lines.Count(static l => !l.CountedQuantity.HasValue);

    /// <summary>Lines where what was counted differs from what the system said.</summary>
    public IEnumerable<StockCountLine> Differences => this.Counted.Where(static l => l.Difference != 0m);
}

/// <summary>One item on a count sheet.</summary>
public sealed class StockCountLine : CompanyEntity
{
    /// <summary>The count this belongs to.</summary>
    public Guid StockCountId { get; set; }

    /// <summary>The count.</summary>
    public StockCount? StockCount { get; set; }

    /// <summary>The item.</summary>
    public required string ItemNo { get; set; }

    /// <summary>Its description, copied so the sheet reads without a join.</summary>
    public required string Description { get; set; }

    /// <summary>
    /// What the system said was there when the sheet was made.
    /// </summary>
    /// <remarks>
    /// Frozen deliberately. Comparing against a live figure means a sale rung up while somebody
    /// walks the aisles becomes a discrepancy nobody can explain, and a count nobody trusts is
    /// worse than no count at all.
    /// </remarks>
    public decimal SystemQuantity { get; set; }

    /// <summary>
    /// What was actually found, or null where nobody has looked yet.
    /// </summary>
    /// <remarks>
    /// Null rather than zero, and the difference matters more here than almost anywhere. Zero is
    /// a shelf somebody looked at and found empty; null is a shelf nobody reached. Posting the
    /// first writes stock off, and posting the second would write off everything the counters ran
    /// out of time for.
    /// </remarks>
    public decimal? CountedQuantity { get; set; }

    /// <summary>Why the difference, in the words of whoever counted.</summary>
    public string? Note { get; set; }

    /// <summary>What was found less what the system said. Positive is a gain.</summary>
    public decimal Difference => this.CountedQuantity is { } counted
        ? counted - this.SystemQuantity
        : 0m;
}
