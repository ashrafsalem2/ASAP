using ASAP.Modules.Finance.Reporting;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Reporting;

/// <summary>
/// Covers the two pure pieces a user-defined statement is built out of: which accounts a range
/// names, and what a formula comes to.
/// </summary>
/// <remarks>
/// Both are written by a person into a text box, so both have to survive whatever gets typed
/// there. Neither may throw: the place to complain about an expression is the screen that shows
/// what it selected, not a stack trace halfway through drawing a balance sheet.
/// </remarks>
public sealed class AccountScheduleTests
{
    private static readonly Dictionary<string, decimal> Chart = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1100"] = 500m,
        ["1300"] = 1_200m,
        ["4100"] = -8_000m,
        ["4200"] = 300m,
        ["4900"] = -150m,
        ["6100"] = 4_000m,
        ["6600"] = 250m,
    };

    [Fact]
    public void A_range_takes_everything_between_its_ends()
    {
        var range = AccountRange.Parse("4000..4999");

        range.Contains("4100").ShouldBeTrue();
        range.Contains("4999").ShouldBeTrue("the end is included, as everybody writing one assumes");
        range.Contains("5000").ShouldBeFalse();
        range.Sum(Chart).ShouldBe(-7_850m);
    }

    [Fact]
    public void Several_terms_may_be_listed()
    {
        AccountRange.Parse("1100|6600").Sum(Chart).ShouldBe(750m);
        AccountRange.Parse("1100, 6600").Sum(Chart).ShouldBe(750m, "a comma reads the same as a pipe");
    }

    [Fact]
    public void An_account_named_twice_is_counted_once()
    {
        // A statement that double-counted whatever somebody happened to list twice would be wrong
        // in a way no reader could see.
        AccountRange.Parse("4000..4999|4100").Sum(Chart).ShouldBe(-7_850m);
    }

    [Fact]
    public void An_open_end_means_everything_from_there_on()
    {
        var range = AccountRange.Parse("6000..");

        range.Contains("6100").ShouldBeTrue();
        range.Contains("9999").ShouldBeTrue();
        range.Contains("5999").ShouldBeFalse();
    }

    [Fact]
    public void Account_numbers_are_compared_as_text_so_a_suffix_survives()
    {
        // A chart is free to use 1100-A, and a range treated numerically would either drop it or
        // decide it falls outside 1000..1999, which every reader can see it does not.
        var range = AccountRange.Parse("1000..1999");

        range.Contains("1100-A").ShouldBeTrue();
    }

    [Fact]
    public void An_unreadable_expression_selects_nothing_rather_than_throwing()
    {
        AccountRange.Parse(null).IsEmpty.ShouldBeTrue();
        AccountRange.Parse("   ").IsEmpty.ShouldBeTrue();
        AccountRange.Parse("||").IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_formula_adds_and_subtracts_the_rows_it_names()
    {
        var rows = Rows(("R10", 1_000m), ("R20", 400m));

        ScheduleFormula.Evaluate("R10 - R20", rows).ShouldBe(600m);
        ScheduleFormula.Evaluate("R10 + R20", rows).ShouldBe(1_400m);
        ScheduleFormula.Evaluate("-R20", rows).ShouldBe(-400m);
    }

    [Fact]
    public void Multiplication_binds_tighter_than_addition()
    {
        var rows = Rows(("R10", 100m), ("R20", 10m));

        // Left to right would give 55. Everybody who writes this means 105, and quietly producing
        // the other number is the worst thing this file could do.
        ScheduleFormula.Evaluate("R10 + R20 / 2", rows).ShouldBe(105m);
        ScheduleFormula.Evaluate("(R10 + R20) / 2", rows).ShouldBe(55m);
    }

    [Fact]
    public void A_margin_on_no_revenue_has_no_answer_rather_than_being_nought()
    {
        var rows = Rows(("R10", 0m), ("R30", 0m));

        // Nought per cent is a claim about a month that had no sales, and it is not true.
        ScheduleFormula.Evaluate("R30 / R10 * 100", rows).ShouldBeNull();
    }

    [Fact]
    public void A_row_that_has_no_answer_makes_anything_built_on_it_have_none_either()
    {
        var rows = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
        {
            ["R10"] = 500m,
            ["R20"] = null,
        };

        // R20 is a heading, or a margin that could not be worked out. Adding it as though it were
        // nought would print a total that looks complete and is not.
        ScheduleFormula.Evaluate("R10 + R20", rows).ShouldBeNull();
    }

    [Fact]
    public void A_row_nobody_defined_counts_as_nothing()
    {
        // Reported by the schedule itself, which is a better place to say it than in the middle
        // of a figure.
        ScheduleFormula.Evaluate("R10 + R999", Rows(("R10", 500m))).ShouldBe(500m);
    }

    [Fact]
    public void An_expression_that_does_not_parse_has_no_answer()
    {
        var rows = Rows(("R10", 500m));

        ScheduleFormula.Evaluate("R10 +", rows).ShouldBeNull();
        ScheduleFormula.Evaluate("(R10", rows).ShouldBeNull();
        ScheduleFormula.Evaluate("R10 R20", rows).ShouldBeNull();
    }

    [Fact]
    public void The_rows_a_formula_names_are_readable_before_it_is_run()
    {
        // Which is what lets the schedule work out the order to resolve rows in, and report a
        // circle as a circle rather than running out of stack.
        var references = ScheduleFormula.ReferencesIn("R30 / R10 * 100 - R30");

        references.Count.ShouldBe(2);
        references.ShouldContain("R30");
        references.ShouldContain("R10");
    }

    private static Dictionary<string, decimal?> Rows(params (string Row, decimal Amount)[] rows)
        => rows.ToDictionary(
            static r => r.Row,
            static r => (decimal?)r.Amount,
            StringComparer.OrdinalIgnoreCase);
}
