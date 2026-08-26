using System.Text.RegularExpressions;

namespace ASAP.Platform.Kernel.Messaging;

/// <summary>
/// A stable identifier for one diagnostic, shaped <c>MODULE.AREA.REASON</c> — for example
/// <c>FIN.JOURNAL.OUT_OF_BALANCE</c> or <c>PROMO.OFFER.BELOW_COST</c>.
/// </summary>
/// <remarks>
/// Codes are part of ASAP's public contract. Support staff quote them, the documentation
/// indexes them, extensions raise them, and clients branch on them — so a code's meaning must
/// never change once shipped. Wording may be reworded and retranslated freely; the code may not.
/// </remarks>
public readonly partial record struct MessageCode
{
    private const int MaxLength = 96;

    /// <summary>The raw code text, upper case.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a code, rejecting anything that does not match <c>MODULE.AREA.REASON</c>.
    /// Validating at construction keeps malformed codes out of the catalogue entirely.
    /// </summary>
    /// <param name="value">The code text, for example <c>FIN.JOURNAL.OUT_OF_BALANCE</c>.</param>
    /// <exception cref="ArgumentException">The code is empty, too long, or badly shaped.</exception>
    public MessageCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A message code cannot be empty.", nameof(value));
        }

        var normalised = value.Trim().ToUpperInvariant();

        if (normalised.Length > MaxLength)
        {
            throw new ArgumentException(
                $"Message code '{normalised}' is {normalised.Length} characters; the limit is {MaxLength}.",
                nameof(value));
        }

        if (!CodePattern().IsMatch(normalised))
        {
            throw new ArgumentException(
                $"Message code '{normalised}' is not shaped MODULE.AREA.REASON — for example FIN.JOURNAL.OUT_OF_BALANCE.",
                nameof(value));
        }

        Value = normalised;
    }

    /// <summary>The module segment, for example <c>FIN</c>. Used to route a message to its owning module.</summary>
    public string Module => Value[..Value.IndexOf('.', StringComparison.Ordinal)];

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Reads a code from its text form.</summary>
    public static implicit operator MessageCode(string value) => new(value);

    /// <summary>Reads the text form of a code.</summary>
    public static implicit operator string(MessageCode code) => code.Value;

    [GeneratedRegex(@"^[A-Z0-9]+(\.[A-Z0-9_]+){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
