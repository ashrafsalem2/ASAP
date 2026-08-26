namespace ASAP.Platform.Kernel.Cqrs;

/// <summary>
/// Answers one kind of request. Exactly one handler is registered per request type.
/// </summary>
/// <typeparam name="TRequest">The request being answered.</typeparam>
/// <typeparam name="TResponse">What answering it produces.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Answers the request.
    /// </summary>
    /// <param name="request">What was asked.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <remarks>
    /// By the time a handler runs the pipeline has already confirmed the caller has permission,
    /// validated the request, and opened a transaction for a command. A handler is therefore
    /// free to concentrate on the business rule it exists to enforce.
    /// </remarks>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Wraps request handling to add behaviour that applies to many requests at once, such as
/// checking permissions or opening a transaction.
/// </summary>
/// <typeparam name="TRequest">The request being wrapped.</typeparam>
/// <typeparam name="TResponse">What answering it produces.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Runs around the rest of the pipeline.
    /// </summary>
    /// <param name="request">What was asked.</param>
    /// <param name="next">Continues to the next behaviour, and eventually to the handler.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The response, which a behaviour may replace. A permission check that fails returns a
    /// failed result without ever calling <paramref name="next"/>.
    /// </returns>
    Task<TResponse> HandleAsync(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken = default);

    /// <summary>Order this behaviour runs in, lowest first and outermost.</summary>
    int Order => 0;
}

/// <summary>
/// Sends a request into the pipeline and returns its response.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Sends a request through the pipeline to its handler.
    /// </summary>
    /// <typeparam name="TResponse">What the request produces.</typeparam>
    /// <param name="request">What is being asked.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="InvalidOperationException">
    /// No handler is registered for the request type, or more than one is.
    /// </exception>
    Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}
