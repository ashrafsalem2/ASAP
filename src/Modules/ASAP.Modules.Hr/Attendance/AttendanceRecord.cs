using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Hr.Attendance;

/// <summary>How a day turned out.</summary>
public enum AttendanceStatus
{
    /// <summary>They came in and the day is accounted for.</summary>
    Present = 0,

    /// <summary>They came in after the grace ran out.</summary>
    Late = 1,

    /// <summary>They did not come in, and nothing explains it.</summary>
    Absent = 2,

    /// <summary>They were on approved leave.</summary>
    OnLeave = 3,

    /// <summary>The shift did not run that day.</summary>
    RestDay = 4,
}

/// <summary>
/// What one person did on one day.
/// </summary>
/// <remarks>
/// <para>
/// One record per person per day. Two would be two accounts of the same day, and every figure
/// derived from them — hours, lateness, overtime — would be the sum of two things that were meant
/// to be one.
/// </para>
/// <para>
/// The derived minutes are stored rather than worked out on demand. They depend on the shift the
/// person was on <em>that day</em>, and a shift changed next year must not restate what somebody
/// was late by last March. The same reason a payroll line stores what it paid.
/// </para>
/// </remarks>
public sealed class AttendanceRecord : CompanyEntity
{
    /// <summary>Whose day it is.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Their number, carried so a report reads without a join.</summary>
    public required string EmployeeNo { get; set; }

    /// <summary>The day.</summary>
    public DateOnly OnDate { get; set; }

    /// <summary>The shift they were on, where they were on one.</summary>
    public string? ShiftCode { get; set; }

    /// <summary>When they clocked in.</summary>
    public TimeOnly? ClockedInAt { get; set; }

    /// <summary>When they clocked out.</summary>
    public TimeOnly? ClockedOutAt { get; set; }

    /// <summary>How the day turned out.</summary>
    public AttendanceStatus Status { get; set; }

    /// <summary>Minutes actually worked, less the shift's break.</summary>
    public int WorkedMinutes { get; set; }

    /// <summary>Minutes late past the grace.</summary>
    public int LateMinutes { get; set; }

    /// <summary>Minutes left before the shift ended.</summary>
    public int EarlyLeaveMinutes { get; set; }

    /// <summary>Minutes worked beyond the shift.</summary>
    public int OvertimeMinutes { get; set; }

    /// <summary>Why it reads as it does, where somebody had to say.</summary>
    public string? Note { get; set; }

    /// <summary>Who recorded it, where it was not a clock.</summary>
    public string? RecordedByUserName { get; set; }
}
