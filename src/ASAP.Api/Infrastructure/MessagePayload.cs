using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Api.Infrastructure;

/// <summary>
/// One <see cref="AsapMessage"/> on its way to a client.
/// </summary>
/// <remarks>
/// <para>
/// Every endpoint that returns messages projects them through here, and nowhere else. Each one
/// used to write its own anonymous object, and they had quietly diverged: the stock receipt
/// carried the resolution, the journal receipt dropped it, and the settlement receipt dropped the
/// severity too, so a warning arrived at the client looking like a remark.
/// </para>
/// <para>
/// The failure mode is worse than the duplication. Adding a field to <see cref="AsapMessage"/>
/// means remembering every projection that should carry it, and nothing complains when one is
/// missed -- the field simply never arrives, and the screen goes on rendering something subtly
/// wrong. One shared shape makes that impossible.
/// </para>
/// </remarks>
/// <param name="Code">The stable identifier.</param>
/// <param name="Severity">How much weight it carries.</param>
/// <param name="Title">What happened.</param>
/// <param name="Detail">Why, with the figures in it.</param>
/// <param name="Resolution">What to do, or what was allowed when it was overridden.</param>
/// <param name="OverridePermission">The permission that would let someone push past it.</param>
/// <param name="WasOverridden">Whether it was a refusal the caller was entitled to override.</param>
/// <param name="Target">The field or record at fault.</param>
/// <param name="Arguments">The raw values behind the text, for the client to re-format.</param>
public sealed record MessagePayload(
    string Code,
    string Severity,
    string Title,
    string? Detail,
    string? Resolution,
    string? OverridePermission,
    bool WasOverridden,
    MessageTargetPayload? Target,
    IReadOnlyDictionary<string, object?> Arguments)
{
    /// <summary>Projects one message.</summary>
    /// <param name="message">The rendered message.</param>
    /// <returns>The payload.</returns>
    public static MessagePayload From(AsapMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new MessagePayload(
            message.Code.Value,
            message.Severity.ToString(),
            message.Title,
            message.Detail,
            message.Resolution,
            message.OverridePermission,
            message.WasOverridden,
            message.Target.IsEmpty
                ? null
                : new MessageTargetPayload(
                    message.Target.Field,
                    message.Target.EntityType,
                    message.Target.DisplayNo),
            message.Arguments);
    }

    /// <summary>Projects a set of messages.</summary>
    /// <param name="messages">The rendered messages.</param>
    /// <returns>The payloads, in the order given.</returns>
    public static IReadOnlyList<MessagePayload> FromAll(IEnumerable<AsapMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return [.. messages.Select(From)];
    }
}

/// <summary>What a message is about.</summary>
/// <param name="Field">The input at fault.</param>
/// <param name="EntityType">The kind of record it concerns.</param>
/// <param name="DisplayNo">The number a user would recognise it by.</param>
public sealed record MessageTargetPayload(string? Field, string? EntityType, string? DisplayNo);
