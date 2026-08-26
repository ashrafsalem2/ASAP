namespace ASAP.Platform.Core.Events;

/// <summary>
/// Adds an integration event to the outbox, inside whatever transaction is already open.
/// </summary>
/// <remarks>
/// Declared here rather than taking a database dependency directly, so the event publisher stays
/// in the platform core and the persistence layer supplies the implementation. It also makes the
/// publisher testable without a database.
/// </remarks>
public interface IOutboxWriter
{
    /// <summary>
    /// Queues a message for delivery after the current transaction commits.
    /// </summary>
    /// <param name="message">The message to queue.</param>
    /// <remarks>
    /// The row must be written in the same transaction as the change that caused it. That
    /// pairing is what stops ASAP from announcing a sale that later rolled back, or committing
    /// one that was never announced.
    /// </remarks>
    void Add(OutboxMessage message);
}
