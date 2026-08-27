using ASAP.Modules.Hr.Payroll;
using ASAP.Modules.Hr.People;
using Shouldly;

namespace ASAP.Modules.Hr.Tests;

/// <summary>
/// Covers how a month's wage is split between the branches somebody worked at.
/// </summary>
/// <remarks>
/// The reason the branch history exists. Somebody who transfers on the sixteenth costs each
/// branch half the month, and a system holding only a current branch charges the whole month
/// wherever they were on payday — so the branch they left looks cheaper than it was, every time
/// anybody moves, and nobody notices because the total is right.
/// </remarks>
public sealed class WageApportionmentTests
{
    private static readonly Guid Jeddah = Guid.Parse("dddddddd-0000-0000-0000-00000000001a");
    private static readonly Guid Riyadh = Guid.Parse("dddddddd-0000-0000-0000-00000000002a");
    private static readonly Guid Dammam = Guid.Parse("dddddddd-0000-0000-0000-00000000003a");

    private static readonly DateOnly First = new(2026, 6, 1);
    private static readonly DateOnly Last = new(2026, 6, 30);

    private static Employee Employee(
        DateOnly? hiredOn = null,
        DateOnly? leftOn = null,
        params (Guid Branch, DateOnly From, DateOnly? To)[] assignments)
    {
        var employee = new Employee
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            No = "EMP-0003",
            Name = "Faisal Al Otaibi",
            HiredOn = hiredOn ?? new DateOnly(2020, 1, 1),
            LeftOn = leftOn,
            BasicWage = 9_000m,
            Allowances = 3_000m,
        };

        foreach (var (branch, from, to) in assignments)
        {
            employee.BranchAssignments.Add(new BranchAssignment
            {
                TenantId = Guid.Empty,
                CompanyId = Guid.Empty,
                BranchId = branch,
                FromDate = from,
                ToDate = to,
            });
        }

        return employee;
    }

    [Fact]
    public void One_branch_all_month_carries_the_whole_wage()
    {
        var employee = Employee(assignments: (Jeddah, new DateOnly(2020, 1, 1), null));

        var shares = WageApportionment.Split(employee, First, Last, 12_000m);

        shares.Single().BranchId.ShouldBe(Jeddah);
        shares.Single().Days.ShouldBe(30);
        shares.Single().Amount.ShouldBe(12_000m);
    }

    [Fact]
    public void A_transfer_halfway_splits_the_month_between_them()
    {
        // Fifteen days each, so six thousand each. This is the case a current-branch column gets
        // wrong, and gets wrong invisibly: the total is right and the attribution is not.
        var employee = Employee(
            assignments:
            [
                (Jeddah, new DateOnly(2020, 1, 1), new DateOnly(2026, 6, 15)),
                (Riyadh, new DateOnly(2026, 6, 16), null),
            ]);

        var shares = WageApportionment.Split(employee, First, Last, 12_000m);

        shares.Count.ShouldBe(2);
        shares.Single(s => s.BranchId == Jeddah).Days.ShouldBe(15);
        shares.Single(s => s.BranchId == Riyadh).Days.ShouldBe(15);
        shares.Sum(static s => s.Amount).ShouldBe(12_000m);
    }

    [Fact]
    public void The_shares_always_sum_to_exactly_the_wage()
    {
        // Three branches and a thousand gives 333.33 three times, which is 999.99. The missing
        // halala has to land somewhere: a payroll journal that does not balance is not a rounding
        // problem, it is a journal that will not post.
        var employee = Employee(
            assignments:
            [
                (Jeddah, new DateOnly(2020, 1, 1), new DateOnly(2026, 6, 10)),
                (Riyadh, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 20)),
                (Dammam, new DateOnly(2026, 6, 21), null),
            ]);

        var shares = WageApportionment.Split(employee, First, Last, 1_000m);

        shares.Count.ShouldBe(3);
        shares.Sum(static s => s.Amount).ShouldBe(1_000m);
    }

    [Fact]
    public void The_rounding_remainder_lands_on_the_branch_carrying_most_of_the_wage()
    {
        // Not on the smallest. A branch somebody visited for two days should not carry the
        // remainder of everybody's rounding.
        var employee = Employee(
            assignments:
            [
                (Jeddah, new DateOnly(2020, 1, 1), new DateOnly(2026, 6, 28)),
                (Riyadh, new DateOnly(2026, 6, 29), null),
            ]);

        var shares = WageApportionment.Split(employee, First, Last, 1_000m);

        shares[0].BranchId.ShouldBe(Jeddah, "largest first");
        shares[0].Days.ShouldBe(28);
        shares.Sum(static s => s.Amount).ShouldBe(1_000m);
    }

    [Fact]
    public void Days_before_hiring_do_not_count()
    {
        // Somebody starting on the fifteenth costs half a month, not a whole one.
        var employee = Employee(
            hiredOn: new DateOnly(2026, 6, 15),
            assignments: (Jeddah, new DateOnly(2026, 6, 15), null));

        WageApportionment.DaysByBranch(employee, First, Last)[Jeddah].ShouldBe(16);
    }

    [Fact]
    public void Days_after_leaving_do_not_count()
    {
        var employee = Employee(
            leftOn: new DateOnly(2026, 6, 10),
            assignments: (Jeddah, new DateOnly(2020, 1, 1), null));

        WageApportionment.DaysByBranch(employee, First, Last)[Jeddah].ShouldBe(10);
    }

    [Fact]
    public void Somebody_with_no_branch_at_all_produces_no_shares()
    {
        // Rather than a share against an empty branch, which would post to nowhere.
        var employee = Employee();

        WageApportionment.Split(employee, First, Last, 12_000m).ShouldBeEmpty();
    }

    [Fact]
    public void A_gap_in_the_history_is_countable_rather_than_silently_absorbed()
    {
        // The arithmetic above drops a day with no branch, and the cost would then be spread
        // across the branches that do have days -- quietly charging them for a day they had
        // nothing to do with. Asked separately so somebody can be told.
        var employee = Employee(
            assignments:
            [
                (Jeddah, new DateOnly(2020, 1, 1), new DateOnly(2026, 6, 10)),
                (Riyadh, new DateOnly(2026, 6, 21), null),
            ]);

        WageApportionment.UnassignedDays(employee, First, Last).ShouldBe(10);

        var shares = WageApportionment.Split(employee, First, Last, 12_000m);
        shares.Sum(static s => s.Days).ShouldBe(20, "the ten unassigned days are not in the split");
    }

    [Fact]
    public void A_part_month_is_thirtieths_rather_than_calendar_days()
    {
        // Dividing by the calendar makes a day of February worth more than a day of March, and
        // nobody's work is worth more for being done in a short month.
        WageApportionment.ForPartMonth(12_000m, daysWorked: 15, daysInPeriod: 30).ShouldBe(6_000m);
        WageApportionment.ForPartMonth(12_000m, daysWorked: 15, daysInPeriod: 28).ShouldBe(6_000m);
    }

    [Fact]
    public void A_full_period_is_the_whole_wage_however_many_days_it_had()
    {
        // Thirty-one days at a thirtieth each would pay 103% of a month, every long month.
        WageApportionment.ForPartMonth(12_000m, daysWorked: 31, daysInPeriod: 31).ShouldBe(12_000m);
        WageApportionment.ForPartMonth(12_000m, daysWorked: 28, daysInPeriod: 28).ShouldBe(12_000m);
    }

    [Fact]
    public void Nothing_worked_is_nothing_owed()
    {
        WageApportionment.ForPartMonth(12_000m, daysWorked: 0, daysInPeriod: 30).ShouldBe(0m);
    }

    [Fact]
    public void Anything_else_the_month_cost_divides_the_same_way_as_the_wage()
    {
        // An end-of-service charge is earned by working the days, so it belongs where the days
        // were worked. Splitting it by a different rule from the wage would make the two
        // disagree about where somebody was.
        var wage = WageApportionment.Reapportion([(Jeddah, 15), (Riyadh, 16)], 12_000m);
        var charge = WageApportionment.Reapportion([(Jeddah, 15), (Riyadh, 16)], 509.24m);

        wage.Select(static s => s.BranchId).ShouldBe(charge.Select(static s => s.BranchId));
        charge.Sum(static s => s.Amount).ShouldBe(509.24m);
        charge.Single(s => s.BranchId == Riyadh).Amount.ShouldBe(262.83m);
        charge.Single(s => s.BranchId == Jeddah).Amount.ShouldBe(246.41m);
    }

    [Fact]
    public void A_deduction_divides_without_leaving_a_halala_behind()
    {
        // Three branches and an amount that does not divide by three. The parts must still be
        // the whole: a hundredth left over becomes a ledger that does not balance.
        var shares = WageApportionment.Reapportion([(Jeddah, 10), (Riyadh, 10), (Dammam, 10)], 100m);

        shares.Count.ShouldBe(3);
        shares.Sum(static s => s.Amount).ShouldBe(100m);
    }

    [Fact]
    public void A_negative_amount_divides_like_a_positive_one()
    {
        // Deductions are passed through here as a positive and negated at the posting line, but
        // nothing stops a caller handing this a negative, and it should not round the wrong way.
        var shares = WageApportionment.Reapportion([(Jeddah, 15), (Riyadh, 16)], -509.24m);

        shares.Sum(static s => s.Amount).ShouldBe(-509.24m);
        shares.Single(s => s.BranchId == Riyadh).Amount.ShouldBe(-262.83m);
    }

    [Fact]
    public void A_branch_with_no_days_carries_none_of_it()
    {
        // A branch somebody was assigned to and transferred out of on the same day is not a
        // branch that had them, and a zero-day share would post a zero line to the ledger.
        var shares = WageApportionment.Reapportion([(Jeddah, 31), (Riyadh, 0)], 12_000m);

        shares.Count.ShouldBe(1);
        shares.Single().BranchId.ShouldBe(Jeddah);
        shares.Single().Amount.ShouldBe(12_000m);
    }

    [Fact]
    public void Nowhere_to_charge_it_charges_it_nowhere()
    {
        WageApportionment.Reapportion([], 12_000m).ShouldBeEmpty();
        WageApportionment.Reapportion([(Jeddah, 0)], 12_000m).ShouldBeEmpty();
    }
}
