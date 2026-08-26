using ASAP.Modules.Finance.Accounts;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Security;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>One account on the income statement.</summary>
/// <param name="AccountNo">The account number.</param>
/// <param name="Name">The account name.</param>
/// <param name="NameArabic">The Arabic name.</param>
/// <param name="Indentation">Indent level, so the statement keeps the chart's shape.</param>
/// <param name="Amount">The figure in the account's natural direction.</param>
/// <param name="Comparative">The same figure for the comparison range, when one was asked for.</param>
public sealed record IncomeStatementRow(
    string AccountNo,
    string Name,
    string? NameArabic,
    int Indentation,
    decimal Amount,
    decimal? Comparative);

/// <summary>One block of the income statement, with its own subtotal.</summary>
/// <param name="Category">Which category the block covers.</param>
/// <param name="Rows">The accounts in it.</param>
/// <param name="Total">What the block comes to.</param>
/// <param name="ComparativeTotal">What it came to in the comparison range.</param>
public sealed record IncomeStatementSection(
    string Category,
    IReadOnlyList<IncomeStatementRow> Rows,
    decimal Total,
    decimal? ComparativeTotal);

/// <summary>What the company earned over a range.</summary>
/// <param name="From">First day covered.</param>
/// <param name="To">Last day covered.</param>
/// <param name="ComparativeFrom">First day of the comparison range, when there is one.</param>
/// <param name="ComparativeTo">Last day of the comparison range.</param>
/// <param name="CurrencyCode">Currency the figures are in.</param>
/// <param name="Sections">Revenue, cost of sales and expenses, in reading order.</param>
/// <param name="GrossProfit">Revenue less cost of sales.</param>
/// <param name="ComparativeGrossProfit">The same for the comparison range.</param>
/// <param name="NetProfit">Gross profit less expenses.</param>
/// <param name="ComparativeNetProfit">The same for the comparison range.</param>
public sealed record IncomeStatement(
    DateOnly From,
    DateOnly To,
    DateOnly? ComparativeFrom,
    DateOnly? ComparativeTo,
    string CurrencyCode,
    IReadOnlyList<IncomeStatementSection> Sections,
    decimal GrossProfit,
    decimal? ComparativeGrossProfit,
    decimal NetProfit,
    decimal? ComparativeNetProfit);

/// <summary>
/// Asks what the company earned between two dates.
/// </summary>
/// <param name="From">First day to include.</param>
/// <param name="To">Last day to include.</param>
/// <param name="ComparativeFrom">First day of a range to show alongside, when wanted.</param>
/// <param name="ComparativeTo">Last day of that range.</param>
/// <param name="IncludeAccountsWithNoActivity">
/// Whether to list accounts that did not move. Off by default: a statement is read for what
/// happened, and rows of zeroes bury it.
/// </param>
[RequiresPermission("Finance", "Report", PermissionAction.Read)]
public sealed record IncomeStatementQuery(
    DateOnly From,
    DateOnly To,
    DateOnly? ComparativeFrom = null,
    DateOnly? ComparativeTo = null,
    bool IncludeAccountsWithNoActivity = false) : IQuery<IncomeStatement>;

/// <summary>
/// Builds the income statement.
/// </summary>
/// <remarks>
/// <para>
/// Only posting accounts appear. A totalling account would double every figure it sums, and the
/// subtotals here come from the category structure rather than from totalling ranges, so the
/// statement is right even on a chart whose ranges were set up carelessly.
/// </para>
/// <para>
/// Figures are shown in each account's natural direction, so revenue reads positive. See
/// <see cref="LedgerBalances.AsPresented"/> for why that is worth the small indirection.
/// </para>
/// </remarks>
/// <param name="balances">Reads the ledger.</param>
public sealed class IncomeStatementQueryHandler(LedgerBalances balances)
    : IRequestHandler<IncomeStatementQuery, IncomeStatement>
{
    /// <summary>The blocks of the statement, in the order they are read.</summary>
    private static readonly GlAccountCategory[] Order =
    [
        GlAccountCategory.Income,
        GlAccountCategory.CostOfGoodsSold,
        GlAccountCategory.Expense,
    ];

    /// <inheritdoc />
    public async Task<IncomeStatement> HandleAsync(
        IncomeStatementQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var movement = await balances
            .MovementAsync(request.From, request.To, cancellationToken)
            .ConfigureAwait(false);

        var comparative = request is { ComparativeFrom: { } from, ComparativeTo: { } to }
            ? await balances.MovementAsync(from, to, cancellationToken).ConfigureAwait(false)
            : null;

        var chart = await balances.ChartAsync(cancellationToken).ConfigureAwait(false);
        var currency = await balances.CurrencyAsync(cancellationToken).ConfigureAwait(false);

        var sections = new List<IncomeStatementSection>(Order.Length);
        var totals = new Dictionary<GlAccountCategory, decimal>();
        var comparativeTotals = new Dictionary<GlAccountCategory, decimal>();

        foreach (var category in Order)
        {
            var rows = new List<IncomeStatementRow>();
            var total = 0m;
            var comparativeTotal = 0m;

            foreach (var account in chart.Where(a => a.AccountType is GlAccountType.Posting
                                                     && a.Category == category))
            {
                var amount = LedgerBalances.AsPresented(account, movement.GetValueOrDefault(account.No));

                var comparativeAmount = comparative is null
                    ? (decimal?)null
                    : LedgerBalances.AsPresented(account, comparative.GetValueOrDefault(account.No));

                total += amount;
                comparativeTotal += comparativeAmount ?? 0m;

                if (amount == 0m
                    && (comparativeAmount ?? 0m) == 0m
                    && !request.IncludeAccountsWithNoActivity)
                {
                    continue;
                }

                rows.Add(new IncomeStatementRow(
                    account.No,
                    account.Name,
                    account.NameArabic,
                    account.Indentation,
                    amount,
                    comparativeAmount));
            }

            totals[category] = total;
            comparativeTotals[category] = comparativeTotal;

            sections.Add(new IncomeStatementSection(
                category.ToString(),
                rows,
                total,
                comparative is null ? null : comparativeTotal));
        }

        var grossProfit = totals[GlAccountCategory.Income] - totals[GlAccountCategory.CostOfGoodsSold];
        var netProfit = grossProfit - totals[GlAccountCategory.Expense];

        var comparativeGross = comparativeTotals[GlAccountCategory.Income]
                               - comparativeTotals[GlAccountCategory.CostOfGoodsSold];

        return new IncomeStatement(
            request.From,
            request.To,
            request.ComparativeFrom,
            request.ComparativeTo,
            currency,
            sections,
            grossProfit,
            comparative is null ? null : comparativeGross,
            netProfit,
            comparative is null ? null : comparativeGross - comparativeTotals[GlAccountCategory.Expense]);
    }
}
