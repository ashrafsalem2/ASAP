using ASAP.Platform.Kernel.Results;

namespace ASAP.Platform.Kernel.Events;

/// <summary>
/// Delivers events to their subscribers. Modules publish through this at every point an
/// extension might reasonably want to join in.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Delivers a domain event to every subscriber, inside the current transaction.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="asapEvent">What happened.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task PublishAsync<TEvent>(TEvent asapEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;

    /// <summary>
    /// Delivers an event that subscribers may refuse, and reports what they decided.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="asapEvent">The operation about to happen.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// A failed result carrying every objection when any subscriber refused, otherwise a
    /// successful result carrying any warnings they attached. Callers must respect a failure:
    /// this is the mechanism by which extensions enforce their own rules.
    /// </returns>
    Task<Result> PublishVetoableAsync<TEvent>(
        TEvent asapEvent,
        CancellationToken cancellationToken = default)
        where TEvent : VetoableEvent;

    /// <summary>
    /// Queues an integration event for delivery after the current transaction commits.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="asapEvent">What happened.</param>
    /// <remarks>
    /// The event is written to the outbox in the same transaction as the change that caused it,
    /// then dispatched by a background worker. That pairing is what stops ASAP from either
    /// announcing a sale that later rolled back, or committing one that was never announced.
    /// </remarks>
    void Enqueue<TEvent>(TEvent asapEvent)
        where TEvent : IIntegrationEvent;
}
