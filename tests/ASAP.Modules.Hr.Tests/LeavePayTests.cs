using ASAP.Modules.Hr.Leave;
using Shouldly;

namespace ASAP.Modules.Hr.Tests;

/// <summary>
/// Covers what leave is paid at, which is the difference between an absence and a deduction.
/// </summary>
/// <remarks>
/// The sliding scale on sick leave is the part worth testing hardest. Article 117 gives thirty
/// days at full pay, sixty at three quarters and thirty at nothing, counted across the year rather
/// than per illness — and a system that started the count again at each absence would pay a year
/// of intermittent sickness in full.
/// </remarks>
public sealed class LeavePayTests
{
    private static LeaveKindPolicy Sick => LeaveKindPolicy.For(LeaveKind.Sick);

    [Fact]
    public void Annual_leave_is_paid_in_full_however_long_it_runs()
    {
        var pay = LeavePayCalculator.For(LeaveKindPolicy.For(LeaveKind.Annual), days: 30m);

        pay.PaidDays.ShouldBe(30m);
        pay.UnpaidDays.ShouldBe(0m);
    }

    [Fact]
    public void Unpaid_leave_is_paid_at_nothing()
    {
        var pay = LeavePayCalculator.For(LeaveKindPolicy.For(LeaveKind.Unpaid), days: 5m);

        pay.PaidDays.ShouldBe(0m);
        pay.UnpaidDays.ShouldBe(5m);
    }

    [Fact]
    public void The_first_thirty_days_of_sickness_are_paid_in_full()
    {
        var pay = LeavePayCalculator.For(Sick, days: 30m);

        pay.PaidDays.ShouldBe(30m);
        pay.UnpaidDays.ShouldBe(0m);
    }

    [Fact]
    public void The_next_sixty_are_paid_at_three_quarters()
    {
        // Days 31 to 90, taken all at once. Forty-five full-pay equivalents out of sixty.
        var pay = LeavePayCalculator.For(Sick, days: 60m, daysAlreadyTaken: 30m);

        pay.PaidDays.ShouldBe(45m);
        pay.UnpaidDays.ShouldBe(15m);
    }

    [Fact]
    public void The_thirty_after_that_are_paid_at_nothing()
    {
        var pay = LeavePayCalculator.For(Sick, days: 30m, daysAlreadyTaken: 90m);

        pay.PaidDays.ShouldBe(0m);
        pay.UnpaidDays.ShouldBe(30m);
    }

    [Fact]
    public void One_absence_can_cross_two_bands()
    {
        // Ninety days in one go: thirty at full pay and sixty at three quarters, which is
        // seventy-five. Taking the whole absence at the rate it ends on would pay sixty-seven and
        // a half, and at the rate it starts on would pay all ninety.
        var pay = LeavePayCalculator.For(Sick, days: 90m);

        pay.PaidDays.ShouldBe(75m);
        pay.UnpaidDays.ShouldBe(15m);
    }

    [Fact]
    public void The_year_is_counted_across_absences_and_not_within_one()
    {
        // Twenty-five days in March and ten more in September is thirty-five days of sickness.
        // Five of the second absence are still in the first band and five have crossed into the
        // second, which is five plus three and three quarters.
        var pay = LeavePayCalculator.For(Sick, days: 10m, daysAlreadyTaken: 25m);

        pay.PaidDays.ShouldBe(8.75m);
        pay.UnpaidDays.ShouldBe(1.25m);
    }

    [Fact]
    public void A_year_of_intermittent_sickness_costs_the_same_as_one_long_absence()
    {
        // The property the band arithmetic exists for. Ten absences of nine days must come to
        // exactly what one absence of ninety comes to, or somebody is better off being ill in
        // one stretch than in ten.
        var together = LeavePayCalculator.For(Sick, days: 90m);

        var apart = 0m;

        for (var taken = 0m; taken < 90m; taken += 9m)
        {
            apart += LeavePayCalculator.For(Sick, days: 9m, daysAlreadyTaken: taken).PaidDays;
        }

        apart.ShouldBe(together.PaidDays);
    }

    [Fact]
    public void Maternity_runs_ten_weeks_and_then_stops()
    {
        var within = LeavePayCalculator.For(LeaveKindPolicy.For(LeaveKind.Maternity), days: 70m);
        var beyond = LeavePayCalculator.For(LeaveKindPolicy.For(LeaveKind.Maternity), days: 80m);

        within.PaidDays.ShouldBe(70m);
        beyond.PaidDays.ShouldBe(70m);
        beyond.UnpaidDays.ShouldBe(10m);
    }

    [Fact]
    public void A_kind_nobody_wrote_a_policy_for_is_paid_rather_than_docked()
    {
        // An extension adding a kind and forgetting the policy should not quietly stop paying
        // somebody. Wrong in the direction that gets noticed and argued about, not the direction
        // that appears as a smaller number on a payslip.
        var pay = LeavePayCalculator.For(LeaveKindPolicy.For((LeaveKind)99), days: 12m);

        pay.PaidDays.ShouldBe(12m);
        pay.UnpaidDays.ShouldBe(0m);
    }

    [Fact]
    public void No_days_is_no_pay_and_no_deduction()
    {
        var pay = LeavePayCalculator.For(Sick, days: 0m);

        pay.Days.ShouldBe(0m);
        pay.PaidDays.ShouldBe(0m);
        pay.UnpaidDays.ShouldBe(0m);
    }
}
