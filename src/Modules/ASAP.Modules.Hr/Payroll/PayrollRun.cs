using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Hr.Payroll;

/// <summary>Where a payroll run stands.</summary>
public enum PayrollStatus
{
    /// <summary>Worked out and not yet committed. Nothing has posted and nobody has been paid.</summary>
    Draft = 0,

    /// <summary>Posted to the ledger. What people are owed is now a liability the company carries.</summary>
    Posted = 1,

    /// <summary>Abandoned. Kept rather than deleted, because somebody will ask what happened to it.</summary>
    Cancelled = 2,
}

/// <summary>
/// One period's pay for everybody in it.
/// </summary>
/// <remarks>
/// <para>
/// Calculated first and posted second, always. A payroll that posted as it calculated could not
/// be checked before it committed, and the one document in an ERP that most needs checking before
/// it commits is the one that decides what four hundred people are paid.
/// </para>
/// <para>
/// Posting does not pay anybody. It records what they are owed, which is a liability; the money
/// leaves when the bank transfer is made, and that is a separate document against the same
/// liability. Conflating the two is how a company comes to believe it has paid staff it has not.
/// </para>
/// </remarks>
public sealed class PayrollRun : CompanyEntity
{
    /// <summary>The run number, issued from a number series.</summary>
    public required string No { get; set; }

    /// <summary>The first day the run covers.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>The last day it covers.</summary>
    public DateOnly ToDate { get; set; }

    /// <summary>The date the entries are posted under.</summary>
    public DateOnly PostingDate { get; set; }

    /// <summary>What it is called on a report.</summary>
    public string? Description { get; set; }

    /// <summary>Where it stands.</summary>
    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;

    /// <summary>The transaction the entries posted under.</summary>
    public long? TransactionNo { get; set; }

    /// <summary>When it was posted.</summary>
    public DateTime? PostedAtUtc { get; set; }

    /// <summary>Who posted it.</summary>
    public Guid? PostedBy { get; set; }

    /// <summary>One line per employee.</summary>
    public ICollection<PayrollLine> Lines { get; set; } = [];

    /// <summary>How many days the period covers.</summary>
    public int DaysInPeriod => ToDate.DayNumber - FromDate.DayNumber + 1;

    /// <summary>What everybody earned, before anything is taken off.</summary>
    public decimal GrossPay => Lines.Sum(static l => l.GrossPay);

    /// <summary>What was taken off.</summary>
    public decimal Deductions => Lines.Sum(static l => l.Deductions);

    /// <summary>What people are actually owed.</summary>
    public decimal NetPay => Lines.Sum(static l => l.NetPay);

    /// <summary>What the end-of-service provision moved by over the period.</summary>
    public decimal EndOfServiceCharge => Lines.Sum(static l => l.EndOfServiceCharge);

    /// <summary>Whether it may still be changed.</summary>
    public bool IsEditable => Status is PayrollStatus.Draft;
}

/// <summary>What one person is owed for the period.</summary>
public sealed class PayrollLine : CompanyEntity
{
    /// <summary>The run it belongs to.</summary>
    public Guid PayrollRunId { get; set; }

    /// <summary>The run, when loaded.</summary>
    public PayrollRun? PayrollRun { get; set; }

    /// <summary>Who.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Their number, kept so the line still reads if the employee record moves.</summary>
    public required string EmployeeNo { get; set; }

    /// <summary>Their name at the time.</summary>
    public required string EmployeeName { get; set; }

    /// <summary>Days of the period they actually worked.</summary>
    public int DaysWorked { get; set; }

    /// <summary>The basic wage due for those days.</summary>
    public decimal BasicPay { get; set; }

    /// <summary>Allowances due for those days.</summary>
    public decimal Allowances { get; set; }

    /// <summary>Anything else earned: overtime, a bonus, a settlement.</summary>
    public decimal OtherEarnings { get; set; }

    /// <summary>Anything taken off: an advance, an absence, a fine.</summary>
    public decimal Deductions { get; set; }

    /// <summary>
    /// What the end-of-service provision moved by over the period.
    /// </summary>
    /// <remarks>
    /// A cost of employing somebody this month, not a cost that appears the day they resign. A
    /// company that only recognises it then has overstated its profit every year until it does.
    /// </remarks>
    public decimal EndOfServiceCharge { get; set; }

    /// <summary>Why this line is what it is, for whoever has to explain it.</summary>
    public string? Note { get; set; }

    /// <summary>How the pay divides between the branches they worked at.</summary>
    public ICollection<PayrollBranchShare> BranchShares { get; set; } = [];

    /// <summary>What they earned before anything is taken off.</summary>
    public decimal GrossPay => BasicPay + Allowances + OtherEarnings;

    /// <summary>What they are owed.</summary>
    public decimal NetPay => GrossPay - Deductions;
}

/// <summary>The part of one person's pay that belongs to one branch.</summary>
/// <remarks>
/// Stored rather than recomputed. The branch history can be corrected afterwards — somebody
/// notices a transfer was recorded a week late — and a posted payroll must go on saying what it
/// actually charged where, not what today's history would say it should have.
/// </remarks>
public sealed class PayrollBranchShare : CompanyEntity
{
    /// <summary>The line it belongs to.</summary>
    public Guid PayrollLineId { get; set; }

    /// <summary>The line, when loaded.</summary>
    public PayrollLine? PayrollLine { get; set; }

    /// <summary>The branch that carries it.</summary>
    public Guid BranchId { get; set; }

    /// <summary>Days worked there.</summary>
    public int Days { get; set; }

    /// <summary>The share of the gross pay it carries.</summary>
    public decimal Amount { get; set; }
}
