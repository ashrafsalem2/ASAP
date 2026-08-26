using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Ledger;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>One account on the trial balance.</summary>
/// <param name="AccountNo">The account number.</param>
/// <param name="Name">The account name.</param>
/// <param name="NameArabic">The Arabic name.</param>
/// <param name="AccountType">Whether it takes entries or shapes the report.</param>
/// <param name="Category">Which statement it belongs to.</param>
/// <param name="Indentation">Indent level, so the printed report keeps the chart's shape.</param>
/// <param name="OpeningBalance">Balance carried in on the first day of the range.</param>
/// <param name="PeriodDebit">Debits inside the range.</param>
/// <param name="PeriodCredit">Credits inside the range.</param>
/// <param name="ClosingBalance">Opening plus the movement.</param>
public sealed record TrialBalanceRow(
    string AccountNo,
    string Name,
    string? NameArabic,
    string AccountType,
    string Category,
    int Indentation,
    decimal OpeningBalance,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingBalance);

/// <summary>The trial balance for a date range.</summary>
/// <param name="From">First day covered.</param>
/// <param name="To">Last day covered.</param>
/// <param name="CurrencyCode">Currency the figures are in.</param>
/// <param name="Rows">One row per account.</param>
/// <param name="TotalDebit">Sum of the debit column.</param>
/// <param name="TotalCredit">Sum of the credit column.</param>
/// <param name="IsBalanced">
/// Whether the two totals agree. They always should; a false here means something wrote the
/// ledger without going through the posting engine.
/// </param>
public sealed record TrialBalance(
    DateOnly From,
    DateOnly To,
    string CurrencyCode,
    IReadOnlyList<TrialBalanceRow> Rows,
    decimal TotalDebit,
    decimal TotalCredit,
    bool IsBalanced);

/// <summary>
/// Asks for the trial balance over a date range.
/// </summary>
/// <param name="From">First day to include.</param>
/// <param name="To">Last day to include.</param>
/// <param name="IncludeAccountsWithNoActivity">
/// Whether to list accounts that neither carry a balance nor moved. Off by default: a chart of
/// three hundred accounts of which twenty were used makes a report nobody reads.
/// </param>
[RequiresPermission("Finance", "Report", PermissionAction.Read)]
public sealed record TrialBalanceQuery(
    DateOnly From,
    DateOnly To,
    bool IncludeAccountsWithNoActivity = false) : IQuery<TrialBalance>;

/// <summary>
/// Builds the trial balance.
/// </summary>
/// <remarks>
/// <para>
/// Two aggregate queries, not one query per account. A chart of three hundred accounts would
/// otherwise be six hundred round trips to draw one screen, and the report is the thing an
/// accountant opens most.
/// </para>
/// <para>
/// The opening balance is everything posted before the range, computed from the entries rather
/// than read from the running balance on the account. The running balance is the balance
/// <em>now</em>; a trial balance for last March needs the balance as it stood then.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
public sealed class TrialBalanceQueryHandler(AsapDbContext context)
    : IRequestHandler<TrialBalanceQuery, TrialBalance>
{
    /// <inheritdoc />
    public async Task<TrialBalance> HandleAsync(
        TrialBalanceQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entries = context.Set<GlEntry>().AsNoTracking();

        var opening = await entries
            .Where(e => e.PostingDate < request.From)
            .GroupBy(static e => e.AccountNo)
            .Select(static g => new { AccountNo = g.Key, Amount = g.Sum(static e => e.Amount) })
            .ToDictionaryAsync(static x => x.AccountNo, static x => x.Amount, cancellationToken)
            .ConfigureAwait(false);

        var movement = await entries
            .Where(e => e.PostingDate >= request.From && e.PostingDate <= request.To)
            .GroupBy(static e => e.AccountNo)
            .Select(static g => new
            {
                AccountNo = g.Key,
                Debit = g.Sum(static e => e.DebitAmount),
                Credit = g.Sum(static e => e.CreditAmount),
            })
            .ToDictionaryAsync(static x => x.AccountNo, cancellationToken)
            .ConfigureAwait(false);

        var accounts = await context.Set<GlAccount>()
            .AsNoTracking()
            .OrderBy(a => a.No)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<TrialBalanceRow>(accounts.Count);
        var totalDebit = 0m;
        var totalCredit = 0m;

        foreach (var account in accounts)
        {
            var openingBalance = opening.GetValueOrDefault(account.No);
            movement.TryGetValue(account.No, out var moved);

            var debit = moved?.Debit ?? 0m;
            var credit = moved?.Credit ?? 0m;

            var hasActivity = openingBalance != 0m || debit != 0m || credit != 0m;

            // Headings and totals never carry a balance of their own, but they are kept when the
            // caller asks for the full chart, because without them the report loses its shape and
            // reads as an undifferentiated list of numbers.
            if (!hasActivity && !request.IncludeAccountsWithNoActivity)
            {
                continue;
            }

            rows.Add(new TrialBalanceRow(
                account.No,
                account.Name,
                account.NameArabic,
                account.AccountType.ToString(),
                account.Category.ToString(),
                account.Indentation,
                openingBalance,
                debit,
                credit,
                openingBalance + debit - credit));

            totalDebit += debit;
            totalCredit += credit;
        }

        var currency = await context.Companies
                           .AsNoTracking()
                           .Select(static c => c.BaseCurrencyCode)
                           .FirstOrDefaultAsync(cancellationToken)
                           .ConfigureAwait(false)
                       ?? "SAR";

        return new TrialBalance(
            request.From,
            request.To,
            currency,
            rows,
            totalDebit,
            totalCredit,

            // Compared exactly, not within a tolerance. Every entry reached this table through the
            // posting engine, which refuses anything that does not balance, so a difference here is
            // not a rounding artefact -- it means something wrote the ledger another way.
            totalDebit == totalCredit);
    }
}
