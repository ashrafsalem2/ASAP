using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ASAP.Api.Infrastructure;

/// <summary>
/// Turns ASAP messages into HTTP problem responses without losing what makes them useful.
/// </summary>
/// <remarks>
/// <para>
/// A plain RFC 9457 problem carries a title and a detail string, which would throw away the
/// resolution, the override permission, the field at fault and the raw values. Those are the
/// parts a client needs to show the user something better than a red banner, so each is carried
/// as an extension member.
/// </para>
/// <para>
/// Every ASAP failure comes back in this shape, whether it was a business refusal, a permission
/// denial or a validation error, so the client has one thing to handle rather than three.
/// </para>
/// </remarks>
public static class AsapProblem
{
    /// <summary>The problem type ASAP uses for its own refusals.</summary>
    public const string TypeUri = "https://asap-erp.com/problems/message";

    /// <summary>
    /// Builds the refusal for a caller who lacks a permission.
    /// </summary>
    /// <param name="permission">The permission that was required.</param>
    /// <param name="doing">What they were trying to do, phrased to follow "permission to".</param>
    /// <param name="instance">The request path.</param>
    /// <returns>A 403 problem naming the permission needed.</returns>
    /// <remarks>
    /// Names the permission rather than saying "forbidden". The person reading it usually cannot
    /// grant it to themselves, and the first thing whoever can will ask is which one.
    /// </remarks>
    public static ProblemDetails Forbidden(string permission, string doing, string? instance = null)
        => new()
        {
            Type = TypeUri,
            Title = $"You do not have permission to {doing}",
            Detail = $"{permission} is required.",
            Status = StatusCodes.Status403Forbidden,
            Instance = instance,
        };

    /// <summary>
    /// Builds a problem response from one message.
    /// </summary>
    /// <param name="message">The refusal.</param>
    /// <param name="status">HTTP status to report it with.</param>
    /// <param name="instance">The request path.</param>
    public static ProblemDetails From(AsapMessage message, int status, string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        var problem = new ProblemDetails
        {
            Type = TypeUri,
            Title = message.Title,
            Detail = message.Detail,
            Status = status,
            Instance = instance,
        };

        Populate(problem, [message]);
        return problem;
    }

    /// <summary>
    /// Builds a problem response from a failed result, carrying every message it collected.
    /// </summary>
    /// <param name="result">The failed result.</param>
    /// <param name="status">HTTP status to report it with.</param>
    /// <param name="instance">The request path.</param>
    /// <exception cref="ArgumentException">The result did not actually fail.</exception>
    public static ProblemDetails From(Result result, int status, string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Succeeded)
        {
            throw new ArgumentException(
                "A successful result has no problem to report.",
                nameof(result));
        }

        var primary = result.Failures.First();

        var problem = new ProblemDetails
        {
            Type = TypeUri,
            Title = primary.Title,
            Detail = primary.Detail,
            Status = status,
            Instance = instance,
        };

        Populate(problem, result.Messages);
        return problem;
    }

    /// <summary>
    /// Chooses the status code that fits a set of messages.
    /// </summary>
    /// <remarks>
    /// 403 for a permission or licensing refusal, because the answer is to be granted something.
    /// 422 for everything else, because the request was well-formed and ASAP understood it
    /// perfectly -- it simply will not do it. A 400 would suggest the client sent something
    /// malformed and invite a developer to go looking for a syntax problem that is not there.
    /// </remarks>
    public static int StatusFor(IEnumerable<AsapMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var failures = messages.Where(static m => m.IsFailure).ToList();

        return failures.Exists(static m => m.Code.Value.StartsWith("SEC.", StringComparison.Ordinal))
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status422UnprocessableEntity;
    }

    private static void Populate(ProblemDetails problem, IReadOnlyCollection<AsapMessage> messages)
    {
        var primary = messages.FirstOrDefault(static m => m.IsFailure) ?? messages.First();

        problem.Extensions["code"] = primary.Code.Value;
        problem.Extensions["severity"] = primary.Severity.ToString();

        if (primary.Resolution is { } resolution)
        {
            problem.Extensions["resolution"] = resolution;
        }

        if (primary.OverridePermission is { } permission)
        {
            problem.Extensions["overridePermission"] = permission;
        }

        if (primary.HelpTopic is { } help)
        {
            problem.Extensions["helpTopic"] = help;
        }

        if (!primary.Target.IsEmpty)
        {
            problem.Extensions["target"] = new
            {
                field = primary.Target.Field,
                entityType = primary.Target.EntityType,
                entityId = primary.Target.EntityId,
                displayNo = primary.Target.DisplayNo,
            };
        }

        if (primary.Arguments.Count > 0)
        {
            // The raw values behind the rendered text, so the client can re-format them for its
            // own locale rather than parsing numbers back out of a sentence.
            problem.Extensions["arguments"] = primary.Arguments;
        }

        // Everything, including warnings that accompanied the failure. A posting refused for one
        // reason while also warning about two others should say all three at once.
        problem.Extensions["messages"] = MessagePayload.FromAll(messages);
    }
}
