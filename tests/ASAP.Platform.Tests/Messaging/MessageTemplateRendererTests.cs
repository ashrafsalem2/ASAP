using System.Globalization;
using ASAP.Platform.Core.Messaging;
using Shouldly;

namespace ASAP.Platform.Tests.Messaging;

public sealed class MessageTemplateRendererTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Substitutes_named_placeholders()
    {
        var rendered = MessageTemplateRenderer.Render(
            "Journal {DocumentNo} is out of balance.",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DocumentNo"] = "GJ-2026-00042",
            },
            English);

        rendered.ShouldBe("Journal GJ-2026-00042 is out of balance.");
    }

    [Fact]
    public void Matches_placeholder_names_without_regard_to_case()
    {
        // Templates are written by developers and translators working from a glossary; insisting
        // they agree on casing would produce broken messages for no benefit.
        var rendered = MessageTemplateRenderer.Render(
            "Short by {difference}.",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Difference"] = 150m },
            English);

        rendered.ShouldBe("Short by 150.");
    }

    [Fact]
    public void Applies_the_format_string_when_one_is_given()
    {
        var rendered = MessageTemplateRenderer.Render(
            "Short by {Difference:N2} {Currency}.",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Difference"] = 150.5m,
                ["Currency"] = "SAR",
            },
            English);

        rendered.ShouldBe("Short by 150.50 SAR.");
    }

    [Fact]
    public void Formats_numbers_in_the_requested_culture()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");

        var rendered = MessageTemplateRenderer.Render(
            "{Amount:N2}",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Amount"] = 1234.5m },
            german);

        rendered.ShouldBe("1.234,50");
    }

    [Fact]
    public void Leaves_a_placeholder_with_no_argument_visible()
    {
        // Deliberate: a message reading "{Difference}" is obviously broken and gets reported,
        // where silently dropping it yields a sentence that reads fine and states nothing.
        var rendered = MessageTemplateRenderer.Render(
            "Short by {Difference}.",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Other"] = 1 },
            English);

        rendered.ShouldBe("Short by {Difference}.");
    }

    [Fact]
    public void Renders_a_null_argument_as_nothing()
    {
        var rendered = MessageTemplateRenderer.Render(
            "Reference: {Reference}.",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Reference"] = null },
            English);

        rendered.ShouldBe("Reference: .");
    }

    [Fact]
    public void Reports_the_placeholders_a_template_uses()
    {
        var names = MessageTemplateRenderer.PlaceholdersIn(
            "Item {ItemNo} costs {Cost:N2} but sells at {Price:N2}.");

        names.ShouldBe(["ItemNo", "Cost", "Price"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("No placeholders here.")]
    public void Returns_a_template_without_placeholders_unchanged(string template)
    {
        MessageTemplateRenderer.Render(template, null, English).ShouldBe(template);
    }

    [Fact]
    public void A_date_reads_the_same_in_every_culture()
    {
        // 8/1/2026 is August in one country and January in another, and a refusal about which
        // days somebody is being paid for cannot leave that to the reader.
        var rendered = MessageTemplateRenderer.Render(
            "Covers {From:d} to {To:d}.",
            new Dictionary<string, object?>
            {
                ["From"] = new DateOnly(2026, 8, 1),
                ["To"] = new DateOnly(2026, 8, 31),
            },
            new CultureInfo("en-US"));

        rendered.ShouldBe("Covers 2026-08-01 to 2026-08-31.");
    }

    [Fact]
    public void An_arabic_culture_does_not_move_the_date_to_another_calendar()
    {
        // The framework renders dates under ar-SA in the Hijri calendar. That is not another
        // spelling of the same day; it is a different number for it, in a message whose entire
        // subject is which days are meant.
        var arguments = new Dictionary<string, object?> { ["On"] = new DateOnly(2026, 8, 1) };

        MessageTemplateRenderer.Render("{On:d}", arguments, new CultureInfo("ar-SA"))
            .ShouldBe(MessageTemplateRenderer.Render("{On:d}", arguments, new CultureInfo("en-GB")));

        MessageTemplateRenderer.Render("{On:d}", arguments, new CultureInfo("ar-SA"))
            .ShouldBe("2026-08-01");
    }

    [Fact]
    public void A_moment_carries_its_time_and_a_number_still_takes_its_format()
    {
        var rendered = MessageTemplateRenderer.Render(
            "{At} — {Amount:N2}",
            new Dictionary<string, object?>
            {
                ["At"] = new DateTime(2026, 8, 1, 14, 5, 0, DateTimeKind.Utc),
                ["Amount"] = 582.8m,
            },
            CultureInfo.InvariantCulture);

        rendered.ShouldBe("2026-08-01 14:05 — 582.80");
    }

    [Fact]
    public void A_width_pads_the_value_the_way_composite_formatting_does()
    {
        // A receipt is columns of characters. Without padding the only way to line a column up is
        // to hope every value is the same length as the one it was designed against.
        var arguments = new Dictionary<string, object?>
        {
            ["Amount"] = 48m,
            ["Description"] = "Desk lamp",
        };

        MessageTemplateRenderer
            .Render("[{Amount,10:N2}]", arguments, CultureInfo.InvariantCulture)
            .ShouldBe("[     48.00]");

        MessageTemplateRenderer
            .Render("[{Description,-15}]", arguments, CultureInfo.InvariantCulture)
            .ShouldBe("[Desk lamp      ]");
    }

    [Fact]
    public void A_value_wider_than_its_field_is_not_cut_short()
    {
        // Truncating hides a figure, and a receipt missing a digit off a total is worse than one
        // out of line.
        var arguments = new Dictionary<string, object?> { ["Amount"] = 1_234_567.89m };

        MessageTemplateRenderer
            .Render("{Amount,4:N2}", arguments, CultureInfo.InvariantCulture)
            .ShouldBe("1,234,567.89");
    }

    [Fact]
    public void A_width_without_a_format_still_pads()
    {
        var arguments = new Dictionary<string, object?> { ["Code"] = "AB" };

        MessageTemplateRenderer
            .Render("[{Code,5}]", arguments, CultureInfo.InvariantCulture)
            .ShouldBe("[   AB]");
    }
}
