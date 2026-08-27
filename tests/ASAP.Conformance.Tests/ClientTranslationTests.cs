using System.Text.RegularExpressions;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// Holds the Angular client's two dictionaries to the same set of keys.
/// </summary>
/// <remarks>
/// <para>
/// The client keeps its own strings -- the shell's own words, which the server has no opinion
/// about -- as one object with an <c>en</c> half and an <c>ar</c> half. TypeScript types the
/// lookup key from the English half alone, so a key added to <c>en</c> and forgotten in <c>ar</c>
/// compiles perfectly and silently falls back to English at runtime.
/// </para>
/// <para>
/// Which means the compiler cannot catch the one mistake anybody actually makes here. This can.
/// </para>
/// <para>
/// Parsed as text rather than executed. Standing up a TypeScript runtime inside a .NET test to
/// read a flat list of string literals would be a great deal of machinery for a job a regular
/// expression does exactly.
/// </para>
/// </remarks>
public sealed partial class ClientTranslationTests
{
    private static readonly Lazy<string> Source = new(ReadTranslations);

    [Fact]
    public void Both_languages_define_the_same_keys()
    {
        var english = KeysOf("en");
        var arabic = KeysOf("ar");

        english.ShouldNotBeEmpty("the English dictionary was not found, so this test proves nothing");

        var missingArabic = english.Except(arabic).Order().ToList();
        var orphanedArabic = arabic.Except(english).Order().ToList();

        missingArabic.ShouldBeEmpty(
            $"{missingArabic.Count} key(s) exist in English but not Arabic, and will silently "
            + $"fall back to English:{Environment.NewLine}  "
            + string.Join(Environment.NewLine + "  ", missingArabic));

        // The other direction matters less but still means somebody renamed a key and left the
        // Arabic behind, so the Arabic string is now unreachable.
        orphanedArabic.ShouldBeEmpty(
            $"{orphanedArabic.Count} Arabic key(s) match nothing in English, so nothing can ever "
            + $"show them:{Environment.NewLine}  "
            + string.Join(Environment.NewLine + "  ", orphanedArabic));
    }

    [Fact]
    public void Arabic_values_are_written_in_arabic()
    {
        // Excludes the deliberate exceptions: the language toggle shows the name of the language
        // being switched *to*, so the Arabic dictionary's entry for it is the English word.
        var suspicious = ValuesOf("ar")
            .Where(static pair => pair.Key is not "shell.language")
            .Where(static pair => pair.Value.Any(char.IsLetter))
            .Where(static pair => !pair.Value.Any(IsArabicLetter))
            .Select(static pair => $"{pair.Key} = \"{pair.Value}\"")
            .ToList();

        suspicious.ShouldBeEmpty(
            $"{suspicious.Count} Arabic value(s) contain no Arabic letters:{Environment.NewLine}  "
            + string.Join(Environment.NewLine + "  ", suspicious));
    }

    [Fact]
    public void Placeholders_survive_translation()
    {
        // A string reading "Posted as transaction {No}." in English and dropping {No} in Arabic
        // loses the number itself, not just the wording. The Arabic reader is left with a
        // sentence that says something happened but not to what.
        var english = ValuesOf("en");
        var arabic = ValuesOf("ar");

        var mismatched = new List<string>();

        foreach (var (key, englishText) in english)
        {
            if (!arabic.TryGetValue(key, out var arabicText))
            {
                continue;
            }

            var wanted = Placeholders(englishText);
            var found = Placeholders(arabicText);

            if (!wanted.SetEquals(found))
            {
                mismatched.Add(
                    $"{key}: English has {Describe(wanted)}, Arabic has {Describe(found)}");
            }
        }

        mismatched.ShouldBeEmpty(
            $"{mismatched.Count} translation(s) do not carry the same placeholders:"
            + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", mismatched));
    }

    private static string Describe(HashSet<string> placeholders)
        => placeholders.Count == 0 ? "none" : string.Join(", ", placeholders.Order());

    private static HashSet<string> Placeholders(string text)
        => [.. PlaceholderPattern().Matches(text).Select(static m => m.Groups[1].Value)];

    private static bool IsArabicLetter(char character)
        => character is >= '؀' and <= 'ۿ';

    private static HashSet<string> KeysOf(string language)
        => [.. ValuesOf(language).Keys];

    /// <summary>Reads one language's entries out of the dictionary.</summary>
    private static Dictionary<string, string> ValuesOf(string language)
    {
        var source = Source.Value;
        var start = source.IndexOf($"{language}: {{", StringComparison.Ordinal);

        if (start < 0)
        {
            return [];
        }

        // Up to the closing brace of this language's block, which is the first line that is a
        // brace at exactly the nesting the block opened at.
        var end = source.IndexOf($"{Environment.NewLine}  }},", start, StringComparison.Ordinal);

        if (end < 0)
        {
            end = source.IndexOf("\n  },", start, StringComparison.Ordinal);
        }

        var block = end < 0 ? source[start..] : source[start..end];
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in EntryPattern().Matches(block))
        {
            var key = match.Groups["key"].Value;

            // Values are written across several lines when they are long, joined with +. Taking
            // all the pieces matters: a placeholder often sits in the second half.
            var value = string.Concat(
                FragmentPattern().Matches(match.Groups["value"].Value)
                    .Select(static f => f.Groups[1].Value));

            entries[key] = value;
        }

        return entries;
    }

    private static string ReadTranslations()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ASAP.slnx")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(
            directory?.FullName ?? string.Empty,
            "frontend", "src", "app", "core", "i18n", "translations.ts");

        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    [GeneratedRegex(@"'(?<key>[a-zA-Z0-9._]+)':\s*(?<value>'[^']*'(?:\s*\+\s*\r?\n?\s*'[^']*')*)")]
    private static partial Regex EntryPattern();

    [GeneratedRegex(@"'([^']*)'")]
    private static partial Regex FragmentPattern();

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderPattern();
}
