using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Platform.Core.Cqrs;

/// <summary>
/// Thrown when an operation is refused for a reason that is not a business outcome.
/// </summary>
/// <remarks>
/// <para>
/// ASAP returns <see cref="Kernel.Results.Result"/> for business outcomes and throws for faults,
/// and this sits on the throwing side. A journal that will not balance is the system working, and
/// comes back as a result. A caller reaching for an operation they have no permission to invoke,
/// or acting with no company selected, is a request that should never have been made -- there is
/// no partial success to report and nothing sensible to return.
/// </para>
/// <para>
/// The practical reason it throws rather than returns: a query handler returns a report or a list,
/// not a result, so there is no room in its return type to carry a refusal. Throwing keeps the
/// behaviour identical whether the operation was a command or a query.
/// </para>
/// <para>
/// The exception carries a full <see cref="AsapMessage"/>, so the client receives the same
/// translated text, resolution and override permission it would get from any other refusal.
/// </para>
/// </remarks>
public sealed class AsapMessageException : Exception
{
    /// <summary>Creates the exception around a message.</summary>
    /// <param name="message">Why the operation was refused. Must be a failure.</param>
    /// <exception cref="ArgumentException">The message is not an error or a block.</exception>
    public AsapMessageException(AsapMessage message)
        : base(message?.ToString() ?? throw new ArgumentNullException(nameof(message)))
    {
        if (!message.IsFailure)
        {
            throw new ArgumentException(
                $"Cannot raise {message.Code} as an exception; it is severity {message.Severity}. "
                + "Only Error and Blocked messages stop an operation.",
                nameof(message));
        }

        AsapMessage = message;
    }

    /// <summary>The refusal, ready to return to the client.</summary>
    public AsapMessage AsapMessage { get; }

    /// <summary>
    /// True when the refusal was a permission problem, which the API answers with 403 rather
    /// than 422.
    /// </summary>
    public bool IsPermissionFailure =>
        AsapMessage.Code.Value.StartsWith("SEC.", StringComparison.Ordinal);
}
