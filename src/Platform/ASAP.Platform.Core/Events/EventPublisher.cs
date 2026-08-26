using System.Text.Json;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ASAP.Platform.Core.Events;

/// <summary>
/// Delivers events to whatever has subscribed to them.
/// </summary>
/// <remarks>
/// Handlers are resolved from the container each time rather than cached, so an extension loaded
/// after startup is picked up without a restart, and so a scoped handler gets the right unit of
/// work.
/// </remarks>
/// <param name="services">Resolves the handlers.</param>
/// <param name="outbox">Receives integration events.</param>
/// <param name="tenantContext">Stamps queued events with where they happened.</param>
/// <param name="clock">Stamps queued events with when they happened.</param>
/// <param name="logger">Records handler failures.</param>
public sealed class EventPublisher(
    IServiceProvider services,
    IOutboxWriter outbox,
    ITenantContext tenantContext,
    IClock clock,
    ILogger<EventPublisher> logger) : IEventPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent asapEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(asapEvent);

        foreach (var handler in ResolveHandlers<TEvent>())
        {
            // Deliberately not caught. A domain event runs inside the caller transaction, and a
            // handler that fails means the reaction did not happen -- so the cause must not be
            // allowed to commit either. Swallowing here would leave a shipment posted with its
            // costing record never updated.
            await handler.HandleAsync(asapEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<Result> PublishVetoableAsync<TEvent>(
        TEvent asapEvent,
        CancellationToken cancellationToken = default)
        where TEvent : VetoableEvent
    {
        ArgumentNullException.ThrowIfNull(asapEvent);

        foreach (var handler in ResolveHandlers<TEvent>())
        {
            // Every subscriber runs even after one has objected, so the user is told every reason
            // the operation was refused at once rather than discovering them one attempt at a time.
            await handler.HandleAsync(asapEvent, cancellationToken).ConfigureAwait(false);
        }

        if (asapEvent.Objections.Count == 0)
        {
            return Result.Success();
        }

        return asapEvent.IsVetoed
            ? Result.Failure(asapEvent.Objections)
            : Result.Success(asapEvent.Objections);
    }

    /// <inheritdoc />
    public void Enqueue<TEvent>(TEvent asapEvent)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(asapEvent);

        var type = asapEvent.GetType();

        outbox.Add(new OutboxMessage
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.CompanyId,
            BranchId = tenantContext.BranchId,
            EventName = asapEvent.EventName,

            // Namespace-qualified type plus assembly name, without version or public key. A
            // message sitting in the outbox across a patch release must still deserialise
            // afterwards, and pinning the assembly version would break exactly that case.
            EventType = $"{type.FullName}, {type.Assembly.GetName().Name}",
            Payload = JsonSerializer.Serialize(asapEvent, type, SerializerOptions),
            OccurredAtUtc = asapEvent.OccurredAtUtc == default ? clock.UtcNow : asapEvent.OccurredAtUtc,
        });
    }

    /// <summary>
    /// Resolves the handlers for an event, in the order they asked to run.
    /// </summary>
    /// <remarks>
    /// Core handlers sit at order 0, so an extension needing to run before core uses a negative
    /// value and one needing to run after uses a positive one. Ties keep registration order,
    /// which for handlers within one module is the order they were written in.
    /// </remarks>
    private List<IEventHandler<TEvent>> ResolveHandlers<TEvent>()
        where TEvent : IAsapEvent
    {
        var handlers = services.GetServices<IEventHandler<TEvent>>().ToList();

        if (handlers.Count > 1)
        {
            handlers = [.. handlers.OrderBy(static h => h.Order)];
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Publishing {EventType} to {HandlerCount} handler(s).",
                typeof(TEvent).Name,
                handlers.Count);
        }

        return handlers;
    }
}
