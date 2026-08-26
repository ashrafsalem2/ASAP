using System.Collections.Immutable;
using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Platform.Kernel.Results;

/// <summary>
/// The outcome of an ASAP operation, carrying every message it produced.
/// </summary>
/// <remarks>
/// <para>
/// ASAP returns results rather than throwing for business outcomes. A journal that will not
/// balance, an offer that would sell below cost, a transfer that would drive stock negative:
/// none of these are exceptional, they are the system doing its job, and each needs to reach
/// the user as a message they can act on. Exceptions stay reserved for genuine faults such as
/// a dropped database connection.
/// </para>
/// <para>
/// A successful result can still carry warnings and information. Posting that succeeds while
/// flagging that an item has gone below its reorder point is one result with two messages, not
/// a failure.
/// </para>
/// </remarks>
public class Result
{
    private static readonly ImmutableArray<AsapMessage> NoMessages = [];

    /// <summary>Creates a result from a set of messages, deciding success from their severities.</summary>
    /// <param name="messages">Every message the operation produced, in the order it produced them.</param>
    protected Result(ImmutableArray<AsapMessage> messages)
    {
        Messages = messages;
    }

    /// <summary>
    /// Every message the operation produced, successes and warnings included.
    /// </summary>
    public ImmutableArray<AsapMessage> Messages { get; }

    /// <summary>
    /// True when nothing stopped the operation. Derived from the messages rather than stored
    /// separately, so a result cannot claim success while carrying an error.
    /// </summary>
    public bool Succeeded => !Messages.Any(static m => m.IsFailure);

    /// <summary>True when at least one message stopped the operation.</summary>
    public bool Failed => !Succeeded;

    /// <summary>Only the messages that caused the failure.</summary>
    public IEnumerable<AsapMessage> Failures => Messages.Where(static m => m.IsFailure);

    /// <summary>
    /// True when the operation was refused only by blocks that some user could override, and
    /// by nothing else. The client uses this to offer a "request approval" path instead of a
    /// plain rejection.
    /// </summary>
    public bool IsFullyOverridable
    {
        get
        {
            var failures = Failures.ToList();
            return failures.Count > 0 && failures.TrueForAll(static m => m.IsOverridable);
        }
    }

    /// <summary>An operation that succeeded with nothing to report.</summary>
    public static Result Success() => new(NoMessages);

    /// <summary>An operation that succeeded, carrying informational or warning messages.</summary>
    /// <param name="messages">Messages to report. Must not contain failures.</param>
    /// <exception cref="ArgumentException">One of the messages is an error or a block.</exception>
    public static Result Success(params IEnumerable<AsapMessage> messages)
    {
        var collected = messages.ToImmutableArray();

        if (collected.Any(static m => m.IsFailure))
        {
            throw new ArgumentException(
                "Result.Success was given a failing message. Use Result.Failure instead.",
                nameof(messages));
        }

        return new Result(collected);
    }

    /// <summary>An operation that was stopped by the given messages.</summary>
    /// <param name="messages">The messages, at least one of which must be a failure.</param>
    /// <exception cref="ArgumentException">None of the messages is an error or a block.</exception>
    public static Result Failure(params IEnumerable<AsapMessage> messages)
    {
        var collected = messages.ToImmutableArray();

        if (!collected.Any(static m => m.IsFailure))
        {
            throw new ArgumentException(
                "Result.Failure needs at least one message of severity Error or Blocked.",
                nameof(messages));
        }

        return new Result(collected);
    }

    /// <summary>
    /// Merges several results into one, concatenating their messages. Used by the posting
    /// engine, which runs every validation and reports all the problems at once rather than
    /// making the user fix them one round-trip at a time.
    /// </summary>
    /// <param name="results">The results to merge.</param>
    public static Result Merge(params IEnumerable<Result> results)
        => new([.. results.SelectMany(static r => r.Messages)]);

    /// <inheritdoc />
    public override string ToString()
        => Succeeded
            ? $"Succeeded ({Messages.Length} message(s))"
            : $"Failed: {string.Join("; ", Failures.Select(static m => m.Code.Value))}";
}
