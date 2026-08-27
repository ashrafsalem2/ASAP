using ASAP.Modules.Hr.People;

namespace ASAP.Modules.Hr.Entitlements;

/// <summary>One band of annual leave: how many days a year somebody earns within it.</summary>
/// <param name="UpToYears">The end of the band, or null for the band that runs to the end.</param>
/// <param name="DaysPerYear">How many days a full year inside this band earns.</param>
public readonly record struct LeaveBand(decimal? UpToYears, decimal DaysPerYear);

/// <summary>
/// How annual leave is earned.
/// </summary>
/// <remarks>
/// Data rather than code, for the same reason the end-of-service bands are: a company operating
/// in two countries needs two policies, not two builds. <see cref="Saudi"/> ships and is what
/// nearly every deployment here will use.
/// </remarks>
/// <param name="Name">What the policy is called.</param>
/// <param name="Bands">How many days a year, by length of service.</param>
/// <param name="CarryOverLimitDays">
/// How many days may be carried into the next year, or null for no limit. A limit is what stops a
/// leave liability growing quietly for a decade.
/// </param>
public sealed record LeavePolicy(
    string Name,
    IReadOnlyList<LeaveBand> Bands,
    decimal? CarryOverLimitDays = null)
{
    /// <summary>
    /// The Saudi Labour Law entitlement, which is what ships.
    /// </summary>
    /// <remarks>
    /// Twenty-one days a year, rising to thirty once somebody has been with the employer five
    /// years (article 109). Unlike the end-of-service bands this one is not cumulative: reaching
    /// five years changes the rate from then on, and does not revalue what came before — leave
    /// is taken as it is earned, so there is nothing behind to revalue.
    /// </remarks>
    public static LeavePolicy Saudi { get; } = new(
        "Saudi Labour Law",
        [
            new LeaveBand(5m, 21m),
            new LeaveBand(null, 30m),
        ]);
}

/// <summary>What somebody has earned and what is left.</summary>
/// <param name="EarnedDays">Days accrued over the period.</param>
/// <param name="TakenDays">Days taken.</param>
/// <param name="BroughtForwardDays">Days carried in from last year, after any cap.</param>
/// <param name="BalanceDays">What remains.</param>
/// <param name="ForfeitedDays">
/// Days lost to the carry-over cap. Reported rather than silently dropped: somebody is owed an
/// explanation for leave that disappeared, and the number is also what argues for the cap.
/// </param>
public readonly record struct LeaveBalance(
    decimal EarnedDays,
    decimal TakenDays,
    decimal BroughtForwardDays,
    decimal BalanceDays,
    decimal ForfeitedDays);

/// <summary>
/// Works out annual leave earned and left.
/// </summary>
/// <remarks>
/// <para>
/// Leave accrues by the day rather than in a lump at the start of a year. Somebody who joins in
/// November has earned about two days by the new year, not twenty-one, and a system that granted
/// the year's allowance on hiring would let a new starter take three weeks and leave.
/// </para>
/// <para>
/// The balance in days is also a liability in money, which is why this is worth computing for
/// every current employee rather than only when somebody asks for time off. Unused leave is owed
/// on the day somebody leaves.
/// </para>
/// </remarks>
public static class LeaveAccrual
{
    /// <summary>
    /// How many days somebody earns in a year at their length of service.
    /// </summary>
    /// <param name="serviceYears">How long they have been here.</param>
    /// <param name="policy">The rules, or null for the Saudi ones.</param>
    /// <returns>The annual entitlement.</returns>
    public static decimal EntitlementPerYear(decimal serviceYears, LeavePolicy? policy = null)
    {
        var rules = policy ?? LeavePolicy.Saudi;

        foreach (var band in rules.Bands)
        {
            if (band.UpToYears is not { } ceiling || serviceYears < ceiling)
            {
                return band.DaysPerYear;
            }
        }

        return rules.Bands.Count > 0 ? rules.Bands[^1].DaysPerYear : 0m;
    }

    /// <summary>
    /// What an employee has earned between two days.
    /// </summary>
    /// <remarks>
    /// Accrued a day at a time, because the entitlement changes mid-period the moment somebody
    /// passes five years. Multiplying the whole period by the rate they finished on would grant
    /// thirty days for a year in which they were only entitled to twenty-one for most of it.
    /// </remarks>
    /// <param name="employee">The employee.</param>
    /// <param name="from">The first day to count.</param>
    /// <param name="to">The last day to count.</param>
    /// <param name="policy">The rules, or null for the Saudi ones.</param>
    /// <returns>Days earned over the period.</returns>
    public static decimal EarnedBetween(
        Employee employee,
        DateOnly from,
        DateOnly to,
        LeavePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(employee);

        if (to < from)
        {
            return 0m;
        }

        // Nothing accrues before somebody starts or after they leave.
        var start = from < employee.HiredOn ? employee.HiredOn : from;
        var end = employee.LeftOn is { } left && left < to ? left : to;

        if (end < start)
        {
            return 0m;
        }

        var earned = 0m;

        // Walked rather than integrated, because the band changes on one particular day and the
        // arithmetic that gets that day right is the arithmetic that reads like what it does.
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            earned += EntitlementPerYear(employee.ServiceYearsOn(day), policy) / 365.25m;
        }

        return Round(earned);
    }

    /// <summary>
    /// What is left, once carry-over has been capped.
    /// </summary>
    /// <param name="earnedDays">Days accrued this period.</param>
    /// <param name="takenDays">Days taken.</param>
    /// <param name="broughtForwardDays">Days carried in from last period, before the cap.</param>
    /// <param name="policy">The rules, or null for the Saudi ones.</param>
    /// <returns>The balance, and what was lost to the cap.</returns>
    public static LeaveBalance Balance(
        decimal earnedDays,
        decimal takenDays,
        decimal broughtForwardDays = 0m,
        LeavePolicy? policy = null)
    {
        var rules = policy ?? LeavePolicy.Saudi;

        var capped = rules.CarryOverLimitDays is { } limit
            ? Math.Min(broughtForwardDays, limit)
            : broughtForwardDays;

        return new LeaveBalance(
            Round(earnedDays),
            Round(takenDays),
            Round(capped),
            Round(capped + earnedDays - takenDays),
            Round(broughtForwardDays - capped));
    }

    /// <summary>
    /// What the leave balance is worth, which is what the company owes if everybody left today.
    /// </summary>
    /// <remarks>
    /// A month's wage divided by thirty rather than by the days in the month. That is the
    /// convention here and it is worth stating: dividing by the calendar makes a day of February
    /// worth more than a day of March, and nobody's leave is worth more for being taken in a
    /// short month.
    /// </remarks>
    /// <param name="employee">The employee.</param>
    /// <param name="balanceDays">Days owed.</param>
    /// <returns>What those days are worth.</returns>
    public static decimal Liability(Employee employee, decimal balanceDays)
    {
        ArgumentNullException.ThrowIfNull(employee);

        return Round(employee.TotalWage / 30m * balanceDays);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
