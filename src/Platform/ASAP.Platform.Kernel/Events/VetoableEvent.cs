using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Platform.Kernel.Events;

/// <summary>
/// An event raised <em>before</em> ASAP does something, which a subscriber may object to.
/// </summary>
/// <remarks>
/// <para>
/// This is how an extension enforces a rule the core knows nothing about, without forking core
/// code. A subscriber to the event raised before a sales order is released can call
/// <see cref="Object"/> when the customer is over their credit limit; the release stops and the
/// user sees the objection as an ordinary ASAP message, indistinguishable from a built-in rule.
/// </para>
/// <para>
/// An objection must be a real message from the catalogue, so a rule added by an extension is
/// still translated, still documented, and still carries a resolution telling the user what to
/// do about it.
/// </para>
/// <para>
/// Every subscriber runs even after one objects, so the user sees every reason the operation
/// was refused at once instead of discovering them one attempt at a time.
/// </para>
/// </remarks>
public abstract class VetoableEvent : IDomainEvent
{
    private readonly List<AsapMessage> _objections = [];

    /// <inheritdoc />
    public virtual string EventName => GetType().Name;

    /// <summary>Every objection raised so far, in the order the subscribers raised them.</summary>
    public IReadOnlyList<AsapMessage> Objections => _objections;

    /// <summary>True when at least one subscriber refused the operation.</summary>
    public bool IsVetoed => _objections.Exists(static m => m.IsFailure);

    /// <summary>
    /// Refuses the operation, giving the user the reason.
    /// </summary>
    /// <param name="message">
    /// Why the operation is being refused. Must be of severity
    /// <see cref="MessageSeverity.Error"/> or <see cref="MessageSeverity.Blocked"/>; anything
    /// milder belongs on <see cref="Warn"/>, which does not stop anything.
    /// </param>
    /// <exception cref="ArgumentException">The message is not a failure.</exception>
    public void Object(AsapMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!message.IsFailure)
        {
            throw new ArgumentException(
                $"Cannot object with message {message.Code}, which is severity {message.Severity}. "
                + "An objection must be Error or Blocked. Use Warn for advisory messages.",
                nameof(message));
        }

        _objections.Add(message);
    }

    /// <summary>
    /// Attaches a message that travels back to the user without stopping the operation. Useful
    /// for advising that stock has fallen below its reorder point while still letting the
    /// shipment post.
    /// </summary>
    /// <param name="message">The advisory message. Must not be a failure.</param>
    /// <exception cref="ArgumentException">The message is a failure.</exception>
    public void Warn(AsapMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.IsFailure)
        {
            throw new ArgumentException(
                $"Cannot warn with message {message.Code}, which is severity {message.Severity}. "
                + "Use Object to refuse the operation.",
                nameof(message));
        }

        _objections.Add(message);
    }
}
