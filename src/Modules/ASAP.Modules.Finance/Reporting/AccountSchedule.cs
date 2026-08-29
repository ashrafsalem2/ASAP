using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>Where a schedule row's figure comes from.</summary>
public enum ScheduleRowKind
{
    /// <summary>The accounts a range names.</summary>
    Accounts = 0,

    /// <summary>Other rows of the same schedule, added and subtracted.</summary>
    Formula = 1,

    /// <summary>Nothing. A heading, or a blank line for the eye.</summary>
    Heading = 2,
}

/// <summary>What period a row's figure covers.</summary>
public enum ScheduleAmountKind
{
    /// <summary>What moved between the two dates. A profit and loss figure.</summary>
    NetChange = 0,

    /// <summary>What the balance stood at on the closing date. A balance sheet figure.</summary>
    BalanceAtDate = 1,
}

/// <summary>
/// A financial statement somebody defined without writing any code.
/// </summary>
/// <remarks>
/// <para>
/// The shipped income statement and balance sheet answer the questions everybody has. What they
/// cannot answer is the question this particular company has — the one where marketing is split
/// out of overheads, or where a covenant is measured on a figure the bank defined. Those are
/// endless and specific, and a system that requires a developer for each one has effectively said
/// no to all of them.
/// </para>
/// <para>
/// So a schedule is data: rows that name account ranges, and rows that add other rows up. It
/// reads like the statement it produces, which is the point — somebody who can write out a
/// profit and loss on paper can build one here.
/// </para>
/// </remarks>
public sealed class AccountSchedule : CompanyEntity
{
    /// <summary>Short stable code, for example <c>CASHFLOW</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>What it is for, shown at the head of the report.</summary>
    public string? Description { get; set; }

    /// <summary>Whether it may still be run.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The rows, in the order they are printed.</summary>
    public ICollection<AccountScheduleLine> Lines { get; set; } = [];
}

/// <summary>
/// One row of a statement.
/// </summary>
/// <remarks>
/// <para>
/// Rows are addressed by <see cref="RowNo"/> rather than by position, and every formula is written
/// in those terms: <c>R100 - R200</c>. Somebody inserting a row between two others does not
/// silently change what every formula below it means, which is exactly what happens when formulas
/// count lines.
/// </para>
/// </remarks>
public sealed class AccountScheduleLine : CompanyEntity
{
    /// <summary>The schedule this belongs to.</summary>
    public Guid AccountScheduleId { get; set; }

    /// <summary>Navigation to the schedule.</summary>
    public AccountSchedule? AccountSchedule { get; set; }

    /// <summary>Where it sits on the page.</summary>
    public int Order { get; set; }

    /// <summary>
    /// What a formula calls this row, for example <c>R100</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not the same as <see cref="Order"/>. Rows are numbered in tens by convention
    /// so there is room to insert, and a row that is moved keeps its name and so keeps every
    /// formula that refers to it correct.
    /// </remarks>
    public required string RowNo { get; set; }

    /// <summary>What the row is called on the page.</summary>
    public required string Description { get; set; }

    /// <summary>What the row is called in Arabic.</summary>
    public string? DescriptionArabic { get; set; }

    /// <summary>Where the figure comes from.</summary>
    public ScheduleRowKind Kind { get; set; } = ScheduleRowKind.Accounts;

    /// <summary>What period it covers.</summary>
    public ScheduleAmountKind AmountKind { get; set; } = ScheduleAmountKind.NetChange;

    /// <summary>
    /// The accounts, or the formula, depending on <see cref="Kind"/>.
    /// </summary>
    /// <remarks>
    /// One field for both because a row is one or the other and never both. Two fields would
    /// allow a row that says one thing in each, and then somebody has to decide which wins.
    /// </remarks>
    public string? Expression { get; set; }

    /// <summary>
    /// Whether to turn the sign before showing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Revenue is a credit, which the ledger holds as a negative number, and printing revenue as
    /// negative on a profit and loss makes a reader distrust every other figure on the page. This
    /// is the switch that turns it, and it is per row because a statement mixes the two — a
    /// balance sheet shows assets as they are stored and liabilities turned.
    /// </para>
    /// <para>
    /// Applied before any formula runs, not on the way to the page. Somebody writing
    /// <c>R10 - R20</c> is reading revenue and cost off the statement in front of them, and a
    /// formula that quietly worked on the ledger's own signs would turn that subtraction into an
    /// addition with nothing visible to explain it.
    /// </para>
    /// </remarks>
    public bool ShowOppositeSign { get; set; }

    /// <summary>How far the description is indented.</summary>
    public int Indent { get; set; }

    /// <summary>Whether the row is printed in bold, as totals usually are.</summary>
    public bool IsBold { get; set; }

    /// <summary>Whether to leave the row out entirely when its figure is nought.</summary>
    public bool HideIfZero { get; set; }
}
