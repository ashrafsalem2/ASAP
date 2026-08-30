using ASAP.Modules.Finance.Journals;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Journals;

/// <summary>
/// Covers the step through the calendar a recurring line takes each time it posts.
/// </summary>
/// <remarks>
/// The month-end cases are the reason this exists at all. A recurrence written as a number of days
/// is wrong in a way that takes months to notice — thirty days on from 31 January is 2 March — and
/// an accrual that lands on the second of the month is one somebody corrects twelve times a year.
/// </remarks>
public sealed class DateFormulaTests
{
    [Theory]
    [InlineData("1D", "2026-03-15", "2026-03-16")]
    [InlineData("1W", "2026-03-15", "2026-03-22")]
    [InlineData("1M", "2026-03-15", "2026-04-15")]
    [InlineData("3M", "2026-03-15", "2026-06-15")]
    [InlineData("1Q", "2026-03-15", "2026-06-15")]
    [InlineData("1Y", "2026-03-15", "2027-03-15")]
    public void A_step_moves_the_date_by_its_unit(string expression, string from, string expected)
    {
        DateFormula.TryParse(expression, out var formula).ShouldBeTrue();

        formula.From(DateOnly.Parse(from)).ShouldBe(DateOnly.Parse(expected));
    }

    [Fact]
    public void The_end_of_a_month_is_the_end_of_whatever_month_it_lands_in()
    {
        DateFormula.TryParse("1M+CM", out var monthEnd).ShouldBeTrue();

        // The recurrence almost every accrual actually wants. Each of these is the last day of the
        // next month, and none of them needs anybody to know how long that month is.
        monthEnd.From(new DateOnly(2026, 1, 31)).ShouldBe(new DateOnly(2026, 2, 28));
        monthEnd.From(new DateOnly(2026, 2, 28)).ShouldBe(new DateOnly(2026, 3, 31));
        monthEnd.From(new DateOnly(2026, 3, 31)).ShouldBe(new DateOnly(2026, 4, 30));

        // And a leap year, which is the case a hand-maintained list of dates gets wrong.
        monthEnd.From(new DateOnly(2028, 1, 31)).ShouldBe(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void A_month_step_alone_clamps_rather_than_spilling_into_the_next_month()
    {
        DateFormula.TryParse("1M", out var month).ShouldBeTrue();

        // 31 January plus a month is 28 February, not 3 March. Worth stating because the naive
        // implementation — thirty days — gives the other answer.
        month.From(new DateOnly(2026, 1, 31)).ShouldBe(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void Terms_add_up_left_to_right()
    {
        DateFormula.TryParse("1M+15D", out var formula).ShouldBeTrue();

        formula.From(new DateOnly(2026, 3, 1)).ShouldBe(new DateOnly(2026, 4, 16));
    }

    [Fact]
    public void A_step_may_go_backwards()
    {
        DateFormula.TryParse("-1M", out var formula).ShouldBeTrue();

        formula.From(new DateOnly(2026, 3, 15)).ShouldBe(new DateOnly(2026, 2, 15));
    }

    [Theory]
    [InlineData("CQ", "2026-02-10", "2026-03-31")]
    [InlineData("CQ", "2026-08-01", "2026-09-30")]
    [InlineData("CY", "2026-02-10", "2026-12-31")]
    public void The_end_of_a_quarter_or_year_is_the_end_of_the_one_it_is_in(
        string expression,
        string from,
        string expected)
    {
        DateFormula.TryParse(expression, out var formula).ShouldBeTrue();

        formula.From(DateOnly.Parse(from)).ShouldBe(DateOnly.Parse(expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("monthly")]
    [InlineData("1")]
    [InlineData("1X")]
    [InlineData("1M+")]
    public void Anything_that_is_not_a_formula_is_refused_whole(string? expression)
    {
        // All or nothing. A formula half understood would advance a recurring line by some amount
        // nobody intended, and it would do it every month until somebody noticed.
        DateFormula.TryParse(expression, out _).ShouldBeFalse();
    }
}
