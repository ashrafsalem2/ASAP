using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Hr.People;

/// <summary>Where an employee stands with the company.</summary>
public enum EmploymentStatus
{
    /// <summary>Hired and not yet started.</summary>
    Pending = 0,

    /// <summary>Working.</summary>
    Active = 1,

    /// <summary>Away and expected back: unpaid leave, secondment, suspension.</summary>
    Suspended = 2,

    /// <summary>Gone. Kept rather than deleted, because a leaver's history is still owed to them.</summary>
    Left = 3,
}

/// <summary>Why somebody left, which decides what they are owed.</summary>
/// <remarks>
/// Not a note. Under Saudi labour law an end-of-service award is reduced by tenure when somebody
/// resigns and paid in full when the employer ends the contract, so this field is an input to a
/// calculation rather than a record of one.
/// </remarks>
public enum LeavingReason
{
    /// <summary>Still here.</summary>
    None = 0,

    /// <summary>The employee resigned.</summary>
    Resignation = 1,

    /// <summary>The employer ended the contract.</summary>
    Termination = 2,

    /// <summary>A fixed-term contract ran out.</summary>
    ContractExpiry = 3,

    /// <summary>Retirement, death, or another reason the law treats as full entitlement.</summary>
    Statutory = 4,
}

/// <summary>How somebody is paid.</summary>
public enum PayFrequency
{
    /// <summary>Once a month, which is what nearly everybody here is.</summary>
    Monthly = 0,

    /// <summary>Every two weeks.</summary>
    Fortnightly = 1,

    /// <summary>Weekly.</summary>
    Weekly = 2,
}

/// <summary>
/// Somebody who works for the company.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately separate from a user account. Most employees never sign in — a warehouse hand, a
/// driver — and some users are not employees, such as an auditor or a supplier's engineer.
/// Joining the two would mean either issuing credentials to people who should not have them or
/// leaving people off the payroll because nobody made them an account.
/// </para>
/// <para>
/// Where they work is not a column here. A branch transfer takes effect on a date and payroll
/// has to split a month across two branches on that date, so it is held as a history —
/// see <see cref="BranchAssignment"/>.
/// </para>
/// </remarks>
public sealed class Employee : CompanyEntity
{
    /// <summary>The employee number, issued from a number series.</summary>
    public required string No { get; set; }

    /// <summary>Their name as the company writes it.</summary>
    public required string Name { get; set; }

    /// <summary>Their name in Arabic, which for most staff here is the one that matters.</summary>
    public string? NameArabic { get; set; }

    /// <summary>The national or residence identity number.</summary>
    public string? NationalId { get; set; }

    /// <summary>Nationality, which drives several statutory obligations.</summary>
    public string? Nationality { get; set; }

    /// <summary>When they were born, for age-dependent entitlements.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Where to reach them.</summary>
    public string? Email { get; set; }

    /// <summary>A telephone number.</summary>
    public string? Phone { get; set; }

    /// <summary>The position they hold.</summary>
    public Guid? PositionId { get; set; }

    /// <summary>The position, when loaded.</summary>
    public Position? Position { get; set; }

    /// <summary>Who they report to.</summary>
    public Guid? ManagerId { get; set; }

    /// <summary>The day they started.</summary>
    public DateOnly HiredOn { get; set; }

    /// <summary>The day they left, when they have.</summary>
    public DateOnly? LeftOn { get; set; }

    /// <summary>Why they left, which decides what they are owed.</summary>
    public LeavingReason LeavingReason { get; set; }

    /// <summary>Where they stand.</summary>
    public EmploymentStatus Status { get; set; } = EmploymentStatus.Pending;

    /// <summary>How often they are paid.</summary>
    public PayFrequency PayFrequency { get; set; } = PayFrequency.Monthly;

    /// <summary>
    /// The basic wage for one pay period.
    /// </summary>
    /// <remarks>
    /// Held apart from allowances because the law computes several things from the basic alone
    /// and several from the total, and a single figure could not answer both.
    /// </remarks>
    public decimal BasicWage { get; set; }

    /// <summary>Housing, transport and the rest, for one pay period.</summary>
    public decimal Allowances { get; set; }

    /// <summary>The user account this employee signs in with, when they have one.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Where they have worked and when, in order.</summary>
    public ICollection<BranchAssignment> BranchAssignments { get; set; } = [];

    /// <summary>Basic plus allowances, which is what most entitlements are measured against.</summary>
    public decimal TotalWage => BasicWage + Allowances;

    /// <summary>Whether they are on the payroll at all.</summary>
    public bool IsEmployed => Status is EmploymentStatus.Active or EmploymentStatus.Suspended;

    /// <summary>
    /// How long they have been here, on a given day, in whole days.
    /// </summary>
    /// <remarks>
    /// Counted to the leaving date once they have left, so a calculation run months afterwards
    /// still produces what they were owed rather than what they would have been owed had they
    /// stayed.
    /// </remarks>
    /// <param name="on">The day to measure to.</param>
    /// <returns>Days of service, never negative.</returns>
    public int ServiceDaysOn(DateOnly on)
    {
        var until = LeftOn is { } left && left < on ? left : on;

        return until <= HiredOn ? 0 : until.DayNumber - HiredOn.DayNumber;
    }

    /// <summary>How long they have been here, in years, as a fraction.</summary>
    /// <param name="on">The day to measure to.</param>
    /// <returns>Years of service.</returns>
    public decimal ServiceYearsOn(DateOnly on) => ServiceDaysOn(on) / 365.25m;
}

/// <summary>A job somebody holds.</summary>
/// <remarks>
/// Separate from the employee so that "how many drivers do we have" and "what does a driver cost
/// us" are answerable without reading every contract, and so a reorganisation renames one row
/// rather than four hundred.
/// </remarks>
public sealed class Position : CompanyEntity
{
    /// <summary>The position code, for example <c>CASHIER</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What the job is called.</summary>
    public required string Title { get; set; }

    /// <summary>What the job is called in Arabic.</summary>
    public string? TitleArabic { get; set; }

    /// <summary>The department it sits in.</summary>
    public string? Department { get; set; }

    /// <summary>Whether anybody may still be hired into it.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Where an employee worked, and from when.
/// </summary>
/// <remarks>
/// <para>
/// A history rather than a column on the employee, because a transfer takes effect on a date in
/// the middle of a month and payroll has to split that month between two branches. A single
/// current-branch field would charge the whole month to wherever they happened to be on payday,
/// and the branch they left would look cheaper than it was every time somebody moved.
/// </para>
/// <para>
/// Rows do not overlap and there are no gaps: one assignment ends the day before the next begins.
/// That is checked when an assignment is written, because a gap means a day nobody paid for and
/// an overlap means a day charged twice.
/// </para>
/// </remarks>
public sealed class BranchAssignment : CompanyEntity
{
    /// <summary>The employee.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>The employee, when loaded.</summary>
    public Employee? Employee { get; set; }

    /// <summary>Where they worked.</summary>
    public Guid BranchId { get; set; }

    /// <summary>The first day they worked there.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>The last day, or null while they are still there.</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>Why they moved, for the record.</summary>
    public string? Reason { get; set; }

    /// <summary>Whether this assignment covers the given day.</summary>
    /// <param name="on">The day.</param>
    /// <returns>True when the day falls inside the assignment.</returns>
    public bool Covers(DateOnly on) => on >= FromDate && (ToDate is not { } to || on <= to);
}
