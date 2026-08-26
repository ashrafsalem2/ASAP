namespace ASAP.Platform.Kernel.Messaging;

/// <summary>
/// The catalogue entry behind one <see cref="MessageCode"/>: its severity and its untranslated
/// text templates. Modules register their definitions at startup; the catalogue renders them
/// into an <see cref="AsapMessage"/> when something actually goes wrong.
/// </summary>
/// <remarks>
/// Templates use named placeholders in braces, as in <c>"Out of balance by {Difference} {Currency}."</c>
/// Named rather than positional, so a translator can reorder them freely without breaking the
/// substitution, which matters when translating between English and Arabic.
/// </remarks>
public sealed record MessageDefinition
{
    /// <summary>The code this entry defines.</summary>
    public required MessageCode Code { get; init; }

    /// <summary>Severity every message raised under this code carries.</summary>
    public required MessageSeverity Severity { get; init; }

    /// <summary>Template for the one-line summary.</summary>
    public required LocalizedText Title { get; init; }

    /// <summary>Template for the explanation, with placeholders for the real values.</summary>
    public LocalizedText? Detail { get; init; }

    /// <summary>Template for the way forward. Required when <see cref="Severity"/> is Blocked.</summary>
    public LocalizedText? Resolution { get; init; }

    /// <summary>Permission that overrides this block, if any.</summary>
    public string? OverridePermission { get; init; }

    /// <summary>Anchor into the user documentation.</summary>
    public string? HelpTopic { get; init; }

    /// <summary>
    /// Checks the entry obeys the rules ASAP sets for its own messages.
    /// </summary>
    /// <returns>The reason it is invalid, or null when it is fine.</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Title.English))
        {
            return $"Message {Code} has no English title.";
        }

        // The whole point of Blocked is that ASAP refuses something the user legitimately
        // wanted. Refusing without saying how to proceed leaves them stuck, so the catalogue
        // rejects such an entry at startup rather than shipping a dead end.
        if (Severity is MessageSeverity.Blocked && string.IsNullOrWhiteSpace(Resolution?.English))
        {
            return $"Message {Code} is Blocked but offers no resolution. "
                 + "Every blocking message must tell the user how to proceed.";
        }

        return null;
    }
}
