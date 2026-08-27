using System.Globalization;
using ASAP.Platform.Core.Printing;
using Shouldly;

namespace ASAP.Platform.Tests.Printing;

/// <summary>
/// Covers the template language a shop manager edits a receipt with.
/// </summary>
/// <remarks>
/// Three things: a placeholder, a repeated region, and everything else printed as written. The
/// tests here are mostly about the third — a template language that surprises the person editing
/// it is one they stop editing, and then the receipt says whatever it said on the day it shipped.
/// </remarks>
public sealed class PrintTemplateRendererTests
{
    private static readonly Dictionary<string, object?> Receipt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ReceiptNo"] = "R-2026-000123",
        ["StationCode"] = "JED-01-T1",
        ["Total"] = 108.50m,
        ["TakenAt"] = new DateOnly(2026, 8, 27),
    };

    private static readonly Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Lines =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] =
            [
                Line("Desk lamp", 2m, 24m),
                Line("Bottled water", 1m, 60.50m),
            ],
        };

    private static Dictionary<string, object?> Line(string description, decimal quantity, decimal amount)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Description"] = description,
            ["Quantity"] = quantity,
            ["Amount"] = amount,
        };

    [Fact]
    public void A_placeholder_takes_its_value_from_the_document()
    {
        PrintTemplateRenderer
            .Render("Receipt {ReceiptNo}", Receipt, null, CultureInfo.InvariantCulture)
            .ShouldBe("Receipt R-2026-000123");
    }

    [Fact]
    public void A_region_repeats_once_per_line()
    {
        var rendered = PrintTemplateRenderer.Render(
            "[[lines]]{Quantity} x {Description}\n[[/lines]]",
            Receipt,
            Lines,
            CultureInfo.InvariantCulture);

        rendered.ShouldBe("2 x Desk lamp\n1 x Bottled water\n");
    }

    [Fact]
    public void The_document_is_visible_inside_a_line()
    {
        // So a label template can print the receipt number on every label without the editor
        // having to copy it into each row.
        var rendered = PrintTemplateRenderer.Render(
            "[[lines]]{ReceiptNo} {Description}\n[[/lines]]",
            Receipt,
            Lines,
            CultureInfo.InvariantCulture);

        rendered.ShouldBe("R-2026-000123 Desk lamp\nR-2026-000123 Bottled water\n");
    }

    [Fact]
    public void A_line_wins_where_both_have_the_name()
    {
        var document = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Description"] = "the whole sale",
        };

        var rendered = PrintTemplateRenderer.Render(
            "[[lines]]{Description}[[/lines]]",
            document,
            Lines,
            CultureInfo.InvariantCulture);

        rendered.ShouldBe("Desk lampBottled water");
    }

    [Fact]
    public void A_region_with_no_lines_prints_nothing()
    {
        // Which is what an empty receipt should look like, rather than a template that refuses
        // to render because a shop rang up a sale with nothing in it.
        PrintTemplateRenderer
            .Render("before[[lines]]{Description}[[/lines]]after", Receipt, null, CultureInfo.InvariantCulture)
            .ShouldBe("beforeafter");
    }

    [Fact]
    public void Two_regions_do_not_swallow_what_is_between_them()
    {
        var regions = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = [Line("Desk lamp", 1m, 24m)],
            ["tenders"] = [Line("Cash", 1m, 24m)],
        };

        var rendered = PrintTemplateRenderer.Render(
            "[[lines]]{Description}[[/lines]]MIDDLE[[tenders]]{Description}[[/tenders]]",
            Receipt,
            regions,
            CultureInfo.InvariantCulture);

        rendered.ShouldBe("Desk lampMIDDLECash");
    }

    [Fact]
    public void Money_takes_the_format_the_template_asks_for()
    {
        PrintTemplateRenderer
            .Render("{Total:N2}", Receipt, null, CultureInfo.InvariantCulture)
            .ShouldBe("108.50");
    }

    [Fact]
    public void A_date_is_iso_whatever_the_culture()
    {
        // The same rule as a message. A receipt printed in an Arabic shop must not carry a Hijri
        // date beside a Gregorian one on the invoice for the same sale.
        PrintTemplateRenderer
            .Render("{TakenAt}", Receipt, null, new CultureInfo("ar-SA"))
            .ShouldBe("2026-08-27");
    }

    [Fact]
    public void A_placeholder_nobody_supplied_stays_visible()
    {
        // Left as written rather than blanked, so a receipt with {Totl} on it is obviously broken
        // and gets reported, instead of printing two hundred receipts with a gap where the total
        // should have been.
        PrintTemplateRenderer
            .Render("Total {Totl}", Receipt, null, CultureInfo.InvariantCulture)
            .ShouldBe("Total {Totl}");
    }

    [Fact]
    public void Everything_else_prints_as_written()
    {
        const string template = "*** THANK YOU ***\n  Please keep your receipt\n";

        PrintTemplateRenderer
            .Render(template, Receipt, null, CultureInfo.InvariantCulture)
            .ShouldBe(template);
    }

    [Fact]
    public void The_editor_can_be_told_what_a_template_refers_to()
    {
        var names = PrintTemplateRenderer.PlaceholdersIn(
            "{ReceiptNo}[[lines]]{Description} {Amount:N2}[[/lines]]{Total:N2}");

        names.ShouldContain("ReceiptNo");
        names.ShouldContain("Description");
        names.ShouldContain("Amount");
        names.ShouldContain("Total");

        PrintTemplateRenderer
            .RegionsIn("{ReceiptNo}[[lines]]x[[/lines]][[tenders]]y[[/tenders]]")
            .ShouldBe(["lines", "tenders"]);
    }
}
