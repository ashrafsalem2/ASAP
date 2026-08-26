using ASAP.Platform.Kernel.Entities;

namespace ASAP.Platform.Core.Numbering;

/// <summary>
/// A named source of document numbers, such as the one that issues general journal numbers or
/// sales invoice numbers.
/// </summary>
/// <remarks>
/// <para>
/// Every document ASAP posts carries a number from a series, so numbering is defined once by an
/// administrator instead of being invented separately by each module.
/// </para>
/// <para>
/// The setting that matters most here is <see cref="AllowGaps"/>. A tax invoice series in Saudi
/// Arabia must be unbroken, so it allocates inside the posting transaction and a rollback hands
/// the number back. An internal series such as a picking list has no such duty and allocates
/// outside the transaction, which is faster under load and simply leaves a gap when something
/// is abandoned. Getting this the wrong way round is expensive in both directions: gaps in a
/// tax series raise questions with the authority, and gapless allocation on a high-volume
/// internal series serialises work that need not be serialised.
/// </para>
/// </remarks>
public sealed class NumberSeries : CompanyEntity
{
    /// <summary>Short stable code, for example <c>SALES-INV</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What the series numbers, shown when an administrator picks one.</summary>
    public required string Description { get; set; }

    /// <summary>Description in Arabic.</summary>
    public string? DescriptionArabic { get; set; }

    /// <summary>
    /// Branch this series belongs to, or null for a company-wide one. Point of sale receipts are
    /// numbered per branch so two shops selling at once cannot collide, and so a receipt number
    /// says on its face where it was issued.
    /// </summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// Whether a gap in the sequence is acceptable.
    /// </summary>
    /// <remarks>
    /// False makes numbering unbroken: the number is allocated inside the posting transaction
    /// under a row lock, so abandoning the post returns it. Required for tax invoices. True
    /// allocates outside the transaction for throughput, and an abandoned document leaves a gap.
    /// </remarks>
    public bool AllowGaps { get; set; } = true;

    /// <summary>
    /// Whether a user may type a number rather than take the next one. Off for anything with a
    /// statutory sequence.
    /// </summary>
    public bool AllowManualEntry { get; set; }

    /// <summary>
    /// Whether numbers must be issued in posting-date order. On, ASAP refuses to post a document
    /// dated earlier than the last one this series numbered, which keeps a printed sequence
    /// consistent with the dates on it.
    /// </summary>
    public bool EnforceDateOrder { get; set; }

    /// <summary>Whether the series may still issue numbers.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The ranges this series issues from, each starting on a date. A new line each January is
    /// how a series restarts its counter every year without losing the history of the last one.
    /// </summary>
    public ICollection<NumberSeriesLine> Lines { get; set; } = [];
}

/// <summary>
/// One range of numbers within a series, in force from a given date.
/// </summary>
/// <remarks>
/// Splitting a series into dated lines is what lets numbering restart each year. The line
/// starting 1 January 2026 issues <c>INV-2026-00001</c> onwards; the 2027 line takes over on its
/// own start date, and the 2026 line keeps its final counter so the history stays readable.
/// </remarks>
public sealed class NumberSeriesLine : CompanyEntity
{
    /// <summary>The series this line belongs to.</summary>
    public Guid NumberSeriesId { get; set; }

    /// <summary>Navigation to the series.</summary>
    public NumberSeries? NumberSeries { get; set; }

    /// <summary>
    /// The date this line starts issuing from. The line used is the latest one whose start date
    /// is on or before the document date.
    /// </summary>
    public DateOnly StartingDate { get; set; }

    /// <summary>
    /// The first number in the range, for example <c>INV-{YYYY}-00001</c>. The width of its
    /// trailing digits fixes the counter width for the whole line.
    /// </summary>
    public required string StartingNumber { get; set; }

    /// <summary>
    /// The last number the line may issue, or null for no ceiling. Used where a range has been
    /// registered with an authority or pre-printed on stationery.
    /// </summary>
    public string? EndingNumber { get; set; }

    /// <summary>
    /// The last number actually issued, or null when the line has issued none yet. This is the
    /// row the allocator locks and advances.
    /// </summary>
    public string? LastNumberUsed { get; set; }

    /// <summary>
    /// The date of the document that took <see cref="LastNumberUsed"/>. Compared against the new
    /// document date when <see cref="NumberSeries.EnforceDateOrder"/> is on.
    /// </summary>
    public DateOnly? LastDateUsed { get; set; }

    /// <summary>How much each issue advances the counter. Normally 1.</summary>
    public int Increment { get; set; } = 1;

    /// <summary>
    /// Warn once the line has this many numbers left, so someone can extend the range before it
    /// runs out mid-trading rather than discovering it at the till.
    /// </summary>
    public int? WarnWhenRemainingBelow { get; set; }

    /// <summary>Whether this line may issue numbers. Closing one retires a range without deleting its history.</summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>
    /// How many numbers the line has left, or null when it has no ceiling.
    /// </summary>
    public long? Remaining()
    {
        if (EndingNumber is null
            || !DocumentNumberFormatter.TryReadCounter(EndingNumber, out var last))
        {
            return null;
        }

        var current = LastNumberUsed is not null
            && DocumentNumberFormatter.TryReadCounter(LastNumberUsed, out var used)
                ? used
                : StartingCounterMinusOne();

        return Math.Max(0, last - current);
    }

    private long StartingCounterMinusOne()
        => DocumentNumberFormatter.TryReadCounter(StartingNumber, out var start) ? start - 1 : 0;
}
