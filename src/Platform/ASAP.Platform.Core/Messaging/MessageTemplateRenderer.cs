using System.Globalization;
using System.Text.RegularExpressions;

namespace ASAP.Platform.Core.Messaging;

/// <summary>
/// Substitutes real values into a message template.
/// </summary>
/// <remarks>
/// <para>
/// Templates use named placeholders, optionally with a .NET format string:
/// <c>"Out of balance by {Difference:N2} {Currency}."</c> Names rather than positions, so a
/// translator can reorder them freely — which matters a great deal between English and Arabic,
/// where the natural order of a sentence differs.
/// </para>
/// <para>
/// A placeholder with no matching argument is left in the text exactly as written. That is a
/// deliberate choice: a message showing <c>{Difference}</c> is obviously broken and gets
/// reported, whereas silently dropping it would produce a sentence that reads fine and is wrong.
/// </para>
/// </remarks>
public static partial class MessageTemplateRenderer
{
    /// <summary>
    /// Renders a template.
    /// </summary>
    /// <param name="template">The template text, with <c>{Name}</c> or <c>{Name:format}</c> placeholders.</param>
    /// <param name="arguments">Values to substitute, matched by name without regard to case.</param>
    /// <param name="culture">Culture used to format numbers and dates. Defaults to the current culture.</param>
    /// <returns>The rendered text.</returns>
    public static string Render(
        string template,
        IReadOnlyDictionary<string, object?>? arguments,
        CultureInfo? culture = null)
    {
        if (string.IsNullOrEmpty(template) || arguments is null || arguments.Count == 0)
        {
            return template;
        }

        var effectiveCulture = culture ?? CultureInfo.CurrentCulture;

        return PlaceholderPattern().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;

            if (!arguments.TryGetValue(name, out var value))
            {
                // Leave it visible rather than guessing. See the remarks on this class.
                return match.Value;
            }

            if (value is null)
            {
                return string.Empty;
            }

            if (Iso(value) is { } isoDate)
            {
                return isoDate;
            }

            var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;

            return format is not null && value is IFormattable formattable
                ? formattable.ToString(format, effectiveCulture)
                : Convert.ToString(value, effectiveCulture) ?? string.Empty;
        });
    }

    /// <summary>
    /// Renders a date the same way everywhere, or null when the value is not a date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whatever the template asked for. A short date is a different string in every culture:
    /// <c>8/1/2026</c> is August in one country and January in another, and a message refusing
    /// to pay somebody for a period is not the place to leave which one it means to the reader.
    /// </para>
    /// <para>
    /// The stronger reason is the calendar. Under an Arabic culture the framework renders dates
    /// in the Hijri calendar, so the same instruction that gives 2026-08-01 in English gives a
    /// number in the 1440s in Arabic — not another spelling of the date but a different date, in
    /// a message whose whole purpose is to say which days are meant.
    /// </para>
    /// </remarks>
    private static string? Iso(object value) => value switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH':'mm", CultureInfo.InvariantCulture),
        DateTime moment => moment.ToString("yyyy-MM-dd HH':'mm", CultureInfo.InvariantCulture),
        DateTimeOffset moment => moment.ToString("yyyy-MM-dd HH':'mm", CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>
    /// Lists the placeholder names a template uses. Lets a startup check confirm that every
    /// placeholder in a shipped message has an argument supplied at the point it is raised.
    /// </summary>
    /// <param name="template">The template text.</param>
    public static IReadOnlyCollection<string> PlaceholdersIn(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return [];
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in PlaceholderPattern().Matches(template))
        {
            names.Add(match.Groups["name"].Value);
        }

        return names;
    }

    // {Name} or {Name:format}. The name is a plain identifier; the format runs to the closing
    // brace, which covers every standard and custom .NET format string in practice.
    [GeneratedRegex(@"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::(?<format>[^}]+))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
