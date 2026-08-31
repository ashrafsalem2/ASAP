using ASAP.Modules.Hr.People;

namespace ASAP.Modules.Hr.Payroll;

/// <summary>How many days of a period one contract covered.</summary>
/// <param name="Contract">The contract.</param>
/// <param name="Days">Days of the period it covered.</param>
public readonly record struct ContractDays(EmploymentContract Contract, int Days);

/// <summary>
/// Which contract paid for which days of a pay period.
/// </summary>
/// <remarks>
/// <para>
/// A raise takes effect on a date, not at the start of a month. Somebody promoted on the
/// sixteenth is owed half a month at the old figure and half at the new, and paying the whole
/// month at either one is wrong by a real amount in somebody's actual pay.
/// </para>
/// <para>
/// The same shape as the branch apportionment beside it, and for the same reason: the period is
/// divided by days, and the shares are made to sum to the whole so nothing is lost to rounding.
/// </para>
/// </remarks>
public static class ContractApportionment
{
    /// <summary>
    /// Which contracts covered a period, and for how many days each.
    /// </summary>
    /// <param name="contracts">Every contract the person has.</param>
    /// <param name="from">The first day of the period.</param>
    /// <param name="to">The last day of the period.</param>
    /// <returns>One entry per contract that covered any of it, earliest first.</returns>
    public static IReadOnlyList<ContractDays> Covering(
        IEnumerable<EmploymentContract> contracts,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        var covering = new List<ContractDays>();

        foreach (var contract in contracts.OrderBy(static c => c.StartsOn))
        {
            var start = contract.StartsOn > from ? contract.StartsOn : from;
            var finish = contract.EndsOn is { } ends && ends < to ? ends : to;

            if (finish < start)
            {
                continue;
            }

            covering.Add(new ContractDays(contract, finish.DayNumber - start.DayNumber + 1));
        }

        return covering;
    }

    /// <summary>
    /// What a period's basic and allowances come to across the contracts that covered it.
    /// </summary>
    /// <remarks>
    /// Each contract contributes its own figure for its own share of the period. Where one
    /// contract covers the whole thing this is the contract's figure apportioned for days
    /// actually worked, exactly as before; where two do, the two shares add up.
    /// </remarks>
    /// <param name="covering">The contracts and their days, from <see cref="Covering"/>.</param>
    /// <param name="daysWorked">Days actually worked in the period.</param>
    /// <param name="daysInPeriod">Days in the whole period.</param>
    /// <returns>The basic and the allowances for the period.</returns>
    public static (decimal Basic, decimal Allowances) Wages(
        IReadOnlyList<ContractDays> covering,
        int daysWorked,
        int daysInPeriod)
    {
        ArgumentNullException.ThrowIfNull(covering);

        if (covering.Count == 0 || daysInPeriod <= 0)
        {
            return (0m, 0m);
        }

        if (covering.Count == 1)
        {
            return (
                WageApportionment.ForPartMonth(covering[0].Contract.BasicWage, daysWorked, daysInPeriod),
                WageApportionment.ForPartMonth(covering[0].Contract.Allowances, daysWorked, daysInPeriod));
        }

        // Days worked are spread over the contracts in proportion to the days each covered. A
        // person who joined mid-period and was then promoted has fewer worked days than covered
        // ones, and charging the whole shortfall to one of the two contracts would pay one of
        // them in full for a stretch nobody worked.
        var covered = covering.Sum(static c => c.Days);

        var basic = 0m;
        var allowances = 0m;

        foreach (var entry in covering)
        {
            var share = (decimal)entry.Days / covered;
            var worked = daysWorked * share;

            basic += entry.Contract.BasicWage / daysInPeriod * worked;
            allowances += entry.Contract.Allowances / daysInPeriod * worked;
        }

        return (
            Math.Round(basic, 2, MidpointRounding.AwayFromZero),
            Math.Round(allowances, 2, MidpointRounding.AwayFromZero));
    }
}
