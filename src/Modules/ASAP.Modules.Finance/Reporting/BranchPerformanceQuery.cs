using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Ledger;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>What one branch earned and spent over a range.</summary>
/// <param name="BranchId">The branch, or null for the unattributed row.</param>
/// <param name="Code">Its code, or null where nothing names it.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its Arabic name.</param>
/// <param name="Revenue">What it sold.</param>
/// <param name="CostOfSales">What those goods cost.</param>
/// <param name="GrossProfit">The two, subtracted.</param>
/// <param name="Expenses">What it cost to run, staff included.</param>
/// <param name="StaffCost">
/// How much of the expenses are people. Broken out because it is the largest controllable cost a
/// shop has and the one a manager is actually asked about.
/// </param>
/// <param name="Result">Gross profit less expenses.</param>
/// <param name="GrossMarginPercent">
/// Gross profit as a share of revenue, or null where nothing was sold. Null rather than zero: a
/// branch that sold nothing has no margin, which is a different statement from a margin of nil.
/// </param>
public sealed record BranchPerformanceRow(
    Guid? BranchId,
    string? Code,
    string Name,
    string? NameArabic,
    decimal Revenue,
    decimal CostOfSales,
    decimal GrossProfit,
    decimal Expenses,
    decimal StaffCost,
    decimal Result,
    decimal? GrossMarginPercent);

/// <summary>What every branch earned and spent over a range.</summary>
/// <param name="From">First day covered.</param>
/// <param name="To">Last day covered.</param>
/// <param name="CurrencyCode">Currency the figures are in.</param>
/// <param name="Branches">One row per branch, best result first.</param>
/// <param name="Unattributed">
/// What was posted with no branch at all. Shown separately rather than spread or hidden: it is
/// usually head office, sometimes a document nobody stamped, and a reader needs to know which
/// share of the company's result the branch rows do not account for.
/// </param>
/// <param name="Total">Every row added together, which reconciles to the income statement.</param>
public sealed record BranchPerformance(
    DateOnly From,
    DateOnly To,
    string CurrencyCode,
    IReadOnlyList<BranchPerformanceRow> Branches,
    BranchPerformanceRow? Unattributed,
    BranchPerformanceRow Total);

/// <summary>
/// Asks what each branch earned and spent between two dates.
/// </summary>
/// <param name="From">First day to include.</param>
/// <param name="To">Last day to include.</param>
/// <param name="IncludeInactive">Whether to show branches that have been closed.</param>
[RequiresPermission("Finance", "Report", PermissionAction.Read)]
public sealed record BranchPerformanceQuery(
    DateOnly From,
    DateOnly To,
    bool IncludeInactive = false) : IQuery<BranchPerformance>;

/// <summary>
/// Builds the branch performance report.
/// </summary>
/// <remarks>
/// <para>
/// An income statement cut by branch, and nothing more clever than that. Every figure comes from
/// the same ledger entries the company-wide statement is built from, which is what makes the rows
/// add up to it — a report that reconciled only approximately would be argued with rather than
/// acted on.
/// </para>
/// <para>
/// It is only as good as the branch on the entries. A sale posts at the shop that made it, a
/// wage at the shop the days were worked at, a stock movement at the place the goods moved. Where
/// nothing said, the entry lands in the unattributed row rather than being spread across the
/// branches on some plausible basis, because a made-up allocation is indistinguishable from a
/// measured one once it is in a table.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="balances">Reads the chart and the reporting currency.</param>
/// <param name="setup">Says which account the wage cost is charged to.</param>
public sealed class BranchPerformanceQueryHandler(
    AsapDbContext context,
    LedgerBalances balances,
    ISetupService setup)
    : IRequestHandler<BranchPerformanceQuery, BranchPerformance>
{
    /// <summary>
    /// Which accounts are counted as staff cost.
    /// </summary>
    /// <remarks>
    /// Taken from what HR posts to rather than from a range of account numbers, so renumbering
    /// the chart does not silently empty the column.
    /// </remarks>
    // Both accounts payroll charges a branch on. The end-of-service charge sits apart from the
    // wage in the chart so the profit and loss can tell paid from provided for, but a branch
    // manager asking what staff cost them wants the two added together.
    private static readonly string[] StaffCostSettings =
    [
        "Hr.Posting.WageAccount",
        "Hr.Posting.EndOfServiceExpenseAccount",
    ];

    /// <inheritdoc />
    public async Task<BranchPerformance> HandleAsync(
        BranchPerformanceQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var chart = await balances.ChartAsync(cancellationToken).ConfigureAwait(false);
        var currency = await balances.CurrencyAsync(cancellationToken).ConfigureAwait(false);

        var categories = chart.ToDictionary(
            static a => a.No,
            static a => a.Category,
            StringComparer.OrdinalIgnoreCase);

        var staffAccounts = await StaffAccountsAsync(cancellationToken).ConfigureAwait(false);

        // One query, grouped in the database. Pulling every entry back to add them up here would
        // work on a demo and fall over on a year of till receipts.
        var movements = await context.Set<GlEntry>()
            .AsNoTracking()
            .Where(e => e.PostingDate >= request.From && e.PostingDate <= request.To)
            .GroupBy(static e => new { e.BranchId, e.AccountNo })
            .Select(static g => new
            {
                g.Key.BranchId,
                g.Key.AccountNo,
                Amount = g.Sum(static e => e.Amount),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var branches = await BranchesAsync(request.IncludeInactive, cancellationToken)
            .ConfigureAwait(false);

        // Every branch appears whether or not it traded. A shop missing from the list because it
        // took nothing reads as a shop nobody thought to include.
        var byBranch = branches.Keys.ToDictionary(static id => id, static _ => new Figures());

        // Anything with no branch, or against one that was filtered out, lands here rather than
        // vanishing: the rows still have to add up to the company.
        var unattributed = new Figures();
        var sawUnattributed = false;

        foreach (var movement in movements)
        {
            if (!categories.TryGetValue(movement.AccountNo, out var category))
            {
                continue;
            }

            Figures figures;

            if (movement.BranchId is { } id && byBranch.TryGetValue(id, out var branchFigures))
            {
                figures = branchFigures;
            }
            else
            {
                figures = unattributed;
                sawUnattributed = true;
            }

            figures.Add(
                category,
                movement.Amount,
                staffAccounts.Contains(movement.AccountNo));
        }

        var rows = byBranch
            .Select(pair => Row(pair.Key, branches[pair.Key], pair.Value))
            .OrderByDescending(static r => r.Result)
            .ThenBy(static r => r.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var whole = new Figures();
        whole.Absorb(unattributed);

        foreach (var figures in byBranch.Values)
        {
            whole.Absorb(figures);
        }

        return new BranchPerformance(
            request.From,
            request.To,
            currency,
            rows,
            sawUnattributed ? Row(null, ("", "Not attributed to a branch", "غير محمّل على فرع"), unattributed) : null,
            Row(null, ("", "Company", "الشركة"), whole));
    }

    private async Task<Dictionary<Guid, (string Code, string Name, string? NameArabic)>> BranchesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = context.Branches.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(static b => b.IsActive);
        }

        return await query
            .ToDictionaryAsync(
                static b => b.Id,
                static b => ValueTuple.Create(b.Code, b.Name, b.NameArabic),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HashSet<string>> StaffAccountsAsync(CancellationToken cancellationToken)
    {
        var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in StaffCostSettings)
        {
            var value = await setup
                .GetAsync<string>(key, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(value))
            {
                accounts.Add(value.Trim());
            }
        }

        // The shipped default, so a company that never touched the setting still gets a figure.
        // Wrong only where somebody moved the setting and this could not read it, and a staff
        // cost of nil on a shop full of staff is more obviously wrong than a slightly stale one.
        accounts.Add("6100");
        accounts.Add("6110");

        return accounts;
    }

    private static BranchPerformanceRow Row(
        Guid? branchId,
        (string Code, string Name, string? NameArabic) branch,
        Figures figures)
    {
        var grossProfit = figures.Revenue - figures.CostOfSales;
        var result = grossProfit - figures.Expenses;

        return new BranchPerformanceRow(
            branchId,
            string.IsNullOrEmpty(branch.Code) ? null : branch.Code,
            branch.Name,
            branch.NameArabic,
            figures.Revenue,
            figures.CostOfSales,
            grossProfit,
            figures.Expenses,
            figures.StaffCost,
            result,
            figures.Revenue == 0m ? null : Math.Round(grossProfit / figures.Revenue * 100m, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// The running totals for one branch, in the direction a reader expects to see them.
    /// </summary>
    /// <remarks>
    /// Revenue is stored as a credit and so arrives negative. It is flipped once, here, rather
    /// than at each of the places that reads it — the same reasoning as
    /// <see cref="LedgerBalances.AsPresented"/>, which cannot be reused because these figures are
    /// summed by category rather than per account.
    /// </remarks>
    private sealed class Figures
    {
        public decimal Revenue { get; private set; }

        public decimal CostOfSales { get; private set; }

        public decimal Expenses { get; private set; }

        public decimal StaffCost { get; private set; }

        public void Add(GlAccountCategory category, decimal amount, bool isStaffCost)
        {
            switch (category)
            {
                case GlAccountCategory.Income:
                    this.Revenue -= amount;
                    break;

                case GlAccountCategory.CostOfGoodsSold:
                    this.CostOfSales += amount;
                    break;

                case GlAccountCategory.Expense:
                    this.Expenses += amount;

                    if (isStaffCost)
                    {
                        this.StaffCost += amount;
                    }

                    break;

                default:
                    // Assets, liabilities and equity are not part of a result. Cash moving in and
                    // out of a shop's drawer says nothing about whether the shop made money.
                    break;
            }
        }

        public void Absorb(Figures other)
        {
            this.Revenue += other.Revenue;
            this.CostOfSales += other.CostOfSales;
            this.Expenses += other.Expenses;
            this.StaffCost += other.StaffCost;
        }
    }
}
