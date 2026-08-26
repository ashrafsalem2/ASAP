namespace ASAP.Platform.Kernel.Messaging;

/// <summary>
/// A string in every language ASAP ships. English is required; other languages fall back to
/// English when a translation is missing, so a partly translated deployment still reads sensibly.
/// </summary>
/// <param name="English">The English text. Required.</param>
/// <param name="Arabic">The Arabic text, or null to fall back to English.</param>
public readonly record struct LocalizedText(string English, string? Arabic = null)
{
    /// <summary>
    /// Picks the text for a culture, falling back to English when the translation is absent.
    /// </summary>
    /// <param name="cultureName">A culture name such as <c>ar</c>, <c>ar-SA</c> or <c>en-US</c>.</param>
    public string For(string? cultureName)
    {
        if (cultureName is not null &&
            cultureName.StartsWith("ar", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(Arabic))
        {
            return Arabic;
        }

        return English;
    }

    /// <inheritdoc />
    public override string ToString() => English;

    /// <summary>Reads English-only text.</summary>
    public static implicit operator LocalizedText(string english) => new(english);
}
