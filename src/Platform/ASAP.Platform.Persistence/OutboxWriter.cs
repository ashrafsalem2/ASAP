using ASAP.Platform.Core.Events;

namespace ASAP.Platform.Persistence;

/// <summary>
/// Writes integration events into the same unit of work as the change that raised them.
/// </summary>
/// <remarks>
/// Deliberately only adds to the change tracker; it never saves. The row must commit with the
/// change that caused it, and calling save here would break exactly the guarantee the outbox
/// exists to provide.
/// </remarks>
/// <param name="context">The unit of work the event belongs to.</param>
public sealed class OutboxWriter(AsapDbContext context) : IOutboxWriter
{
    /// <inheritdoc />
    public void Add(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        context.Outbox.Add(message);
    }
}
