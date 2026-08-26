using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Periods;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>
/// The reading of the ledger that every financial statement starts from.
/// </summary>
/// <remarks>
/// <para>
/// Balances are summed from the entries rather than read off <see cref="GlAccount.Balance"/>. The
/// running balance is the balance <em>now</em>; a statement asked for as at last March needs the
/// balance as it stood then, and the two are the same number only by coincidence.
/// </para>
/// <para>
/// One aggregate query per range, never one per account. A chart of three hundred accounts would
/// otherwise be hundreds of round trips to draw a single page.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
public sealed class LedgerBalances(AsapDbContext context)
{
    /// <summary>
    /// Sums the signed amount posted to each account within a date range.
    /// </summary>
    /// <param name="from">First day to include, or null for everything up to <paramref name="to"/>.</param>
    /// <param name="to">Last day to include.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The movement per account number. Accounts with nothing posted are absent.</returns>
    public async Task<Dictionary<string, decimal>> MovementAsync(
        DateOnly? from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var entries = context.Set<GlEntry>().AsNoTracking().Where(e => e.PostingDate <= to);

        if (from is { } start)
        {
            entries = entries.Where(e => e.PostingDate >= start);
        }

        return await entries
            .GroupBy(static e => e.AccountNo)
            .Select(static g => new { AccountNo = g.Key, Amount = g.Sum(static e => e.Amount) })
            .ToDictionaryAsync(static x => x.AccountNo, static x => x.Amount, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads the chart of accounts in report order.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Every account, ordered by number.</returns>
    public Task<List<GlAccount>> ChartAsync(CancellationToken cancellationToken = default)
        => context.Set<GlAccount>()
            .AsNoTracking()
            .OrderBy(a => a.No)
            .ToListAsync(cancellationToken);

    /// <summary>The currency the company reports in.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The base currency code.</returns>
    public async Task<string> CurrencyAsync(CancellationToken cancellationToken = default)
        => await context.Companies
               .AsNoTracking()
               .Select(static c => c.BaseCurrencyCode)
               .FirstOrDefaultAsync(cancellationToken)
               .ConfigureAwait(false)
           ?? "SAR";

    /// <summary>
    /// Finds the financial year a date falls in.
    /// </summary>
    /// <param name="date">The date being reported on.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The year, or null when none has been set up covering that date.</returns>
    /// <remarks>
    /// Both statements need it, and for the same reason: the income statement defaults its range
    /// to the year to date, and the balance sheet has to separate this year's result from the
    /// results of years that were never closed.
    /// </remarks>
    public Task<FiscalYear?> YearContainingAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
        => context.Set<FiscalYear>()
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.StartDate <= date && y.EndDate >= date, cancellationToken);

    /// <summary>
    /// Turns a signed ledger balance into the figure a reader expects to see.
    /// </summary>
    /// <param name="account">The account the balance belongs to.</param>
    /// <param name="balance">The signed balance: debits positive, credits negative.</param>
    /// <returns>The balance in the account's natural direction.</returns>
    /// <remarks>
    /// Revenue is stored as a credit, which is a negative number, and printing revenue as negative
    /// on an income statement is the sort of thing that makes a reader distrust every other figure
    /// on the page. Sales of 100 read as 100; a sales return that leaves the account net debit
    /// reads as negative, which is correct and worth seeing.
    /// </remarks>
    public static decimal AsPresented(GlAccount account, decimal balance)
    {
        ArgumentNullException.ThrowIfNull(account);

        return account.IsDebitAccount ? balance : -balance;
    }
}
