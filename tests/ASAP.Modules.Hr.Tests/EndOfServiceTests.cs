using ASAP.Modules.Hr.Entitlements;
using ASAP.Modules.Hr.People;
using Shouldly;

namespace ASAP.Modules.Hr.Tests;

/// <summary>
/// Covers what somebody is owed when they go.
/// </summary>
/// <remarks>
/// Every figure here is one a person will be paid, and several are ones they will check. The
/// mistake this file exists to prevent is the plausible one: revaluing all of somebody's service
/// at the rate of the band they finished in, which reads like a simpler rule and roughly doubles
/// a long-serving employee's award.
/// </remarks>
public sealed class EndOfServiceTests
{
    private static Employee Employee(
        DateOnly hiredOn,
        DateOnly? leftOn = null,
        decimal basicWage = 8_000m,
        decimal allowances = 2_000m)
        => new()
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            No = "EMP-0001",
            Name = "Salim Al Harbi",
            HiredOn = hiredOn,
            LeftOn = leftOn,
            BasicWage = basicWage,
            Allowances = allowances,
        };

    /// <summary>
    /// Somebody hired roughly this many years before the day they leave.
    /// </summary>
    /// <remarks>
    /// Roughly, and it cannot be otherwise: a length of service derived from two dates is never
    /// an exact number of years, because 3,652 days is 9.9986 of them. The band arithmetic is
    /// tested separately on exact inputs; what is checked through here is the whole calculation,
    /// to the nearest riyal.
    /// </remarks>
    private static Employee AfterYears(decimal years, decimal basicWage = 8_000m, decimal allowances = 2_000m)
    {
        var left = new DateOnly(2026, 1, 1);
        var hired = left.AddDays(-(int)Math.Round(years * 365.25m));

        return Employee(hired, left, basicWage, allowances);
    }

    private static readonly DateOnly Leaving = new(2026, 1, 1);

    [Fact]
    public void The_award_is_measured_on_the_whole_wage_including_allowances()
    {
        // Saudi law says the last wage, and the last wage includes housing and transport. A
        // policy that quietly used the basic would understate every award by whatever housing is
        // worth, which for most people here is a quarter of what they are paid.
        var award = EndOfServiceCalculator.For(AfterYears(2m), Leaving);

        award.MonthlyWage.ShouldBe(10_000m);
    }

    [Fact]
    public void Half_a_month_a_year_for_the_first_five()
    {
        // Exact inputs, because this is the band arithmetic and nothing else.
        EndOfServiceCalculator.MonthsEarned(4m, EndOfServicePolicy.Saudi.Bands).ShouldBe(2m);
        EndOfServiceCalculator.MonthsEarned(5m, EndOfServicePolicy.Saudi.Bands).ShouldBe(2.5m);
    }

    [Fact]
    public void A_full_month_a_year_after_five()
    {
        // Ten years is five at half and five at whole: 2.5 + 5 = 7.5 months.
        EndOfServiceCalculator.MonthsEarned(10m, EndOfServicePolicy.Saudi.Bands).ShouldBe(7.5m);
        EndOfServiceCalculator.MonthsEarned(6m, EndOfServicePolicy.Saudi.Bands).ShouldBe(3.5m);
    }

    [Fact]
    public void The_early_years_are_not_revalued_at_the_later_rate()
    {
        // The mistake this file exists for. Ten years at a full month each would be ten months --
        // a third again -- and it reads like a simpler rule, which is exactly why it gets written.
        var earned = EndOfServiceCalculator.MonthsEarned(10m, EndOfServicePolicy.Saudi.Bands);

        earned.ShouldBe(7.5m);
        earned.ShouldNotBe(10m, "the first five years stay at half a month");
    }

    [Fact]
    public void A_part_year_earns_a_part_award()
    {
        // Nobody leaves on an anniversary.
        EndOfServiceCalculator.MonthsEarned(2.5m, EndOfServicePolicy.Saudi.Bands).ShouldBe(1.25m);
        EndOfServiceCalculator.MonthsEarned(0.5m, EndOfServicePolicy.Saudi.Bands).ShouldBe(0.25m);
    }

    [Fact]
    public void The_whole_calculation_agrees_with_the_bands()
    {
        // End to end, through the dates, to the nearest riyal -- which is what somebody is paid
        // to. Ten years on ten thousand is seven and a half months.
        var award = EndOfServiceCalculator.For(AfterYears(10m), Leaving);

        award.FullAward.ShouldBe(75_000m, 20m);
    }

    [Fact]
    public void Somebody_let_go_keeps_the_whole_award()
    {
        var award = EndOfServiceCalculator.For(
            AfterYears(3m),
            Leaving,
            reason: LeavingReason.Termination);

        award.RetainedFraction.ShouldBe(1m);
        award.Award.ShouldBe(award.FullAward);
        award.ForfeitedByResigning.ShouldBe(0m);
    }

    [Fact]
    public void Resigning_inside_two_years_earns_nothing()
    {
        var award = EndOfServiceCalculator.For(
            AfterYears(1.9m),
            Leaving,
            reason: LeavingReason.Resignation);

        award.FullAward.ShouldBeGreaterThan(0m, "the service was earned");
        award.Award.ShouldBe(0m, "and forfeited by resigning this early");
        award.ForfeitedByResigning.ShouldBe(award.FullAward);
    }

    [Fact]
    public void Resigning_between_two_and_five_years_keeps_a_third()
    {
        var award = EndOfServiceCalculator.For(
            AfterYears(4m),
            Leaving,
            reason: LeavingReason.Resignation);

        award.FullAward.ShouldBe(20_000m, 20m);
        award.Award.ShouldBe(6_666.67m, 10m);
    }

    [Fact]
    public void Resigning_between_five_and_ten_years_keeps_two_thirds()
    {
        // Seven years: five at half and two at whole is 4.5 months, 45,000. Two thirds is 30,000.
        var award = EndOfServiceCalculator.For(
            AfterYears(7m),
            Leaving,
            reason: LeavingReason.Resignation);

        award.FullAward.ShouldBe(45_000m, 20m);
        award.Award.ShouldBe(30_000m, 15m);
    }

    [Fact]
    public void Resigning_after_ten_years_keeps_the_whole_award()
    {
        var award = EndOfServiceCalculator.For(
            AfterYears(11m),
            Leaving,
            reason: LeavingReason.Resignation);

        award.RetainedFraction.ShouldBe(1m);
        award.Award.ShouldBe(award.FullAward);
    }

    [Fact]
    public void A_day_either_side_of_a_band_is_worth_a_great_deal()
    {
        // Somebody deciding whether to resign this month or next is making a decision worth
        // several months' wages if they are near a band. Both figures have to be right, and the
        // difference has to be reportable, or a payroll office cannot explain it.
        var justUnder = EndOfServiceCalculator.For(
            AfterYears(4.99m),
            Leaving,
            reason: LeavingReason.Resignation);

        var justOver = EndOfServiceCalculator.For(
            AfterYears(5.01m),
            Leaving,
            reason: LeavingReason.Resignation);

        justOver.Award.ShouldBeGreaterThan(justUnder.Award * 1.9m, "a third becomes two thirds");
        justUnder.ForfeitedByResigning.ShouldBeGreaterThan(justOver.ForfeitedByResigning);
    }

    [Fact]
    public void Somebody_who_never_started_is_owed_nothing()
    {
        var award = EndOfServiceCalculator.For(
            Employee(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)),
            Leaving);

        award.ServiceYears.ShouldBe(0m);
        award.Award.ShouldBe(0m);
    }

    [Fact]
    public void A_calculation_run_after_they_left_gives_what_they_were_owed_then()
    {
        // Not what they would be owed had they stayed. A payroll office reopening a leaver's file
        // in March must see the March figure they were paid, not a larger one.
        var employee = Employee(new DateOnly(2020, 1, 1), new DateOnly(2024, 1, 1));

        var atLeaving = EndOfServiceCalculator.For(employee, new DateOnly(2024, 1, 1));
        var muchLater = EndOfServiceCalculator.For(employee, new DateOnly(2026, 6, 1));

        muchLater.Award.ShouldBe(atLeaving.Award);
    }

    [Fact]
    public void Another_country_is_a_policy_rather_than_a_fork()
    {
        // The bands are data. A company operating in two jurisdictions needs two policies, not
        // two builds, and this is the test that says the arithmetic does not assume Saudi.
        var flat = new EndOfServicePolicy(
            "One month a year, no reduction",
            [new AwardBand(null, 1m)],
            [new ResignationBand(null, 1m)]);

        var award = EndOfServiceCalculator.For(
            AfterYears(10m),
            Leaving,
            flat,
            LeavingReason.Resignation);

        award.FullAward.ShouldBe(100_000m, 30m);
        award.Award.ShouldBe(100_000m, 30m);
    }

    [Fact]
    public void A_policy_may_measure_on_the_basic_wage_alone()
    {
        var basicOnly = EndOfServicePolicy.Saudi with { OnBasicWageOnly = true };

        var award = EndOfServiceCalculator.For(AfterYears(4m), Leaving, basicOnly);

        award.MonthlyWage.ShouldBe(8_000m);
        award.FullAward.ShouldBe(16_000m, 20m);
    }
}
