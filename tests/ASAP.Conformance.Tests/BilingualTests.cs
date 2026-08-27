using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// ASAP ships in Arabic and English, and this is what holds it to that.
/// </summary>
/// <remarks>
/// <para>
/// Bilingual support decays quietly. Nobody ever decides to drop it; somebody adds one message in
/// a hurry, the English falls through as the Arabic fallback, and the gap is invisible until a
/// user working in Arabic hits exactly that message -- which, since most of these are refusals,
/// is on the worst day they were going to have anyway.
/// </para>
/// <para>
/// So every string a user can see is checked here rather than trusted: messages, permissions,
/// settings and menu entries. The failure names the exact declaration, because "something is
/// missing Arabic" is not an actionable test failure.
/// </para>
/// </remarks>
public sealed class BilingualTests
{
    /// <summary>Every module ASAP ships, which is what these rules have to hold across.</summary>
    private static readonly IAsapModule[] Modules =
    [
        new PlatformModule(),
        new ASAP.Modules.Finance.FinanceModule(),
        new ASAP.Modules.Inventory.InventoryModule(),
        new ASAP.Modules.Purchasing.PurchasingModule(),
        new ASAP.Modules.Promotions.PromotionsModule(),
        new ASAP.Modules.Sales.SalesModule(),
        new ASAP.Modules.Pos.PosModule(),
    ];

    /// <summary>Collects the English text of anything missing its Arabic.</summary>
    private static List<string> Missing(IEnumerable<(string Where, LocalizedText? Text)> candidates)
        =>
        [
            .. candidates
                .Where(static c => c.Text is { } text && string.IsNullOrWhiteSpace(text.Arabic))
                .Select(static c => $"{c.Where}: \"{Trim(c.Text!.Value.English)}\""),
        ];

    private static string Trim(string english)
        => english.Length <= 60 ? english : english[..57] + "...";

    [Fact]
    public void Every_message_speaks_both_languages()
    {
        // Title, detail and resolution alike. A message whose title translates but whose
        // resolution does not is the worse case of the two: the user is told in Arabic that
        // something went wrong, then told in English what to do about it.
        var missing = Missing(Modules
            .SelectMany(static m => m.Messages)
            .SelectMany(static d => new (string, LocalizedText?)[]
            {
                ($"{d.Code} title", d.Title),
                ($"{d.Code} detail", d.Detail),
                ($"{d.Code} resolution", d.Resolution),
            }));

        missing.ShouldBeEmpty(Report("messages", missing));
    }

    [Fact]
    public void Every_permission_speaks_both_languages()
    {
        // Shown on the permission set screen, which is where an administrator decides what a
        // colleague may do. Guessing at that from an untranslated string is how people end up
        // granting more than they meant to.
        var missing = Missing(Modules
            .SelectMany(static m => m.Permissions)
            .SelectMany(static p => new (string, LocalizedText?)[]
            {
                ($"{p.Key} name", p.DisplayName),
                ($"{p.Key} description", p.Description),
            }));

        missing.ShouldBeEmpty(Report("permissions", missing));
    }

    [Fact]
    public void Every_setting_speaks_both_languages()
    {
        var missing = Missing(Modules
            .SelectMany(static m => m.Setups)
            .SelectMany(static s => new (string, LocalizedText?)[]
                {
                    ($"{s.Key} group", s.Group),
                    ($"{s.Key} name", s.DisplayName),
                    ($"{s.Key} description", s.Description),
                }
                .Concat(s.AllowedValues?.SelectMany(o => new (string, LocalizedText?)[]
                {
                    ($"{s.Key} option {o.Value} label", o.Label),
                    ($"{s.Key} option {o.Value} description", o.Description),
                }) ?? [])));

        missing.ShouldBeEmpty(Report("settings", missing));
    }

    [Fact]
    public void Every_menu_entry_speaks_both_languages()
    {
        // The menu is the first thing anybody sees, and a single English entry in an Arabic menu
        // is the most visible possible way to look unfinished.
        var missing = Missing(Modules
            .SelectMany(static m => m.Navigation)
            .Select(static n => ((string, LocalizedText?))($"{n.Id}", n.DisplayName)));

        missing.ShouldBeEmpty(Report("menu entries", missing));
    }

    [Fact]
    public void Arabic_is_not_just_the_english_copied_across()
    {
        // Catches the other way this decays: pasting the English into the Arabic slot to satisfy
        // a check like the ones above. Codes, numbers and account references are legitimately
        // identical, so only text with letters in it is judged.
        var suspicious = Modules
            .SelectMany(static m => m.Messages)
            .SelectMany(static d => new (string Where, LocalizedText? Text)[]
            {
                ($"{d.Code} title", d.Title),
                ($"{d.Code} detail", d.Detail),
                ($"{d.Code} resolution", d.Resolution),
            })
            .Where(static c => c.Text is { } t
                               && t.Arabic is not null
                               && t.Arabic == t.English
                               && t.English.Any(char.IsLetter))
            .Select(static c => c.Where)
            .ToList();

        suspicious.ShouldBeEmpty(Report("messages whose Arabic is a copy of the English", suspicious));
    }

    [Fact]
    public void Arabic_text_is_actually_written_in_arabic()
    {
        // The cheapest possible check that somebody filled the slot with a translation rather
        // than with anything at all: Arabic text should contain Arabic letters.
        var notArabic = Modules
            .SelectMany(static m => m.Messages)
            .SelectMany(static d => new (string Where, LocalizedText? Text)[]
            {
                ($"{d.Code} title", d.Title),
                ($"{d.Code} detail", d.Detail),
                ($"{d.Code} resolution", d.Resolution),
            })
            .Where(static c => c.Text is { Arabic: { } arabic }
                               && !string.IsNullOrWhiteSpace(arabic)
                               && !arabic.Any(IsArabicLetter))
            .Select(static c => c.Where)
            .ToList();

        notArabic.ShouldBeEmpty(Report("Arabic text with no Arabic letters in it", notArabic));
    }

    private static bool IsArabicLetter(char character)
        => character is >= '؀' and <= 'ۿ';

    private static string Report(string what, IReadOnlyList<string> missing)
        => missing.Count == 0
            ? string.Empty
            : $"{missing.Count} {what} are missing their Arabic:{Environment.NewLine}  "
              + string.Join(Environment.NewLine + "  ", missing);
}
