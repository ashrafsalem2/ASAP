using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Periods;

/// <summary>
/// One financial year, divided into the periods entries are reported in.
/// </summary>
/// <remarks>
/// A year is created before it can be posted into, which is deliberate friction: it forces
/// someone to decide the period structure rather than having ASAP invent one, and it stops a
/// mistyped date in 2036 from silently opening a decade of empty periods.
/// </remarks>
public sealed class FiscalYear : CompanyEntity
{
    /// <summary>How the year is referred to, for example <c>2026</c> or <c>FY2026-27</c>.</summary>
    public required string Code { get; set; }

    /// <summary>First day of the year.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Last day of the year.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// Whether the year has been closed. A closed year accepts nothing further, from anyone: the
    /// statements have been issued and possibly audited, and a late entry would make the filed
    /// figures wrong.
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>When the year was closed, in UTC.</summary>
    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>Who closed it.</summary>
    public Guid? ClosedBy { get; set; }

    /// <summary>
    /// Whether the year-end transfer to retained earnings has run. Distinct from
    /// <see cref="IsClosed"/>: closing stops posting, while the transfer is the entry that moves
    /// the income statement result into equity so the new year opens at zero.
    /// </summary>
    public bool IncomeTransferred { get; set; }

    /// <summary>The periods this year is divided into.</summary>
    public ICollection<FiscalPeriod> Periods { get; set; } = [];

    /// <summary>Whether a date falls inside this year.</summary>
    /// <param name="date">The date to test.</param>
    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;
}

/// <summary>
/// One reporting period within a financial year, usually a calendar month.
/// </summary>
public sealed class FiscalPeriod : CompanyEntity
{
    /// <summary>The year this period belongs to.</summary>
    public Guid FiscalYearId { get; set; }

    /// <summary>Navigation to the year.</summary>
    public FiscalYear? FiscalYear { get; set; }

    /// <summary>Position within the year, starting at 1.</summary>
    public int PeriodNo { get; set; }

    /// <summary>Period name, for example <c>January 2026</c>.</summary>
    public required string Name { get; set; }

    /// <summary>Period name in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>First day of the period.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Last day of the period.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// Whether the period is closed to ordinary posting.
    /// </summary>
    /// <remarks>
    /// Closing a period is routine and reversible by someone with the right permission, unlike
    /// closing a year. It is what stops a clerk keying a January invoice in March after January
    /// has been reported.
    /// </remarks>
    public bool IsClosed { get; set; }

    /// <summary>
    /// Whether this is an adjustment period rather than a trading one.
    /// </summary>
    /// <remarks>
    /// An adjustment period sits at the end of the year with the same dates as the final trading
    /// period, and holds audit adjustments. Keeping them separate is what lets a company report
    /// December as it was and December as adjusted, without one obscuring the other.
    /// </remarks>
    public bool IsAdjustment { get; set; }

    /// <summary>Whether a date falls inside this period.</summary>
    /// <param name="date">The date to test.</param>
    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;
}
