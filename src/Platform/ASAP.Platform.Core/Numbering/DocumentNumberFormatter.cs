using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ASAP.Platform.Core.Numbering;

/// <summary>
/// Builds and advances document numbers such as <c>GJ-2026-00042</c>.
/// </summary>
/// <remarks>
/// <para>
/// A number is a prefix, optional date placeholders, and a zero-padded counter at the end:
/// <c>"GJ-{YYYY}-00001"</c> becomes <c>GJ-2026-00001</c> and then <c>GJ-2026-00002</c>. The
/// counter is always the trailing run of digits, and its width is fixed by however many digits
/// the starting number was written with. Writing <c>00001</c> rather than <c>1</c> is what makes
/// document numbers sort correctly as text, which matters everywhere they are listed or exported.
/// </para>
/// <para>
/// Placeholders must sit ahead of the counter. <c>GJ-{YYYY}</c> with no counter after it would
/// leave the year itself as the trailing digits, and incrementing would advance the year.
/// <see cref="ValidatePattern"/> refuses such a pattern when the series is set up, rather than
/// letting it produce nonsense on the day it is first used.
/// </para>
/// </remarks>
public static partial class DocumentNumberFormatter
{
    /// <summary>
    /// Substitutes the date placeholders in a pattern.
    /// </summary>
    /// <param name="pattern">
    /// The pattern, which may contain <c>{YYYY}</c>, <c>{YY}</c>, <c>{MM}</c> or <c>{DD}</c>.
    /// </param>
    /// <param name="date">The date to substitute, normally the document posting date.</param>
    /// <returns>The pattern with placeholders replaced.</returns>
    public static string ApplyDate(string pattern, DateOnly date)
    {
        if (string.IsNullOrEmpty(pattern) || !pattern.Contains('{', StringComparison.Ordinal))
        {
            return pattern;
        }

        return DatePlaceholderPattern().Replace(pattern, match => match.Groups["token"].Value switch
        {
            "YYYY" => date.Year.ToString("D4", CultureInfo.InvariantCulture),
            "YY" => (date.Year % 100).ToString("D2", CultureInfo.InvariantCulture),
            "MM" => date.Month.ToString("D2", CultureInfo.InvariantCulture),
            "DD" => date.Day.ToString("D2", CultureInfo.InvariantCulture),
            _ => match.Value,
        });
    }

    /// <summary>
    /// Advances a number by a step, keeping the prefix and the counter width.
    /// </summary>
    /// <param name="current">The last number issued, for example <c>GJ-2026-00041</c>.</param>
    /// <param name="step">How much to advance by. Normally 1.</param>
    /// <param name="next">The next number, when there is one.</param>
    /// <returns>
    /// False when the number has no trailing counter, or when advancing would need more digits
    /// than the counter has. Both mean the series is unusable and the caller must say so rather
    /// than silently widening the number, which would break the sort order of everything already
    /// issued.
    /// </returns>
    public static bool TryAdvance(string current, int step, [NotNullWhen(true)] out string? next)
    {
        next = null;

        if (string.IsNullOrEmpty(current) || step < 1)
        {
            return false;
        }

        var match = CounterPattern().Match(current);

        if (!match.Success)
        {
            return false;
        }

        var counter = match.Groups["counter"].Value;
        var prefix = current[..match.Index];

        // A counter wide enough to overflow a long would already be an absurd document number,
        // but parsing defensively costs nothing and turns a crash into a clear refusal.
        if (!long.TryParse(counter, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var advanced = value + step;
        var advancedText = advanced.ToString(CultureInfo.InvariantCulture);

        // Refuse to widen. Issuing GJ-2026-100000 after GJ-2026-99999 would sort before every
        // earlier number as text, so the series is declared exhausted instead.
        if (advancedText.Length > counter.Length)
        {
            return false;
        }

        next = prefix + advancedText.PadLeft(counter.Length, '0');
        return true;
    }

    /// <summary>
    /// Reads the counter off a number, for comparing two numbers from the same series.
    /// </summary>
    /// <param name="number">The document number.</param>
    /// <param name="counter">The counter value, when the number has one.</param>
    public static bool TryReadCounter(string number, out long counter)
    {
        counter = 0;

        if (string.IsNullOrEmpty(number))
        {
            return false;
        }

        var match = CounterPattern().Match(number);

        return match.Success
            && long.TryParse(
                match.Groups["counter"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out counter);
    }

    /// <summary>
    /// Reads the fixed part of a number, everything ahead of the counter. Two numbers sharing a
    /// prefix belong to the same run of the series.
    /// </summary>
    /// <param name="number">The document number.</param>
    /// <returns>The prefix, or the whole string when there is no counter.</returns>
    public static string ReadPrefix(string number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return string.Empty;
        }

        var match = CounterPattern().Match(number);

        return match.Success ? number[..match.Index] : number;
    }

    /// <summary>
    /// Checks a starting number is usable before the series is saved.
    /// </summary>
    /// <param name="pattern">The starting number as the administrator typed it.</param>
    /// <returns>The reason it is unusable, or null when it is fine.</returns>
    public static string? ValidatePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "A starting number is required.";
        }

        foreach (Match placeholder in DatePlaceholderPattern().Matches(pattern))
        {
            var token = placeholder.Groups["token"].Value;

            if (token is not ("YYYY" or "YY" or "MM" or "DD"))
            {
                return $"'{{{token}}}' is not a recognised placeholder. Use YYYY, YY, MM or DD.";
            }
        }

        // The counter has to be digits the administrator actually typed. Checking the pattern
        // rather than the substituted sample is what catches GJ-{YYYY}: it substitutes to
        // GJ-2026, which ends in digits and looks perfectly valid, and whose first increment
        // would issue GJ-2027 in the middle of 2026.
        var literalCounter = CounterPattern().Match(pattern);

        if (!literalCounter.Success)
        {
            return "The starting number must end in digits, which ASAP increments. "
                 + "For example GJ-{YYYY}-00001.";
        }

        var sample = ApplyDate(pattern, new DateOnly(2026, 12, 31));
        var substitutedCounter = CounterPattern().Match(sample);

        // A placeholder butted straight against the counter merges with it: INV-{YY}0001 becomes
        // INV-260001, where the counter is six digits rather than four and the first increment
        // advances the year. Separate them with any non-digit character.
        if (substitutedCounter.Value.Length != literalCounter.Value.Length)
        {
            return "A date placeholder runs straight into the counter, so they would be "
                 + "incremented together. Separate them, for example INV-{YY}-0001.";
        }

        if (!TryAdvance(sample, 1, out _))
        {
            return "The starting number is already at the end of its range and cannot be advanced. "
                 + "Widen the counter, for example 00001 rather than 9.";
        }

        return null;
    }

    [GeneratedRegex(@"\{(?<token>[A-Za-z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex DatePlaceholderPattern();

    [GeneratedRegex(@"(?<counter>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex CounterPattern();
}
