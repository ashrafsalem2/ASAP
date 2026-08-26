namespace ASAP.Platform.Kernel.Events;

/// <summary>
/// Reacts to one kind of ASAP event. Register an implementation in the container and the
/// publisher will find it; nothing has to be wired by hand.
/// </summary>
/// <typeparam name="TEvent">The event this handler subscribes to.</typeparam>
/// <remarks>
/// This is the primary extension point in ASAP. An extension that wants to run alongside core
/// behaviour implements this interface rather than editing core code, which means the extension
/// keeps working across core upgrades as long as the event itself keeps its shape.
/// </remarks>
public interface IEventHandler<in TEvent>
    where TEvent : IAsapEvent
{
    /// <summary>
    /// Handles the event.
    /// </summary>
    /// <param name="asapEvent">What happened.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <remarks>
    /// For a <see cref="IDomainEvent"/> this runs inside the caller transaction, so throwing
    /// rolls the whole operation back. For an <see cref="IIntegrationEvent"/> the transaction
    /// has already committed, so throwing is logged and retried but cannot undo anything.
    /// </remarks>
    Task HandleAsync(TEvent asapEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Controls the order handlers run in, lowest first. Leave it alone unless the order truly
    /// matters; handlers that depend on each other are usually a sign the work belongs in one
    /// handler. Core handlers sit at 0, so an extension can use a negative value to run first.
    /// </summary>
    int Order => 0;
}
