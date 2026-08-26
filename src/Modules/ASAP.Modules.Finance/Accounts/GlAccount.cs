using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Accounts;

/// <summary>
/// What an account is for structurally: something entries land on, or something that only exists
/// to shape the report.
/// </summary>
public enum GlAccountType
{
    /// <summary>Entries post here. The only type that ever carries a balance of its own.</summary>
    Posting = 0,

    /// <summary>A caption on the chart. Nothing posts to it and it totals nothing.</summary>
    Heading = 1,

    /// <summary>Sums a range of accounts named in <see cref="GlAccount.Totaling"/>.</summary>
    Total = 2,

    /// <summary>Opens a range that a matching <see cref="EndTotal"/> closes.</summary>
    BeginTotal = 3,

    /// <summary>Closes the range opened by a <see cref="BeginTotal"/> and sums it.</summary>
    EndTotal = 4,
}

/// <summary>
/// Which statement an account belongs to, and where on it.
/// </summary>
/// <remarks>
/// Chosen over a free-text classification because the year-end routine has to know, without
/// guessing, which balances carry forward and which close to retained earnings. Getting that
/// wrong does not produce an error; it produces a balance sheet that is quietly wrong by a year
/// of trading.
/// </remarks>
public enum GlAccountCategory
{
    /// <summary>Balance sheet: what the company owns.</summary>
    Assets = 0,

    /// <summary>Balance sheet: what the company owes.</summary>
    Liabilities = 1,

    /// <summary>Balance sheet: capital and retained earnings.</summary>
    Equity = 2,

    /// <summary>Income statement: revenue.</summary>
    Income = 3,

    /// <summary>Income statement: the direct cost of what was sold.</summary>
    CostOfGoodsSold = 4,

    /// <summary>Income statement: everything else the business spends.</summary>
    Expense = 5,
}

/// <summary>
/// One line of the chart of accounts.
/// </summary>
/// <remarks>
/// <para>
/// The chart is a flat list of numbered accounts with indentation and totalling accounts giving
/// it structure, rather than a parent-child tree. That is how every accountant already reads a
/// chart, it makes a range like <c>1000..1999</c> mean exactly what it says, and it survives an
/// account being renumbered far better than a tree of foreign keys.
/// </para>
/// <para>
/// An account is never deleted once anything has posted to it. Withdrawing one from use is what
/// <see cref="IsBlocked"/> is for: last year's entries must still resolve to a name.
/// </para>
/// </remarks>
public sealed class GlAccount : CompanyEntity
{
    /// <summary>
    /// The account number, for example <c>1100</c>. Sorted as text, so numbers are written to a
    /// consistent width and a chart reads in the order it was designed in.
    /// </summary>
    public required string No { get; set; }

    /// <summary>Account name.</summary>
    public required string Name { get; set; }

    /// <summary>Account name in Arabic, as it appears on an Arabic statement.</summary>
    public string? NameArabic { get; set; }

    /// <summary>What the account is for structurally.</summary>
    public GlAccountType AccountType { get; set; } = GlAccountType.Posting;

    /// <summary>
    /// Which statement it belongs to. Only meaningful for a
    /// <see cref="GlAccountType.Posting"/> account.
    /// </summary>
    public GlAccountCategory Category { get; set; } = GlAccountCategory.Assets;

    /// <summary>
    /// The range a totalling account sums, for example <c>1000..1999</c> or <c>1100|1200</c>.
    /// Null on a posting account.
    /// </summary>
    public string? Totaling { get; set; }

    /// <summary>Indent level on the printed chart, so the structure reads at a glance.</summary>
    public int Indentation { get; set; }

    /// <summary>
    /// Whether a person may post to this account by hand.
    /// </summary>
    /// <remarks>
    /// Off for accounts that only a module should touch: the inventory account, the VAT accounts,
    /// the receivables and payables control accounts. A hand-written entry to receivables makes
    /// the control account disagree with the customer ledger behind it, and finding that
    /// afterwards costs an afternoon.
    /// </remarks>
    public bool AllowsDirectPosting { get; set; } = true;

    /// <summary>
    /// Whether the account is withdrawn from use. Blocking retires an account without disturbing
    /// what was posted to it while it was live.
    /// </summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Whether every entry on this account must carry a dimension value for each mandatory
    /// dimension. Enforced at posting, naming the dimension.
    /// </summary>
    public bool RequiresDimensions { get; set; }

    /// <summary>
    /// Currency this account is restricted to, or null for the company base currency. Used for
    /// a bank account held in a foreign currency.
    /// </summary>
    public string? CurrencyCode { get; set; }

    /// <summary>
    /// Running balance in the company base currency, maintained as entries post.
    /// </summary>
    /// <remarks>
    /// Denormalised deliberately. Summing a million ledger rows to show a chart of accounts is
    /// the difference between a screen that opens and one that times out. It is written only by
    /// the posting engine, inside the posting transaction, so it cannot drift from the entries.
    /// </remarks>
    public decimal Balance { get; set; }

    /// <summary>True when the account is on the balance sheet rather than the income statement.</summary>
    public bool IsBalanceSheet =>
        Category is GlAccountCategory.Assets or GlAccountCategory.Liabilities or GlAccountCategory.Equity;

    /// <summary>
    /// True when the account increases on the debit side. Assets and costs do; income, liabilities
    /// and equity do not. Drives how a balance is presented rather than how it is stored.
    /// </summary>
    public bool IsDebitAccount =>
        Category is GlAccountCategory.Assets
                 or GlAccountCategory.Expense
                 or GlAccountCategory.CostOfGoodsSold;

    /// <summary>
    /// Whether an entry may land on this account at all.
    /// </summary>
    /// <remarks>
    /// A heading or a totalling account is part of the report, not a place value can rest.
    /// Posting to one would make the chart total more than the sum of its parts.
    /// </remarks>
    public bool IsPostable => AccountType is GlAccountType.Posting && !IsBlocked && !IsDeleted;
}
