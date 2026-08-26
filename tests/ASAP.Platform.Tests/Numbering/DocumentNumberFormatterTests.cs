using ASAP.Platform.Core.Numbering;
using Shouldly;

namespace ASAP.Platform.Tests.Numbering;

public sealed class DocumentNumberFormatterTests
{
    [Theory]
    [InlineData("GJ-{YYYY}-00001", "GJ-2026-00001")]
    [InlineData("INV-{YY}{MM}-0001", "INV-2608-0001")]
    [InlineData("POS-{YYYY}{MM}{DD}-001", "POS-20260826-001")]
    [InlineData("PLAIN-00001", "PLAIN-00001")]
    public void Substitutes_date_placeholders(string pattern, string expected)
    {
        DocumentNumberFormatter.ApplyDate(pattern, new DateOnly(2026, 8, 26)).ShouldBe(expected);
    }

    [Fact]
    public void Advances_the_counter_and_keeps_its_width()
    {
        // Zero padding is what makes document numbers sort correctly as text, everywhere they
        // are listed or exported, so the width must survive every increment.
        DocumentNumberFormatter.TryAdvance("GJ-2026-00041", 1, out var next).ShouldBeTrue();
        next.ShouldBe("GJ-2026-00042");
    }

    [Fact]
    public void Advances_across_a_digit_boundary_without_widening()
    {
        DocumentNumberFormatter.TryAdvance("INV-2026-00099", 1, out var next).ShouldBeTrue();
        next.ShouldBe("INV-2026-00100");
    }

    [Fact]
    public void Advances_by_a_step_larger_than_one()
    {
        DocumentNumberFormatter.TryAdvance("CHQ-000100", 10, out var next).ShouldBeTrue();
        next.ShouldBe("CHQ-000110");
    }

    [Fact]
    public void Refuses_to_widen_the_counter_when_the_range_is_exhausted()
    {
        // Issuing GJ-2026-100000 after GJ-2026-99999 would sort ahead of everything already
        // issued. The series is declared exhausted instead, so an administrator widens it
        // deliberately rather than discovering a broken sort months later.
        DocumentNumberFormatter.TryAdvance("GJ-2026-99999", 1, out var next).ShouldBeFalse();
        next.ShouldBeNull();
    }

    [Theory]
    [InlineData("NO-DIGITS-AT-END")]
    [InlineData("")]
    public void Refuses_a_number_with_no_trailing_counter(string number)
    {
        DocumentNumberFormatter.TryAdvance(number, 1, out _).ShouldBeFalse();
    }

    [Fact]
    public void Reads_the_counter_off_a_number()
    {
        DocumentNumberFormatter.TryReadCounter("GJ-2026-00042", out var counter).ShouldBeTrue();
        counter.ShouldBe(42);
    }

    [Fact]
    public void Reads_the_prefix_off_a_number()
    {
        DocumentNumberFormatter.ReadPrefix("GJ-2026-00042").ShouldBe("GJ-2026-");
    }

    [Fact]
    public void Treats_a_number_with_no_counter_as_all_prefix()
    {
        DocumentNumberFormatter.ReadPrefix("MANUAL").ShouldBe("MANUAL");
    }

    [Fact]
    public void Accepts_a_well_formed_pattern()
    {
        DocumentNumberFormatter.ValidatePattern("INV-{YYYY}-00001").ShouldBeNull();
    }

    [Fact]
    public void Rejects_a_pattern_whose_year_would_be_incremented()
    {
        // The trap this check exists for: GJ-{YYYY} leaves the year as the trailing digits, so
        // the first increment would issue GJ-2027 in the middle of 2026.
        DocumentNumberFormatter
            .ValidatePattern("GJ-{YYYY}")
            .ShouldNotBeNull()
            .ShouldContain("must end in digits");
    }

    [Fact]
    public void Rejects_a_pattern_with_no_counter_at_all()
    {
        DocumentNumberFormatter.ValidatePattern("INVOICE").ShouldNotBeNull();
    }

    [Fact]
    public void Rejects_a_placeholder_butted_against_the_counter()
    {
        // INV-{YY}0001 substitutes to INV-260001, whose trailing digits are six wide rather
        // than four -- so the first increment advances the year along with the counter.
        DocumentNumberFormatter
            .ValidatePattern("INV-{YY}0001")
            .ShouldNotBeNull()
            .ShouldContain("runs straight into the counter");
    }

    [Fact]
    public void Accepts_adjacent_placeholders_when_a_separator_precedes_the_counter()
    {
        DocumentNumberFormatter.ValidatePattern("INV-{YY}{MM}-0001").ShouldBeNull();
    }

    [Fact]
    public void Rejects_an_unrecognised_placeholder()
    {
        DocumentNumberFormatter
            .ValidatePattern("INV-{QUARTER}-00001")
            .ShouldNotBeNull()
            .ShouldContain("not a recognised placeholder");
    }

    [Fact]
    public void Rejects_a_pattern_that_starts_already_exhausted()
    {
        DocumentNumberFormatter
            .ValidatePattern("INV-9")
            .ShouldNotBeNull()
            .ShouldContain("Widen the counter");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_pattern(string pattern)
    {
        DocumentNumberFormatter.ValidatePattern(pattern).ShouldNotBeNull();
    }

    [Fact]
    public void Reports_how_many_numbers_a_line_has_left()
    {
        var line = new NumberSeriesLine
        {
            StartingNumber = "INV-2026-00001",
            EndingNumber = "INV-2026-00100",
            LastNumberUsed = "INV-2026-00095",
        };

        line.Remaining().ShouldBe(5);
    }

    [Fact]
    public void Reports_a_full_range_before_any_number_is_issued()
    {
        var line = new NumberSeriesLine
        {
            StartingNumber = "INV-2026-00001",
            EndingNumber = "INV-2026-00100",
        };

        line.Remaining().ShouldBe(100);
    }

    [Fact]
    public void Reports_no_ceiling_when_the_line_has_no_ending_number()
    {
        var line = new NumberSeriesLine { StartingNumber = "INV-2026-00001" };

        line.Remaining().ShouldBeNull();
    }
}
