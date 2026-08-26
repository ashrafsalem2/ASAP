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
}
