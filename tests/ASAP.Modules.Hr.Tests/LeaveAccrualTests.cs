using ASAP.Modules.Hr.Entitlements;
using ASAP.Modules.Hr.People;
using Shouldly;

namespace ASAP.Modules.Hr.Tests;

/// <summary>
/// Covers how annual leave is earned and what it is worth.
/// </summary>
/// <remarks>
/// The mistake here is the opposite of the end-of-service one. Leave is not cumulative — passing
/// five years changes the rate from then on and does not revalue what came before, because leave
/// is taken as it is earned and there is nothing behind to revalue. Applying the end-of-service
/// pattern here would hand somebody nine extra days on their fifth anniversary.
/// </remarks>
public sealed class LeaveAccrualTests
{
    private static Employee Employee(
        DateOnly hiredOn,
        DateOnly? leftOn = null,
        decimal basicWage = 9_000m,
        decimal allowances = 3_000m)
        => new()
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            No = "EMP-0002",
            Name = "Nora Al Qahtani",
            HiredOn = hiredOn,
            LeftOn = leftOn,
            BasicWage = basicWage,
            Allowances = allowances,
        };

    [Fact]
    public void Twenty_one_days_a_year_to_begin_with()
    {
        LeaveAccrual.EntitlementPerYear(0m).ShouldBe(21m);
        LeaveAccrual.EntitlementPerYear(4.99m).ShouldBe(21m);
    }

    [Fact]
    public void Thirty_days_a_year_after_five()
    {
        LeaveAccrual.EntitlementPerYear(5m).ShouldBe(30m);
        LeaveAccrual.EntitlementPerYear(12m).ShouldBe(30m);
    }

    [Fact]
    public void A_full_first_year_earns_about_twenty_one_days()
    {
        // To within a day, because a year is 365 days and the rate is per 365.25.
        var employee = Employee(new DateOnly(2025, 1, 1));

        LeaveAccrual
            .EarnedBetween(employee, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))
            .ShouldBe(21m, 0.5m);
    }

    [Fact]
    public void Joining_in_November_does_not_earn_a_whole_year()
    {
        // A system that granted the year's allowance on hiring would let a new starter take
        // three weeks and leave. Two months is about three and a half days.
        var employee = Employee(new DateOnly(2025, 11, 1));

        LeaveAccrual
            .EarnedBetween(employee, new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 31))
            .ShouldBe(3.5m, 0.5m);
    }

    [Fact]
    public void Nothing_accrues_before_somebody_starts()
    {
        var employee = Employee(new DateOnly(2026, 6, 1));

        LeaveAccrual
            .EarnedBetween(employee, new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 31))
            .ShouldBe(0m);
    }

    [Fact]
    public void Nothing_accrues_after_somebody_leaves()
    {
        var employee = Employee(new DateOnly(2020, 1, 1), new DateOnly(2026, 3, 31));

        var toLeaving = LeaveAccrual
            .EarnedBetween(employee, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        var wellAfter = LeaveAccrual
            .EarnedBetween(employee, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        wellAfter.ShouldBe(toLeaving, "the extra months were not worked");
    }

    [Fact]
    public void The_rate_changes_partway_through_the_year_it_changes_in()
    {
        // Somebody who passes five years in July earns 21 days a year until then and 30 after.
        // Multiplying the whole year by the rate they finished on would grant thirty days for a
        // year in which they were entitled to twenty-one for over half of it.
        var employee = Employee(new DateOnly(2021, 7, 1));

        var crossingYear = LeaveAccrual
            .EarnedBetween(employee, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        crossingYear.ShouldBeGreaterThan(21m, "part of the year was at the higher rate");
        crossingYear.ShouldBeLessThan(30m, "and part of it was not");
        crossingYear.ShouldBe(25.5m, 1m);
    }

    [Fact]
    public void A_backwards_period_earns_nothing_rather_than_something_negative()
    {
        var employee = Employee(new DateOnly(2020, 1, 1));

        LeaveAccrual
            .EarnedBetween(employee, new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1))
            .ShouldBe(0m);
    }

    [Fact]
    public void The_balance_is_what_came_in_plus_what_was_earned_less_what_was_taken()
    {
        var balance = LeaveAccrual.Balance(earnedDays: 21m, takenDays: 8m, broughtForwardDays: 5m);

        balance.BalanceDays.ShouldBe(18m);
        balance.ForfeitedDays.ShouldBe(0m);
    }

    [Fact]
    public void A_carry_over_cap_is_reported_rather_than_applied_silently()
    {
        // Somebody is owed an explanation for leave that disappeared, and the number is also what
        // argues for having a cap at all.
        var capped = new LeavePolicy(
            "Ten days may carry",
            LeavePolicy.Saudi.Bands,
            CarryOverLimitDays: 10m);

        var balance = LeaveAccrual.Balance(
            earnedDays: 30m,
            takenDays: 0m,
            broughtForwardDays: 25m,
            policy: capped);

        balance.BroughtForwardDays.ShouldBe(10m);
        balance.ForfeitedDays.ShouldBe(15m, "and the fifteen lost days are said, not dropped");
        balance.BalanceDays.ShouldBe(40m);
    }

    [Fact]
    public void Unused_leave_is_worth_a_thirtieth_of_a_month_a_day()
    {
        // Divided by thirty rather than by the days in the month. Dividing by the calendar makes
        // a day of February worth more than a day of March, and nobody's leave is worth more for
        // being taken in a short month.
        var employee = Employee(new DateOnly(2020, 1, 1));

        LeaveAccrual.Liability(employee, balanceDays: 30m).ShouldBe(12_000m);
        LeaveAccrual.Liability(employee, balanceDays: 1m).ShouldBe(400m);
    }

    [Fact]
    public void Leave_is_not_cumulative_the_way_the_end_of_service_award_is()
    {
        // The trap. Passing five years does not retrospectively upgrade four years of leave that
        // has already been taken -- there is nothing behind to revalue.
        var employee = Employee(new DateOnly(2021, 1, 1));

        var fifthYear = LeaveAccrual
            .EarnedBetween(employee, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

        var sixthYear = LeaveAccrual
            .EarnedBetween(employee, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        fifthYear.ShouldBe(21m, 1m, "still on the lower rate for almost all of it");
        sixthYear.ShouldBe(30m, 1m, "and the higher rate from then on");
    }

    [Fact]
    public void Another_country_is_a_policy_rather_than_a_fork()
    {
        var flat = new LeavePolicy("Twenty-five days flat", [new LeaveBand(null, 25m)]);

        LeaveAccrual.EntitlementPerYear(1m, flat).ShouldBe(25m);
        LeaveAccrual.EntitlementPerYear(20m, flat).ShouldBe(25m);
    }
}
