using ASAP.Modules.Hr.People;

namespace ASAP.Modules.Hr.Entitlements;

/// <summary>What somebody is owed when they go, and how it was arrived at.</summary>
/// <param name="ServiceYears">How long they were here.</param>
/// <param name="MonthlyWage">The wage the award is measured on.</param>
/// <param name="FullAward">What the award comes to before any reduction for resigning.</param>
/// <param name="RetainedFraction">The share of it they keep, which is one unless they resigned.</param>
/// <param name="Award">What is actually owed.</param>
public readonly record struct EndOfServiceAward(
    decimal ServiceYears,
    decimal MonthlyWage,
    decimal FullAward,
    decimal RetainedFraction,
    decimal Award)
{
    /// <summary>How much was lost by resigning rather than being let go.</summary>
    /// <remarks>
    /// Worth reporting on its own. Somebody deciding whether to resign this month or next is
    /// making a decision worth several months' wages if they are near a band, and a payroll
    /// office that can only show the final figure cannot explain why.
    /// </remarks>
    public decimal ForfeitedByResigning => FullAward - Award;
}

/// <summary>
/// Works out an end-of-service award.
/// </summary>
/// <remarks>
/// <para>
/// Free of the database and the clock. Everything it needs is passed in, which is what lets the
/// awkward cases — a resignation two days short of a band, eleven years of service, a policy from
/// another country — be tested without hiring anybody.
/// </para>
/// <para>
/// The provision this produces is also what the company should be carrying on its balance sheet
/// for every current employee, not only for leavers. An accrued liability nobody computes until
/// somebody resigns is a liability that arrives as a surprise.
/// </para>
/// </remarks>
public static class EndOfServiceCalculator
{
    /// <summary>
    /// What an employee would be owed if they left on a given day.
    /// </summary>
    /// <param name="employee">The employee.</param>
    /// <param name="on">The day they leave, or the day the provision is being measured at.</param>
    /// <param name="policy">The rules to apply, or null for the Saudi ones.</param>
    /// <param name="reason">
    /// Why they are leaving. Defaults to termination, which is the full award — the right default
    /// for a provision, because a company must carry what it might owe rather than what it hopes
    /// to owe if everybody happens to resign early.
    /// </param>
    /// <returns>The award, and the working behind it.</returns>
    public static EndOfServiceAward For(
        Employee employee,
        DateOnly on,
        EndOfServicePolicy? policy = null,
        LeavingReason reason = LeavingReason.Termination)
    {
        ArgumentNullException.ThrowIfNull(employee);

        var rules = policy ?? EndOfServicePolicy.Saudi;
        var years = employee.ServiceYearsOn(on);
        var wage = rules.OnBasicWageOnly ? employee.BasicWage : employee.TotalWage;

        var months = MonthsEarned(years, rules.Bands);
        var full = Round(months * wage);
        var retained = RetainedFraction(years, reason, rules);

        return new EndOfServiceAward(
            Round(years),
            wage,
            full,
            retained,
            Round(full * retained));
    }

    /// <summary>
    /// How many months of wage the service has earned.
    /// </summary>
    /// <remarks>
    /// Bands are cumulative the way income tax bands are: the first five years earn at the first
    /// rate however long somebody stays. Revaluing every year at the rate of the band they end in
    /// is the mistake that doubles a provision, and it is an easy one to make because it reads
    /// like a simpler rule.
    /// </remarks>
    public static decimal MonthsEarned(decimal serviceYears, IReadOnlyList<AwardBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);

        if (serviceYears <= 0m)
        {
            return 0m;
        }

        var months = 0m;
        var counted = 0m;

        foreach (var band in bands)
        {
            var ceiling = band.UpToYears ?? serviceYears;
            var inThisBand = Math.Min(serviceYears, ceiling) - counted;

            if (inThisBand <= 0m)
            {
                continue;
            }

            months += inThisBand * band.MonthsPerYear;
            counted += inThisBand;

            if (counted >= serviceYears)
            {
                break;
            }
        }

        return months;
    }

    /// <summary>
    /// How much of the award somebody keeps.
    /// </summary>
    /// <remarks>
    /// Only a resignation is reduced. Where the employer ends the contract — or where the law
    /// treats the ending as statutory, such as retirement — the whole award is due, and a system
    /// that applied the resignation bands to a redundancy would underpay somebody who had just
    /// been let go.
    /// </remarks>
    public static decimal RetainedFraction(
        decimal serviceYears,
        LeavingReason reason,
        EndOfServicePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (reason is not LeavingReason.Resignation)
        {
            return 1m;
        }

        foreach (var band in policy.ResignationBands)
        {
            if (band.UpToYears is not { } ceiling || serviceYears < ceiling)
            {
                return band.Fraction;
            }
        }

        return 1m;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
