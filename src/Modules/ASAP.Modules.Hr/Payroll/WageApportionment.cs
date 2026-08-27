using ASAP.Modules.Hr.People;

namespace ASAP.Modules.Hr.Payroll;

/// <summary>How much of a period somebody spent at one branch, and what it cost.</summary>
/// <param name="BranchId">The branch.</param>
/// <param name="Days">Days worked there inside the period.</param>
/// <param name="Amount">The share of the wage it carries.</param>
public readonly record struct BranchShare(Guid BranchId, int Days, decimal Amount);

/// <summary>
/// Splits a period's wage between the branches somebody actually worked at.
/// </summary>
/// <remarks>
/// <para>
/// This is what the effective-dated branch history exists for. Somebody who transfers on the
/// sixteenth costs each branch half the month, and a system holding only a current branch would
/// charge the whole month wherever they happened to be on payday — so the branch they left looks
/// cheaper than it was, every time anybody moves.
/// </para>
/// <para>
/// The shares are made to sum to exactly the wage. Three branches and a wage of a thousand gives
/// 333.33 three times, which is 999.99, and the missing halala has to land somewhere rather than
/// being lost: a payroll journal that does not balance is not a rounding problem, it is a journal
/// that will not post.
/// </para>
/// </remarks>
public static class WageApportionment
{
    /// <summary>
    /// Splits a wage across the branches somebody worked at over a period.
    /// </summary>
    /// <param name="employee">The employee, with their branch assignments loaded.</param>
    /// <param name="from">The first day of the period.</param>
    /// <param name="to">The last day of the period.</param>
    /// <param name="amount">What they are paid for the period.</param>
    /// <returns>
    /// One share per branch, largest first, summing to exactly <paramref name="amount"/>. Empty
    /// when they worked no days in the period.
    /// </returns>
    public static IReadOnlyList<BranchShare> Split(
        Employee employee,
        DateOnly from,
        DateOnly to,
        decimal amount)
    {
        ArgumentNullException.ThrowIfNull(employee);

        return Reapportion(DaysByBranch(employee, from, to).Select(static d => (d.Key, d.Value)), amount);
    }

    /// <summary>
    /// Splits an amount across branches in the same proportion as a set of days already worked out.
    /// </summary>
    /// <remarks>
    /// The wage is not the only thing a month at a branch costs. What somebody earned this month
    /// towards their end-of-service award, and what they were docked, both belong to the branches
    /// the days were spent at, in the same proportion and by the same rounding rule. Charging
    /// them anywhere else leaves every branch figure wrong by a little and head office wrong by
    /// the sum of it.
    /// </remarks>
    /// <param name="across">Branches and the days each carries.</param>
    /// <param name="amount">The amount to divide.</param>
    /// <returns>A share per branch, summing to <paramref name="amount"/> exactly.</returns>
    public static IReadOnlyList<BranchShare> Reapportion(
        IEnumerable<(Guid BranchId, int Days)> across,
        decimal amount)
    {
        ArgumentNullException.ThrowIfNull(across);

        // Largest first, so the halalas left over by rounding land on the branch that carries
        // most of the wage. Putting them on the smallest share would make a branch somebody
        // visited for two days carry the remainder of everybody's rounding.
        var ordered = across
            .Where(static d => d.Days > 0)
            .OrderByDescending(static d => d.Days)
            .ThenBy(static d => d.BranchId)
            .ToList();

        var worked = ordered.Sum(static d => d.Days);

        if (worked == 0)
        {
            return [];
        }

        var shares = new List<BranchShare>(ordered.Count);
        var allocated = 0m;

        for (var i = 0; i < ordered.Count; i++)
        {
            var (branchId, branchDays) = ordered[i];

            // The last share is whatever is left rather than its own rounded fraction. That is
            // what guarantees the parts sum to the whole, and it is why this is worth a function
            // rather than a division at each call site.
            var share = i == ordered.Count - 1
                ? amount - allocated
                : Round(amount * branchDays / worked);

            allocated += share;
            shares.Add(new BranchShare(branchId, branchDays, share));
        }

        return shares;
    }

    /// <summary>
    /// How many days of the period somebody spent at each branch.
    /// </summary>
    /// <remarks>
    /// Days before they were hired and after they left are not counted, so somebody who starts
    /// on the fifteenth costs half a month rather than a whole one.
    /// </remarks>
    /// <param name="employee">The employee.</param>
    /// <param name="from">The first day of the period.</param>
    /// <param name="to">The last day of the period.</param>
    /// <returns>Days per branch, and nothing for days nobody was responsible for.</returns>
    public static Dictionary<Guid, int> DaysByBranch(Employee employee, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(employee);

        var days = new Dictionary<Guid, int>();

        if (to < from)
        {
            return days;
        }

        var start = from < employee.HiredOn ? employee.HiredOn : from;
        var end = employee.LeftOn is { } left && left < to ? left : to;

        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (EmployeeService.BranchOn(employee, day) is not { } branchId)
            {
                continue;
            }

            days[branchId] = days.GetValueOrDefault(branchId) + 1;
        }

        return days;
    }

    /// <summary>
    /// Days in the period that belong to nobody.
    /// </summary>
    /// <remarks>
    /// Worth asking separately from the split, because a day with no branch is silently dropped
    /// by the arithmetic above and the cost would then be apportioned across the branches that
    /// do have days — quietly charging them for a day they had nothing to do with.
    /// </remarks>
    /// <param name="employee">The employee.</param>
    /// <param name="from">The first day of the period.</param>
    /// <param name="to">The last day of the period.</param>
    /// <returns>How many worked days had no branch.</returns>
    public static int UnassignedDays(Employee employee, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(employee);

        if (to < from)
        {
            return 0;
        }

        var start = from < employee.HiredOn ? employee.HiredOn : from;
        var end = employee.LeftOn is { } left && left < to ? left : to;
        var unassigned = 0;

        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (EmployeeService.BranchOn(employee, day) is null)
            {
                unassigned++;
            }
        }

        return unassigned;
    }

    /// <summary>
    /// What somebody is paid for a period they did not work all of.
    /// </summary>
    /// <remarks>
    /// A month's wage divided by thirty rather than by the days in the month, which is the
    /// convention here and the same one leave is valued by. Dividing by the calendar makes a day
    /// of February worth more than a day of March, and nobody's work is worth more for being done
    /// in a short month.
    /// </remarks>
    /// <param name="monthlyWage">A full month's wage.</param>
    /// <param name="daysWorked">Days actually worked in the month.</param>
    /// <param name="daysInPeriod">Days the period covers.</param>
    /// <returns>What is due for the days worked.</returns>
    public static decimal ForPartMonth(decimal monthlyWage, int daysWorked, int daysInPeriod)
    {
        if (daysWorked <= 0 || daysInPeriod <= 0)
        {
            return 0m;
        }

        return daysWorked >= daysInPeriod
            ? Round(monthlyWage)
            : Round(monthlyWage / 30m * daysWorked);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
