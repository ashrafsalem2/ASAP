using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Hr.Attendance;

/// <summary>
/// A working pattern: when it starts, when it ends, and which days it runs.
/// </summary>
/// <remarks>
/// <para>
/// A pattern rather than a rota. It says what a normal day looks like for whoever is on it, which
/// is what lateness and overtime are measured against. Who works which particular day is the
/// assignment, and what actually happened is the attendance record.
/// </para>
/// <para>
/// The grace is here rather than in a company setting because it genuinely differs by shift. A
/// night shift where the handover is at the gate has no grace at all; an office shift where
/// somebody is at their desk by five past has ten minutes and nobody minds.
/// </para>
/// </remarks>
public sealed class Shift : CompanyEntity
{
    /// <summary>Its code, for example <c>DAY</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>When it starts.</summary>
    public TimeOnly StartsAt { get; set; }

    /// <summary>
    /// When it ends.
    /// </summary>
    /// <remarks>
    /// Earlier than the start means it crosses midnight, which is what a night shift is. Nothing
    /// else distinguishes the two, and a separate flag would be a second thing to keep in step
    /// with the times.
    /// </remarks>
    public TimeOnly EndsAt { get; set; }

    /// <summary>Unpaid break in the middle of it, in minutes.</summary>
    public int BreakMinutes { get; set; }

    /// <summary>
    /// Which days of the week it runs, as a bit per day with Sunday as 1.
    /// </summary>
    /// <remarks>
    /// The same encoding the promotions engine uses for a day-limited offer. One number rather
    /// than seven columns, because every question anybody asks of it is "does it run today".
    /// </remarks>
    public int DaysOfWeek { get; set; } = 0b0111_1111;

    /// <summary>Minutes after the start that are not counted as late.</summary>
    public int GraceMinutes { get; set; }

    /// <summary>Whether anybody may still be put on it.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Whether it runs on a given day.</summary>
    /// <param name="on">The day.</param>
    /// <returns>Whether the shift runs that day of the week.</returns>
    public bool RunsOn(DateOnly on) => (DaysOfWeek & (1 << (int)on.DayOfWeek)) != 0;

    /// <summary>Whether it finishes on the day after it starts.</summary>
    public bool CrossesMidnight => EndsAt <= StartsAt;

    /// <summary>How long it is, less the break, in minutes.</summary>
    public int PaidMinutes
    {
        get
        {
            var span = CrossesMidnight
                ? (1440 - (int)(StartsAt - TimeOnly.MinValue).TotalMinutes)
                  + (int)(EndsAt - TimeOnly.MinValue).TotalMinutes
                : (int)(EndsAt - StartsAt).TotalMinutes;

            return Math.Max(0, span - BreakMinutes);
        }
    }
}

/// <summary>
/// Which shift somebody is on, from a date.
/// </summary>
/// <remarks>
/// Effective-dated for the same reason a contract is: somebody moved from days to nights in March
/// was late by the night shift's clock in April and by the day shift's clock in February, and one
/// current value could only ever be right about one of those.
/// </remarks>
public sealed class ShiftAssignment : CompanyEntity
{
    /// <summary>Whose it is.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Their number, carried so a report reads without a join.</summary>
    public required string EmployeeNo { get; set; }

    /// <summary>The shift.</summary>
    public required string ShiftCode { get; set; }

    /// <summary>The first day it applies.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>The last day it applies, or null where it still stands.</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>Whether it covers a given day.</summary>
    /// <param name="on">The day.</param>
    /// <returns>Whether the day falls inside it.</returns>
    public bool Covers(DateOnly on) => on >= FromDate && (ToDate is null || on <= ToDate);
}
