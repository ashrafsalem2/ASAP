using ASAP.Platform.Core.Cqrs;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ASAP.Api.Infrastructure;

/// <summary>
/// Turns an <see cref="AsapMessageException"/> into a problem response, and anything else into a
/// deliberately uninformative 500.
/// </summary>
/// <param name="logger">Records the failure with its full detail, server-side.</param>
/// <param name="environment">Decides how much detail an unexpected fault may reveal.</param>
public sealed class AsapExceptionHandler(
    ILogger<AsapExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        ProblemDetails problem;

        switch (exception)
        {
            case AsapMessageException asap:
            {
                var status = asap.IsPermissionFailure
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status422UnprocessableEntity;

                // Expected, not exceptional: a refusal ASAP raised on purpose. Logged at
                // information so a wall of permission denials does not read as a system fault,
                // while still leaving a trail of who was refused what.
                logger.LogInformation(
                    "Refused {Path}: {Code} - {Title}",
                    httpContext.Request.Path,
                    asap.AsapMessage.Code.Value,
                    asap.AsapMessage.Title);

                problem = AsapProblem.From(asap.AsapMessage, status, httpContext.Request.Path);
                break;
            }

            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
            {
                // The caller hung up. Nothing failed, and there is nobody left to answer.
                return true;
            }

            default:
            {
                logger.LogError(
                    exception,
                    "Unhandled failure on {Method} {Path}",
                    httpContext.Request.Method,
                    httpContext.Request.Path);

                problem = new ProblemDetails
                {
                    Type = "https://asap-erp.com/problems/unexpected",
                    Title = "Something went wrong",
                    Status = StatusCodes.Status500InternalServerError,
                    Instance = httpContext.Request.Path,

                    // An exception message can carry a connection string, a file path or a row of
                    // customer data. In production the client gets a correlation id and the
                    // detail stays in the log where support can find it.
                    Detail = environment.IsDevelopment()
                        ? exception.ToString()
                        : "The problem has been logged. Quote the trace id below when reporting it.",
                };

                problem.Extensions["traceId"] = httpContext.TraceIdentifier;
                break;
            }
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        await httpContext.Response
            .WriteAsJsonAsync(problem, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}
