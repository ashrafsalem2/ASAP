using System.Text.RegularExpressions;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// Covers how numbers read in the messages people are shown.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a real one. A till that came up short told the cashier "580.00 was
/// counted and 582.8000 was expected, a difference of -2.8000" — the column's storage scale,
/// leaked verbatim into a sentence somebody has to act on while a queue waits. The values were
/// right; the message was not something a person should have to read.
/// </para>
/// <para>
/// A test cannot tell from a template alone whether <c>{Amount}</c> holds money or a word. What
/// it can tell is whether the codebase is consistent with itself: if Finance writes
/// <c>{Amount:N2}</c> and a new module writes <c>{Amount}</c>, one of them is wrong, and the one
/// that is wrong is nearly always the one that forgot. That is the rule enforced here.
/// </para>
/// </remarks>
public sealed partial class MessageFormattingTests
{
    private static readonly IReadOnlyList<IAsapModule> Modules =
    [
        new PlatformModule(),
        new ASAP.Modules.Finance.FinanceModule(),
        new ASAP.Modules.Inventory.InventoryModule(),
        new ASAP.Modules.Purchasing.PurchasingModule(),
        new ASAP.Modules.Promotions.PromotionsModule(),
        new ASAP.Modules.Sales.SalesModule(),
        new ASAP.Modules.Pos.PosModule(),
    ];

    /// <summary>One placeholder as it appears in one message.</summary>
    private sealed record Use(string Module, string Code, string Field, string Name, string? Format);

    private static List<Use> AllUses()
    {
        var uses = new List<Use>();

        foreach (var module in Modules)
        {
            foreach (var message in module.Messages)
            {
                Collect(message.Title, "Title");
                Collect(message.Detail, "Detail");
                Collect(message.Resolution, "Resolution");

                void Collect(LocalizedText? text, string field)
                {
                    if (text is null)
                    {
                        return;
                    }

                    foreach (var template in new[] { text.Value.English, text.Value.Arabic })
                    {
                        if (string.IsNullOrEmpty(template))
                        {
                            continue;
                        }

                        foreach (Match match in PlaceholderPattern().Matches(template))
                        {
                            uses.Add(new Use(
                                module.ModuleId,
                                message.Code.Value,
                                field,
                                match.Groups["name"].Value,
                                match.Groups["format"].Success ? match.Groups["format"].Value : null));
                        }
                    }
                }
            }
        }

        return uses;
    }

    [Fact]
    public void A_placeholder_is_formatted_the_same_way_wherever_it_appears()
    {
        var byName = AllUses()
            .GroupBy(static u => u.Name, StringComparer.Ordinal)
            .Where(static g => g.Select(static u => u.Format).Distinct().Count() > 1)
            .ToList();

        var complaints = byName.Select(group =>
        {
            var variants = group
                .GroupBy(static u => u.Format ?? "(none)", StringComparer.Ordinal)
                .Select(g => $"{g.Key} in {string.Join(", ", g.Select(u => $"{u.Module}/{u.Code}").Distinct())}");

            return $"{{{group.Key}}} is written {string.Join(" but ", variants)}";
        }).ToList();

        complaints.ShouldBeEmpty(
            "the same placeholder formatted two ways means one of them is wrong, and it is "
            + "nearly always the one that forgot:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, complaints));
    }

    [Fact]
    public void The_two_languages_of_one_message_format_a_placeholder_the_same_way()
    {
        // An Arabic sentence that prints 582.8000 where the English prints 582.80 is a message
        // that is only half fixed, and only half the users notice.
        var mismatched = new List<string>();

        foreach (var module in Modules)
        {
            foreach (var message in module.Messages)
            {
                Check(message.Title, "Title");
                Check(message.Detail, "Detail");
                Check(message.Resolution, "Resolution");

                void Check(LocalizedText? text, string field)
                {
                    if (text is null || string.IsNullOrEmpty(text.Value.Arabic))
                    {
                        return;
                    }

                    var english = FormatsIn(text.Value.English);
                    var arabic = FormatsIn(text.Value.Arabic!);

                    foreach (var (name, format) in english)
                    {
                        if (arabic.TryGetValue(name, out var other) && other != format)
                        {
                            mismatched.Add(
                                $"{module.ModuleId}/{message.Code.Value} {field}: {{{name}}} is "
                                + $"'{format ?? "(none)"}' in English and '{other ?? "(none)"}' in Arabic");
                        }
                    }
                }
            }
        }

        mismatched.ShouldBeEmpty(string.Join(Environment.NewLine, mismatched));
    }

    [Fact]
    public void The_rule_is_capable_of_failing()
    {
        // Proving the shape of the check rather than trusting it. Two uses of one name with
        // different formats have to be reported, or the two tests above pass for free.
        var uses = new List<Use>
        {
            new("Alpha", "A.ONE", "Detail", "Amount", "N2"),
            new("Beta", "B.TWO", "Detail", "Amount", null),
        };

        uses.GroupBy(static u => u.Name, StringComparer.Ordinal)
            .Count(static g => g.Select(static u => u.Format).Distinct().Count() > 1)
            .ShouldBe(1);
    }

    private static Dictionary<string, string?> FormatsIn(string template)
    {
        var formats = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (Match match in PlaceholderPattern().Matches(template))
        {
            formats[match.Groups["name"].Value] =
                match.Groups["format"].Success ? match.Groups["format"].Value : null;
        }

        return formats;
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::(?<format>[^}]+))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
