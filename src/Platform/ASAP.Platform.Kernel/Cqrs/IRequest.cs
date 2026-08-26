using ASAP.Platform.Kernel.Results;

namespace ASAP.Platform.Kernel.Cqrs;

/// <summary>
/// Something the application layer can be asked to do, producing a response.
/// </summary>
/// <typeparam name="TResponse">What answering the request produces.</typeparam>
/// <remarks>
/// Routing every operation through one request type is what gives ASAP a single place to hang
/// its cross-cutting concerns. Permission checks, validation, transactions, audit logging and
/// the event outbox are all pipeline behaviours, so no handler has to remember them and no
/// module can accidentally skip one.
/// </remarks>
public interface IRequest<out TResponse>;

/// <summary>
/// A request that changes something and reports its outcome as a <see cref="Result"/>.
/// </summary>
/// <remarks>
/// Commands run inside a transaction opened by the pipeline. Returning a failed result rolls
/// it back, so a handler that finds a problem halfway through does not have to unwind by hand.
/// </remarks>
public interface ICommand : IRequest<Result>;

/// <summary>
/// A request that changes something and produces a value, such as a document number.
/// </summary>
/// <typeparam name="TValue">What the command produces on success.</typeparam>
public interface ICommand<TValue> : IRequest<Result<TValue>>;

/// <summary>
/// A request that reads without changing anything. Runs outside a write transaction and against
/// a no-tracking context.
/// </summary>
/// <typeparam name="TResponse">What the query returns.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>;
