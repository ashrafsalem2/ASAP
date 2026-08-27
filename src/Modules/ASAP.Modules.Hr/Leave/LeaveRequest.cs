using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Hr.Leave;

/// <summary>
/// Somebody asking to be away, and what was decided.
/// </summary>
/// <remarks>
/// <para>
/// The record of leave taken, which is the half the entitlement calculation was missing. Without
/// it the leave figure on a liability report is everything anybody has ever earned, which is an
/// upper bound presented as a total — the sort of number that is wrong in the company's favour
/// every year until somebody leaves and asks for what they are actually owed.
/// </para>
/// <para>
/// A request is kept whatever is decided about it. A rejection counts against nothing but is
/// still an answer somebody was given, and a cancelled request is why a shop was short-staffed
/// that week according to the rota and not according to the payroll.
/// </para>
/// </remarks>
public sealed class LeaveRequest : CompanyEntity
{
    /// <summary>The request number.</summary>
    public required string No { get; set; }

    /// <summary>Who is asking.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Their number, copied so a report needs no join.</summary>
    public required string EmployeeNo { get; set; }

    /// <summary>Their name, copied for the same reason.</summary>
    public required string EmployeeName { get; set; }

    /// <summary>What kind of leave.</summary>
    public LeaveKind Kind { get; set; } = LeaveKind.Annual;

    /// <summary>First day away.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Last day away.</summary>
    public DateOnly ToDate { get; set; }

    /// <summary>Where it has got to.</summary>
    public LeaveStatus Status { get; set; } = LeaveStatus.Draft;

    /// <summary>Why, in the words of whoever asked.</summary>
    public string? Reason { get; set; }

    /// <summary>What whoever decided said about it.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>Who decided.</summary>
    public Guid? DecidedBy { get; set; }

    /// <summary>When they decided.</summary>
    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>
    /// How many days away.
    /// </summary>
    /// <remarks>
    /// Calendar days, inclusive of both ends. That is how the Labour Law counts leave and it is
    /// also the only count somebody can check against a calendar without knowing the rota — a
    /// working-day count would need the shift pattern of the branch they were at on each of those
    /// days, and would silently change if that pattern were edited afterwards.
    /// </remarks>
    public int Days => this.ToDate < this.FromDate ? 0 : this.ToDate.DayNumber - this.FromDate.DayNumber + 1;

    /// <summary>Whether the request still counts towards a balance or a wage.</summary>
    public bool Counts => this.Status is LeaveStatus.Approved;

    /// <summary>Whether it can still be changed.</summary>
    public bool IsEditable => this.Status is LeaveStatus.Draft or LeaveStatus.Submitted;

    /// <summary>How many of its days fall inside a period.</summary>
    /// <param name="from">First day of the period.</param>
    /// <param name="to">Last day of the period.</param>
    /// <returns>The overlap in days, or zero where there is none.</returns>
    public int DaysWithin(DateOnly from, DateOnly to)
    {
        var start = this.FromDate < from ? from : this.FromDate;
        var end = this.ToDate > to ? to : this.ToDate;

        return end < start ? 0 : end.DayNumber - start.DayNumber + 1;
    }

    /// <summary>Whether this request covers any of the same days as another span.</summary>
    /// <param name="from">First day of the other span.</param>
    /// <param name="to">Last day of the other span.</param>
    /// <returns>True where the two overlap by at least a day.</returns>
    public bool Overlaps(DateOnly from, DateOnly to)
        => this.FromDate <= to && this.ToDate >= from;
}
