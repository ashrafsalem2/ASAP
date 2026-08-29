using ASAP.Modules.Hr.People;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Hr.Reporting;

/// <summary>How many people are at one branch on a day, or working nowhere in particular.</summary>
/// <param name="BranchId">The branch, or null for staff with no current assignment.</param>
/// <param name="BranchCode">Its code, when there is one.</param>
/// <param name="BranchName">Its name, when there is one.</param>
/// <param name="Count">How many people.</param>
public readonly record struct HeadcountRow(
    Guid? BranchId,
    string? BranchCode,
    string? BranchName,
    int Count);

/// <summary>What one branch's staff cost, on a day.</summary>
/// <param name="BranchId">The branch, or null for staff with no current assignment.</param>
/// <param name="BranchCode">Its code, when there is one.</param>
/// <param name="BranchName">Its name, when there is one.</param>
/// <param name="Count">How many people it carries.</param>
/// <param name="MonthlyWageCost">What a month costs at their current basic and allowances.</param>
public readonly record struct BranchCostRow(
    Guid? BranchId,
    string? BranchCode,
    string? BranchName,
    int Count,
    decimal MonthlyWageCost);

/// <summary>How many people came and went over a period, and what that comes to as a rate.</summary>
/// <param name="FromDate">The first day of the period.</param>
/// <param name="ToDate">The last day of the period.</param>
/// <param name="OpeningHeadcount">Who was here at the start.</param>
/// <param name="Hired">How many started during the period.</param>
/// <param name="Left">How many left during the period.</param>
/// <param name="ClosingHeadcount">Who was here at the end.</param>
/// <param name="TurnoverRate">
/// Leavers against the average of the opening and closing headcount, which is the usual way the
/// figure is quoted -- against the number who could have left rather than against the number who
/// started, which a business hiring quickly would otherwise flatter.
/// </param>
public readonly record struct TurnoverSummary(
    DateOnly FromDate,
    DateOnly ToDate,
    int OpeningHeadcount,
    int Hired,
    int Left,
    int ClosingHeadcount,
    decimal TurnoverRate);

/// <summary>
/// Reports on the staff list itself, rather than on what any one of them is owed.
/// </summary>
/// <remarks>
/// Deliberately apart from <see cref="People.EmployeeService.EntitlementsAsync"/>, which answers
/// what the company owes. This answers who is here, where, at what cost, and how many have come
/// and gone -- questions a branch manager or head office asks about people as a group, not about
/// any one person's pay.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="clock">Supplies today.</param>
public sealed class HrReportingService(AsapDbContext context, IClock clock)
{
    /// <summary>How many people are at each branch on a day.</summary>
    /// <param name="on">The day to measure at, or null for today.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per branch with anybody assigned, most staffed first.</returns>
    public async Task<IReadOnlyList<HeadcountRow>> HeadcountByBranchAsync(
        DateOnly? on = null,
        CancellationToken cancellationToken = default)
    {
        var byBranch = await GroupByCurrentBranchAsync(on ?? clock.Today, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. byBranch
                .Select(static g => new HeadcountRow(
                    g.Branch?.Id, g.Branch?.Code, g.Branch?.Name, g.Employees.Count))
                .OrderByDescending(static r => r.Count),
        ];
    }

    /// <summary>What each branch's staff cost, on a day.</summary>
    /// <param name="on">The day to measure at, or null for today.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per branch with anybody assigned, most costly first.</returns>
    public async Task<IReadOnlyList<BranchCostRow>> CostByBranchAsync(
        DateOnly? on = null,
        CancellationToken cancellationToken = default)
    {
        var byBranch = await GroupByCurrentBranchAsync(on ?? clock.Today, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. byBranch
                .Select(static g => new BranchCostRow(
                    g.Branch?.Id,
                    g.Branch?.Code,
                    g.Branch?.Name,
                    g.Employees.Count,
                    g.Employees.Sum(static e => e.TotalWage)))
                .OrderByDescending(static r => r.MonthlyWageCost),
        ];
    }

    /// <summary>How many people came and went over a period.</summary>
    /// <param name="fromDate">The first day of the period.</param>
    /// <param name="toDate">The last day of the period.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The counts, and the rate they come to.</returns>
    public async Task<TurnoverSummary> TurnoverAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        // Every employee ever hired here, leavers included -- a turnover figure computed only
        // over people still employed would not be turnover.
        var employees = await context.Set<Employee>()
            .AsNoTracking()
            .Select(static e => new { e.HiredOn, e.LeftOn })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // "As of" both boundaries the same way -- somebody hired exactly on fromDate is already
        // part of the opening count, the same as somebody hired exactly on toDate is already part
        // of the closing one. Asking the two questions with different rules would make a hire
        // dated on the boundary appear or vanish depending only on which count was asked first.
        var opening = employees.Count(e => e.HiredOn <= fromDate && (e.LeftOn is null || e.LeftOn >= fromDate));
        var hired = employees.Count(e => e.HiredOn > fromDate && e.HiredOn <= toDate);
        var left = employees.Count(e => e.LeftOn is { } leftOn && leftOn > fromDate && leftOn <= toDate);
        var closing = employees.Count(e => e.HiredOn <= toDate && (e.LeftOn is null || e.LeftOn > toDate));

        var average = (opening + closing) / 2m;
        var rate = average > 0m ? Math.Round(left / average, 4, MidpointRounding.AwayFromZero) : 0m;

        return new TurnoverSummary(fromDate, toDate, opening, hired, left, closing, rate);
    }

    /// <summary>Everybody currently employed, grouped by where they were assigned on a day.</summary>
    private async Task<IReadOnlyList<(Branch? Branch, List<Employee> Employees)>> GroupByCurrentBranchAsync(
        DateOnly on,
        CancellationToken cancellationToken)
    {
        var employees = await context.Set<Employee>()
            .AsNoTracking()
            .Where(static e => e.Status == EmploymentStatus.Active || e.Status == EmploymentStatus.Suspended)
            .Include(static e => e.BranchAssignments)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var branchIds = employees
            .Select(e => EmployeeService.BranchOn(e, on))
            .Where(static id => id is not null)
            .Select(static id => id!.Value)
            .Distinct()
            .ToList();

        var branches = await context.Set<Branch>()
            .AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(static b => b.Id, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. employees
                .GroupBy(e => EmployeeService.BranchOn(e, on))
                .Select(g => (
                    Branch: g.Key is { } id && branches.TryGetValue(id, out var branch) ? branch : null,
                    Employees: g.ToList())),
        ];
    }
}
