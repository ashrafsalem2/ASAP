using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Hr.People;

/// <summary>What kind of engagement a contract is.</summary>
public enum ContractKind
{
    /// <summary>Open-ended, with no agreed finish.</summary>
    Permanent = 0,

    /// <summary>
    /// Agreed to run to a date.
    /// </summary>
    /// <remarks>
    /// The end date is not optional on one of these. A fixed-term contract with no end is a
    /// permanent one somebody mislabelled, and the difference matters to everything from notice
    /// to end-of-service.
    /// </remarks>
    FixedTerm = 1,

    /// <summary>A trial period, which always ends on a date and is normally followed by another.</summary>
    Probation = 2,
}

/// <summary>
/// What somebody was engaged on, over a stretch of time.
/// </summary>
/// <remarks>
/// <para>
/// The wage used to live on the employee, which meant it had exactly one value: the current one.
/// A raise in April silently rewrote March, so re-running March's payroll paid April's figure and
/// nothing said the number had changed, when, or on whose authority. A contract is a document
/// with dates, and payroll reads the one that was in force.
/// </para>
/// <para>
/// Contracts for one person never overlap. Two covering the same day are two wages for the same
/// day, and payroll would pay whichever row it read first — a difference nobody finds until
/// somebody queries a payslip, by which time the same wrong figure has gone out several more
/// times. A new contract closes the one before it the day before it starts, so there is no gap
/// either.
/// </para>
/// </remarks>
public sealed class EmploymentContract : CompanyEntity
{
    /// <summary>Whose contract it is.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Their number, carried so a payslip reads without a join.</summary>
    public required string EmployeeNo { get; set; }

    /// <summary>The first day it covers.</summary>
    public DateOnly StartsOn { get; set; }

    /// <summary>The last day it covers, or null where it is open-ended.</summary>
    public DateOnly? EndsOn { get; set; }

    /// <summary>What kind of engagement it is.</summary>
    public ContractKind Kind { get; set; } = ContractKind.Permanent;

    /// <summary>The basic wage for one pay period under this contract.</summary>
    public decimal BasicWage { get; set; }

    /// <summary>Housing, transport and the rest, for one pay period under this contract.</summary>
    public decimal Allowances { get; set; }

    /// <summary>How often it pays.</summary>
    public PayFrequency PayFrequency { get; set; } = PayFrequency.Monthly;

    /// <summary>The position held under it, where it differs from the employee's current one.</summary>
    public Guid? PositionId { get; set; }

    /// <summary>The paper contract's own reference, for anybody holding the file.</summary>
    public string? Reference { get; set; }

    /// <summary>When it was signed.</summary>
    public DateOnly? SignedOn { get; set; }

    /// <summary>Who recorded it, which is not always who signed it.</summary>
    public string? RecordedByUserName { get; set; }

    /// <summary>Why it was raised: a raise, a promotion, a renewal.</summary>
    public string? Reason { get; set; }

    /// <summary>Basic plus allowances, which is what most entitlements are measured against.</summary>
    public decimal TotalWage => BasicWage + Allowances;

    /// <summary>Whether it covers a given day.</summary>
    /// <param name="on">The day.</param>
    /// <returns>Whether the day falls inside it.</returns>
    public bool Covers(DateOnly on) => on >= StartsOn && (EndsOn is null || on <= EndsOn);
}
