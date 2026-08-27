using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ASAP.Platform.Core.Messaging;

namespace ASAP.Platform.Core.Printing;

/// <summary>
/// Renders a print template against a document.
/// </summary>
/// <remarks>
/// <para>
/// A deliberately small language, because the person editing a receipt layout is a shop manager
/// rather than a developer. There are exactly three things in it: a placeholder, a repeated
/// region, and everything else, which is printed as written.
/// </para>
/// <para>
/// Placeholders are the same <c>{Name}</c> and <c>{Name:format}</c> the message catalogue uses,
/// rendered by the same code. That is worth more than it sounds: a date on a receipt and a date
/// in a refusal come out identically, and neither can quietly switch calendars because somebody
/// changed the language.
/// </para>
/// <para>
/// A repeated region is <c>[[lines]] … [[/lines]]</c>. Inside it the placeholders come from each
/// line in turn; outside, from the document. Nesting is not supported and is not wanted: a
/// receipt has lines, and a line does not have lines.
/// </para>
/// </remarks>
public static partial class PrintTemplateRenderer
{
    /// <summary>
    /// Renders a template.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="document">Values for the placeholders outside a repeated region.</param>
    /// <param name="regions">
    /// The repeatable regions by name, each a list of one set of values per repetition. A region
    /// the template does not use costs nothing; a region the template uses and this does not have
    /// prints as nothing, which is what an empty receipt should look like.
    /// </param>
    /// <param name="culture">Culture used for numbers. Dates are ISO whatever it says.</param>
    /// <returns>The rendered text.</returns>
    public static string Render(
        string template,
        IReadOnlyDictionary<string, object?> document,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>? regions = null,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var effective = culture ?? CultureInfo.CurrentCulture;

        var expanded = RegionPattern().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            var body = match.Groups["body"].Value;

            if (regions is null || !regions.TryGetValue(name, out var rows) || rows.Count == 0)
            {
                return string.Empty;
            }

            var built = new StringBuilder();

            foreach (var row in rows)
            {
                // The document's values are visible inside a line too, so a template can print
                // the receipt number against every line without repeating it in each row.
                built.Append(MessageTemplateRenderer.Render(body, Merge(document, row), effective));
            }

            return built.ToString();
        });

        return MessageTemplateRenderer.Render(expanded, document, effective);
    }

    /// <summary>
    /// Lists the placeholder names a template uses, inside and outside its regions.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <returns>Every name the template refers to.</returns>
    /// <remarks>
    /// Lets the editor tell somebody that <c>{Totl}</c> is not a field before they print two
    /// hundred receipts with a gap where the total should have been.
    /// </remarks>
    public static IReadOnlyCollection<string> PlaceholdersIn(string template)
        => MessageTemplateRenderer.PlaceholdersIn(
            RegionPattern().Replace(template ?? string.Empty, "${body}"));

    /// <summary>Lists the repeated regions a template uses.</summary>
    /// <param name="template">The template text.</param>
    /// <returns>The region names, in the order they appear.</returns>
    public static IReadOnlyList<string> RegionsIn(string template)
        => [.. RegionPattern()
            .Matches(template ?? string.Empty)
            .Select(static m => m.Groups["name"].Value)];

    private static Dictionary<string, object?> Merge(
        IReadOnlyDictionary<string, object?> document,
        IReadOnlyDictionary<string, object?> row)
    {
        var merged = new Dictionary<string, object?>(document, StringComparer.OrdinalIgnoreCase);

        // The line wins. A line's own Description is what a line is about, whatever the document
        // calls its description.
        foreach (var (key, value) in row)
        {
            merged[key] = value;
        }

        return merged;
    }

    // [[name]] ... [[/name]] across lines. Non-greedy, so two regions in one template do not
    // swallow everything between the first opening and the last closing tag.
    [GeneratedRegex(
        @"\[\[(?<name>[A-Za-z_][A-Za-z0-9_]*)\]\](?<body>.*?)\[\[/\k<name>\]\]",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex RegionPattern();
}
