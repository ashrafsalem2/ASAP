using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Events;

/// <summary>
/// An integration event waiting to be delivered.
/// </summary>
/// <remarks>
/// <para>
/// The row is written in the same transaction as the change that caused it. That pairing is the
/// whole point: it makes it impossible for ASAP to announce a sale that later rolled back, or to
/// commit one that was never announced. A background worker picks rows up afterwards and
/// delivers them.
/// </para>
/// <para>
/// Delivery is at-least-once, not exactly-once. A worker can publish a message and fail before
/// marking it done, and the next pass will publish it again. Subscribers must therefore tolerate
/// seeing the same event twice, which is why every one carries a stable
/// <see cref="Entity.Id"/> they can deduplicate on.
/// </para>
/// </remarks>
public sealed class OutboxMessage : Entity, ITenantScoped
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>Company the event happened in, or null for something tenant-wide.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>Branch the event happened at, or null at head office.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// The event name, for example <c>Finance.JournalPosted</c>. Routes the message to its
    /// subscribers without deserialising the payload first.
    /// </summary>
    public required string EventName { get; set; }

    /// <summary>
    /// The assembly-qualified type name, so the worker can rebuild the event object. Kept
    /// alongside <see cref="EventName"/> rather than instead of it, because the routing name must
    /// survive a class being moved or renamed.
    /// </summary>
    public required string EventType { get; set; }

    /// <summary>The serialised event.</summary>
    public required string Payload { get; set; }

    /// <summary>When the thing happened, in UTC. Not when it was delivered.</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>When delivery succeeded, in UTC. Null while still pending.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>How many delivery attempts have been made.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// When the next attempt is due, in UTC. Set by the worker to back off after a failure, so a
    /// dead endpoint does not spin.
    /// </summary>
    public DateTime? NextAttemptAtUtc { get; set; }

    /// <summary>What went wrong on the last attempt, for diagnosis.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// True once delivery has failed enough times to be given up on. The message stays in the
    /// table for someone to look at rather than being deleted, because a lost integration event
    /// is usually the first symptom of a larger problem.
    /// </summary>
    public bool IsDeadLettered { get; set; }
}
