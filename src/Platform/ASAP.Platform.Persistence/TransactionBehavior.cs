using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Platform.Persistence;

/// <summary>
/// Wraps a command in a database transaction, and rolls it back when the command reports failure.
/// </summary>
/// <remarks>
/// <para>
/// Queries get no transaction. Only <see cref="ICommand"/> and <see cref="ICommand{TValue}"/> are
/// wrapped, which is why the marker interfaces exist as more than documentation.
/// </para>
/// <para>
/// The part that matters is the rollback on a <em>returned</em> failure. A posting routine that
/// writes several ledger entries and then finds the seventh unbalanced returns a failed result
/// rather than throwing; without this behaviour the first six would already be saved. Treating a
/// failed result exactly like an exception is what makes returning failures safe.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request being wrapped.</typeparam>
/// <typeparam name="TResponse">What answering it produces.</typeparam>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders a concurrency conflict into something a user can act on.</param>
/// <param name="logger">Records rollbacks.</param>
public sealed class TransactionBehavior<TRequest, TResponse>(
    AsapDbContext context,
    IMessageCatalog messages,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly bool IsCommand =
        typeof(ICommand).IsAssignableFrom(typeof(TRequest))
        || Array.Exists(
            typeof(TRequest).GetInterfaces(),
            static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

    /// <inheritdoc />
    /// <remarks>
    /// After the permission check at -1000, so nothing opens a transaction for a request that was
    /// about to be refused.
    /// </remarks>
    public int Order => -500;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!IsCommand)
        {
            return await next().ConfigureAwait(false);
        }

        // A handler that opened its own transaction -- a gapless number series allocation, say --
        // is already inside one. Joining it rather than nesting keeps the whole operation atomic.
        if (context.Database.CurrentTransaction is not null)
        {
            return await next().ConfigureAwait(false);
        }

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var response = await next().ConfigureAwait(false);

            if (response is Result { Failed: true } failure)
            {
                logger.LogInformation(
                    "{Request} was rolled back: {Codes}",
                    typeof(TRequest).Name,
                    string.Join(", ", failure.Failures.Select(static m => m.Code.Value)));

                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return response;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            // Two users editing one record is an ordinary Tuesday, not a fault. Turning it into
            // a message tells the second user what happened rather than showing them a stack trace.
            logger.LogInformation(ex, "{Request} hit a concurrency conflict.", typeof(TRequest).Name);

            throw new Core.Cqrs.AsapMessageException(messages.Render(
                PlatformMessages.ConcurrencyConflict,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["User"] = "Another user",
                }));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
