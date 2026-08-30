namespace ASAP.Platform.Kernel.Accounting;

/// <summary>Whether an account will actually take an entry.</summary>
public enum AccountPostability
{
    /// <summary>Nothing in the chart carries that number.</summary>
    NotFound = 0,

    /// <summary>It is there, but withdrawn from use.</summary>
    Blocked = 1,

    /// <summary>It is a caption or a total, not somewhere entries land.</summary>
    NotAPostingAccount = 2,

    /// <summary>It will take an entry.</summary>
    Postable = 3,
}

/// <summary>What one account in the chart is.</summary>
/// <param name="AccountNo">Its number.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Category">
/// Where it sits in the statements, as the owning module names it -- <c>Assets</c>,
/// <c>Income</c>, <c>Expense</c> and so on.
/// </param>
/// <param name="Postability">Whether an entry aimed at it would land.</param>
public readonly record struct AccountDescription(
    string AccountNo,
    string Name,
    string? NameArabic,
    string Category,
    AccountPostability Postability);

/// <summary>
/// Reads the chart of accounts without depending on whichever module owns it.
/// </summary>
/// <remarks>
/// <para>
/// Inventory has to be able to say "that account number is a heading, nothing will ever post to
/// it" while a setup screen is still open, and it holds no reference to the Finance assembly --
/// a module that referenced another could not be sold without it, which is the point of the
/// architecture. So the question is asked through the kernel and whichever module owns the ledger
/// answers it.
/// </para>
/// <para>
/// Nothing may assume an implementation exists. On an installation without a general ledger there
/// is none, and the right behaviour is to stop checking rather than to refuse everything: a
/// company running stock without accounts is a supported way to run, and turning an unanswerable
/// question into a refusal would make it not one.
/// </para>
/// </remarks>
public interface IChartOfAccounts
{
    /// <summary>
    /// Says what an account is, or nothing when the chart has never heard of it.
    /// </summary>
    /// <param name="accountNo">The account number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What it is, or null when there is no such account.</returns>
    Task<AccountDescription?> DescribeAsync(string accountNo, CancellationToken cancellationToken = default);
}
