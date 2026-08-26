using System.Collections.Concurrent;
using ASAP.Platform.Kernel.Cqrs;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Platform.Core.Cqrs;

/// <summary>
/// Sends a request through its pipeline to its handler.
/// </summary>
/// <remarks>
/// The pipeline is assembled outermost-first by <see cref="IPipelineBehavior{TRequest,TResponse}.Order"/>,
/// so a low-ordered behaviour wraps everything after it. Permission checking sits early and cheap;
/// opening a transaction sits later, so no transaction is started for a request that was about to
/// be refused anyway.
/// </remarks>
/// <param name="services">Resolves handlers and behaviours.</param>
public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    // Building the closed generic executor for a request type costs reflection, and an ERP sends
    // the same handful of request types many thousands of times a day. Cached per request and
    // response type pair, which is fixed for the process lifetime.
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> Executors = new();

    /// <inheritdoc />
    public Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executor = (Executor<TResponse>)Executors.GetOrAdd(
            (request.GetType(), typeof(TResponse)),
            static key => Activator.CreateInstance(
                typeof(Executor<,>).MakeGenericType(key.Request, key.Response))!);

        return executor.ExecuteAsync(services, request, cancellationToken);
    }

    /// <summary>Erases the request type so the dispatcher can hold executors of mixed shapes.</summary>
    private abstract class Executor<TResponse>
    {
        public abstract Task<TResponse> ExecuteAsync(
            IServiceProvider services,
            IRequest<TResponse> request,
            CancellationToken cancellationToken);
    }

    private sealed class Executor<TRequest, TResponse> : Executor<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> ExecuteAsync(
            IServiceProvider services,
            IRequest<TResponse> request,
            CancellationToken cancellationToken)
        {
            var typed = (TRequest)request;

            var handler = services.GetService<IRequestHandler<TRequest, TResponse>>()
                ?? throw new InvalidOperationException(
                    $"No handler is registered for {typeof(TRequest).Name}. "
                    + "Register it in the ConfigureServices of the module that owns the request.");

            var behaviors = services
                .GetServices<IPipelineBehavior<TRequest, TResponse>>()
                .OrderBy(static b => b.Order)
                .ToList();

            Func<Task<TResponse>> next = () => handler.HandleAsync(typed, cancellationToken);

            // Wrap from the inside out, so the lowest-ordered behaviour ends up outermost and
            // therefore runs first.
            for (var i = behaviors.Count - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var inner = next;
                next = () => behavior.HandleAsync(typed, inner, cancellationToken);
            }

            return next();
        }
    }
}
