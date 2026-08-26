namespace ASAP.Platform.Kernel.Events;

/// <summary>Marker for anything ASAP publishes to subscribers.</summary>
public interface IAsapEvent
{
    /// <summary>
    /// Stable name used in logs, the audit trail, and the developer documentation, for example
    /// <c>Finance.JournalPosting</c>. Defaults to the type name so most events need not set it.
    /// </summary>
    string EventName => GetType().Name;
}

/// <summary>
/// Something that happened inside the current unit of work. Subscribers run in the same
/// transaction as the operation that raised the event, so whatever they write commits or rolls
/// back with it.
/// </summary>
/// <remarks>
/// Use this when the reaction must be atomic with the cause: updating an item costing record as
/// a shipment posts, say. For anything slow or external, raise an
/// <see cref="IIntegrationEvent"/> instead so a failing e-mail server cannot roll back a sale.
/// </remarks>
public interface IDomainEvent : IAsapEvent;

/// <summary>
/// Something that happened and has already been committed. Subscribers run after the
/// transaction succeeds, so their failure cannot undo it.
/// </summary>
/// <remarks>
/// This is the right hook for anything crossing a boundary: notifying a branch, calling a
/// payment gateway, pushing to a reporting store, printing to a hardware station. Delivery is
/// at-least-once, so subscribers must tolerate seeing the same event twice.
/// </remarks>
public interface IIntegrationEvent : IAsapEvent
{
    /// <summary>When the thing happened, in UTC. Set by the publisher.</summary>
    DateTime OccurredAtUtc { get; }
}
