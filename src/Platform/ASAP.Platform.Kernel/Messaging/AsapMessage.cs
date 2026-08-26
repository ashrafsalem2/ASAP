namespace ASAP.Platform.Kernel.Messaging;

/// <summary>
/// One thing ASAP has to tell the user, rendered and ready to display.
/// </summary>
/// <remarks>
/// <para>
/// Every message answers three questions in order: <b>what</b> happened (<see cref="Title"/>),
/// <b>why</b> it happened with the actual numbers filled in (<see cref="Detail"/>), and
/// <b>what to do about it</b> (<see cref="Resolution"/>). A message that refuses an operation
/// without answering the third question is a defect in ASAP, not a user error.
/// </para>
/// <para>
/// <see cref="Arguments"/> carries the raw, unformatted values behind the rendered text. The
/// client uses them to format currency and dates in its own locale, and integrations use them
/// to branch on the numbers without parsing prose.
/// </para>
/// </remarks>
public sealed record AsapMessage
{
    /// <summary>Stable identifier for this diagnostic. Safe to branch on; the wording is not.</summary>
    public required MessageCode Code { get; init; }

    /// <summary>How much weight the message carries.</summary>
    public required MessageSeverity Severity { get; init; }

    /// <summary>One line saying what happened, already localised.</summary>
    public required string Title { get; init; }

    /// <summary>Why it happened, with the real values substituted in. Already localised.</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// What the user should do next, already localised. Required for
    /// <see cref="MessageSeverity.Blocked"/>, because refusing without a way forward is a dead end.
    /// </summary>
    public string? Resolution { get; init; }

    /// <summary>What the message is about: the field at fault, the record it concerns, or both.</summary>
    public MessageTarget Target { get; init; }

    /// <summary>
    /// The raw values behind the rendered text, keyed by placeholder name. Lets the client
    /// re-format numbers for its own locale and lets integrations read the figures directly.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; init; }
        = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The permission that would let a user override this block, for example
    /// <c>Promotions.Offer.OverrideMargin</c>. Null when nothing can override it: a posting
    /// into a closed fiscal year, for instance, is refused to everyone.
    /// </summary>
    public string? OverridePermission { get; init; }

    /// <summary>
    /// True when this began as a <see cref="MessageSeverity.Blocked"/> refusal and was downgraded
    /// because the caller holds <see cref="OverridePermission"/>.
    /// </summary>
    /// <remarks>
    /// Recorded explicitly rather than inferred from severity plus permission, because those two
    /// together are also the shape of a message that was only ever a warning. The difference
    /// matters twice over: the audit log has to record exactly which protections somebody pushed
    /// past, and the text shown has to stop telling the user how to avoid a refusal that did not
    /// happen.
    /// </remarks>
    public bool WasOverridden { get; init; }

    /// <summary>Anchor into the user documentation explaining this code in full.</summary>
    public string? HelpTopic { get; init; }

    /// <summary>True when this message alone is enough to stop the operation.</summary>
    public bool IsFailure => Severity is MessageSeverity.Error or MessageSeverity.Blocked;

    /// <summary>True when a sufficiently privileged user could push this through anyway.</summary>
    public bool IsOverridable => OverridePermission is not null;

    /// <inheritdoc />
    public override string ToString()
        => Detail is null ? $"[{Code}] {Title}" : $"[{Code}] {Title} - {Detail}";
}
