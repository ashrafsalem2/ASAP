namespace ASAP.Platform.Kernel.Messaging;

/// <summary>
/// Turns a <see cref="MessageCode"/> plus the values behind it into a rendered, localised
/// <see cref="AsapMessage"/>. Modules and extensions raise messages through this rather than
/// building strings themselves, which is what keeps every diagnostic in ASAP translatable,
/// documented, and stable enough for clients to branch on.
/// </summary>
public interface IMessageCatalog
{
    /// <summary>
    /// Renders a message in the language of the current user.
    /// </summary>
    /// <param name="code">The code to render. Must already be registered.</param>
    /// <param name="arguments">
    /// Values for the template placeholders, keyed by name and matched case-insensitively.
    /// They are also carried through to <see cref="AsapMessage.Arguments"/> unformatted.
    /// </param>
    /// <param name="target">What the message is about: the field at fault or the related record.</param>
    /// <returns>The rendered message.</returns>
    /// <exception cref="KeyNotFoundException">
    /// The code is not registered. Failing loudly here is deliberate: a message that reaches a
    /// user as a bare code is a bug, and it should surface in development rather than production.
    /// </exception>
    AsapMessage Render(
        MessageCode code,
        IReadOnlyDictionary<string, object?>? arguments = null,
        MessageTarget target = default);

    /// <summary>Looks up a definition without rendering it.</summary>
    /// <param name="code">The code to look up.</param>
    /// <returns>The definition, or null when the code is unknown.</returns>
    MessageDefinition? Find(MessageCode code);

    /// <summary>
    /// Every registered definition. Powers the developer documentation, which publishes the
    /// full list of codes ASAP can raise, and powers the translation export.
    /// </summary>
    IReadOnlyCollection<MessageDefinition> All { get; }
}
