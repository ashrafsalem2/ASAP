using ASAP.Modules.Finance.Accounts;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Security;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>One line on the balance sheet.</summary>
/// <param name="AccountNo">The account number, or null on a line ASAP computed.</param>
/// <param name="Name">The line name.</param>
/// <param name="NameArabic">The Arabic name.</param>
/// <param name="Indentation">Indent level, so the statement keeps the chart's shape.</param>
/// <param name="Amount">The figure in the line's natural direction.</param>
/// <param name="IsComputed">
/// Whether the line came from the ledger or was worked out here. The result for the year is
/// computed until the year-end transfer moves it into equity for real.
/// </param>
public sealed record BalanceSheetRow(
    string? AccountNo,
    string Name,
    string? NameArabic,
    int Indentation,
    decimal Amount,
    bool IsComputed = false);

/// <summary>One side or block of the balance sheet.</summary>
/// <param name="Category">Which category the block covers.</param>
/// <param name="Rows">The lines in it.</param>
/// <param name="Total">What the block comes to.</param>
public sealed record BalanceSheetSection(
    string Category,
    IReadOnlyList<BalanceSheetRow> Rows,
    decimal Total);

/// <summary>What the company owned and owed on a given day.</summary>
/// <param name="AsAt">The day reported on.</param>
/// <param name="CurrencyCode">Currency the figures are in.</param>
/// <param name="Sections">Assets, liabilities and equity.</param>
/// <param name="TotalAssets">What the company owns.</param>
/// <param name="TotalLiabilitiesAndEquity">What it owes plus what belongs to its owners.</param>
/// <param name="IsBalanced">Whether the two sides agree.</param>
/// <param name="ResultForTheYear">
/// Profit or loss since the start of the financial year, carried into equity as a computed line
/// because the year-end transfer has not run.
/// </param>
/// <param name="UntransferredPriorResult">
/// Profit or loss left in the income statement accounts from earlier years whose year-end transfer
/// never ran. Normally zero; anything else is worth someone's attention.
/// </param>
public sealed record BalanceSheet(
    DateOnly AsAt,
    string CurrencyCode,
    IReadOnlyList<BalanceSheetSection> Sections,
    decimal TotalAssets,
    decimal TotalLiabilitiesAndEquity,
    bool IsBalanced,
    decimal ResultForTheYear,
    decimal UntransferredPriorResult);

/// <summary>Asks what the company owned and owed on a given day.</summary>
/// <param name="AsAt">The day to report on.</param>
/// <param name="IncludeAccountsWithNoBalance">Whether to list accounts sitting at zero.</param>
[RequiresPermission("Finance", "Report", PermissionAction.Read)]
public sealed record BalanceSheetQuery(
    DateOnly AsAt,
    bool IncludeAccountsWithNoBalance = false) : IQuery<BalanceSheet>;

/// <summary>
/// Builds the balance sheet.
/// </summary>
/// <remarks>
/// <para>
/// The part worth explaining is why equity carries lines that are not accounts.
/// </para>
/// <para>
/// A balance sheet balances because everything the company owns is owed either to outsiders or to
/// its owners, and profit earned belongs to the owners. That profit only reaches an equity account
/// when the year-end transfer runs, and until it does, the income statement accounts are still
/// holding it. Reading only the balance sheet accounts would therefore produce a statement that
/// misses by exactly the profit earned so far -- balanced yesterday, out by a year of trading
/// today, and out by nothing at all in a company that has not traded, which is the worst version
/// because it looks correct in testing.
/// </para>
/// <para>
/// So the result is computed and shown, split in two: this year's, which is normal and expected,
/// and anything left over from earlier years, which means a year-end transfer was never run and
/// somebody should know.
/// </para>
/// </remarks>
/// <param name="balances">Reads the ledger.</param>
public sealed class BalanceSheetQueryHandler(LedgerBalances balances)
    : IRequestHandler<BalanceSheetQuery, BalanceSheet>
{
    private static readonly GlAccountCategory[] Order =
    [
        GlAccountCategory.Assets,
        GlAccountCategory.Liabilities,
        GlAccountCategory.Equity,
    ];

    /// <inheritdoc />
    public async Task<BalanceSheet> HandleAsync(
        BalanceSheetQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var toDate = await balances
            .MovementAsync(from: null, request.AsAt, cancellationToken)
            .ConfigureAwait(false);

        var chart = await balances.ChartAsync(cancellationToken).ConfigureAwait(false);
        var currency = await balances.CurrencyAsync(cancellationToken).ConfigureAwait(false);

        var year = await balances
            .YearContainingAsync(request.AsAt, cancellationToken)
            .ConfigureAwait(false);

        // Everything earned since the year began, and separately anything still sitting in the
        // income statement accounts from before it.
        var thisYear = year is null
            ? null
            : await balances
                .MovementAsync(year.StartDate, request.AsAt, cancellationToken)
                .ConfigureAwait(false);

        var resultForTheYear = ResultOf(chart, thisYear ?? toDate);

        var priorResult = thisYear is null
            ? 0m
            : ResultOf(chart, toDate) - resultForTheYear;

        var sections = new List<BalanceSheetSection>(Order.Length);
        var totals = new Dictionary<GlAccountCategory, decimal>();

        foreach (var category in Order)
        {
            var rows = new List<BalanceSheetRow>();
            var total = 0m;

            foreach (var account in chart.Where(a => a.AccountType is GlAccountType.Posting
                                                     && a.Category == category))
            {
                var amount = LedgerBalances.AsPresented(account, toDate.GetValueOrDefault(account.No));
                total += amount;

                if (amount == 0m && !request.IncludeAccountsWithNoBalance)
                {
                    continue;
                }

                rows.Add(new BalanceSheetRow(
                    account.No,
                    account.Name,
                    account.NameArabic,
                    account.Indentation,
                    amount));
            }

            if (category is GlAccountCategory.Equity)
            {
                AddComputedEquity(rows, resultForTheYear, priorResult);
                total += resultForTheYear + priorResult;
            }

            totals[category] = total;
            sections.Add(new BalanceSheetSection(category.ToString(), rows, total));
        }

        var assets = totals[GlAccountCategory.Assets];
        var liabilitiesAndEquity = totals[GlAccountCategory.Liabilities] + totals[GlAccountCategory.Equity];

        return new BalanceSheet(
            request.AsAt,
            currency,
            sections,
            assets,
            liabilitiesAndEquity,

            // Compared exactly. Every entry reached the ledger through a posting engine that
            // refuses anything unbalanced, so a difference is not rounding -- it means either
            // something wrote the ledger another way, or an account carries a category that
            // contradicts what it is really used for.
            assets == liabilitiesAndEquity,
            resultForTheYear,
            priorResult);
    }

    /// <summary>
    /// Adds the equity lines that are not accounts.
    /// </summary>
    /// <remarks>
    /// Shown even at zero, unlike the account rows. Equity that silently omits the result reads as
    /// though the company earned nothing, and a reader cannot tell the difference between a line
    /// that is absent and a line that is zero.
    /// </remarks>
    private static void AddComputedEquity(
        List<BalanceSheetRow> rows,
        decimal resultForTheYear,
        decimal priorResult)
    {
        if (priorResult != 0m)
        {
            rows.Add(new BalanceSheetRow(
                null,
                "Result of earlier years, not yet transferred",
                "نتيجة سنوات سابقة لم تُرحّل",
                0,
                priorResult,
                IsComputed: true));
        }

        rows.Add(new BalanceSheetRow(
            null,
            "Result for the year",
            "نتيجة السنة",
            0,
            resultForTheYear,
            IsComputed: true));
    }

    /// <summary>
    /// Works out profit or loss from the income statement accounts.
    /// </summary>
    /// <param name="chart">The chart of accounts.</param>
    /// <param name="movement">Signed movement per account.</param>
    /// <returns>Profit as a positive number, a loss as a negative one.</returns>
    private static decimal ResultOf(
        IEnumerable<GlAccount> chart,
        IReadOnlyDictionary<string, decimal> movement)
        => chart
            .Where(static a => a.AccountType is GlAccountType.Posting && !a.IsBalanceSheet)
            .Sum(a => a.Category is GlAccountCategory.Income
                ? LedgerBalances.AsPresented(a, movement.GetValueOrDefault(a.No))
                : -LedgerBalances.AsPresented(a, movement.GetValueOrDefault(a.No)));
}
