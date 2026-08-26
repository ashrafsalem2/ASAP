using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Platform.Kernel.Results;

/// <summary>
/// The outcome of an operation that produces a value when it succeeds, such as posting a
/// journal and getting back the ledger entry numbers.
/// </summary>
/// <typeparam name="TValue">What the operation produces.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue? value, ImmutableArray<AsapMessage> messages)
        : base(messages)
    {
        _value = value;
    }

    /// <summary>
    /// What the operation produced.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The operation failed, so there is no value. Reading it would mean the caller skipped
    /// checking <see cref="Result.Succeeded"/>, which is a bug worth surfacing immediately.
    /// </exception>
    public TValue Value => Succeeded
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read the value of a failed result. Failures: "
            + $"{string.Join("; ", Failures.Select(static m => m.Code.Value))}");

    /// <summary>
    /// Reads the value only when the operation succeeded, without risking an exception.
    /// </summary>
    /// <param name="value">The value, when the operation succeeded.</param>
    /// <returns>True when there is a value to read.</returns>
    public bool TryGetValue([NotNullWhen(true)] out TValue? value)
    {
        value = Succeeded ? _value : default;
        return Succeeded && value is not null;
    }

    /// <summary>An operation that produced a value with nothing to report.</summary>
    /// <param name="value">What the operation produced.</param>
    public static Result<TValue> Success(TValue value) => new(value, []);

    /// <summary>An operation that produced a value alongside informational or warning messages.</summary>
    /// <param name="value">What the operation produced.</param>
    /// <param name="messages">Messages to report. Must not contain failures.</param>
    /// <exception cref="ArgumentException">One of the messages is an error or a block.</exception>
    public static Result<TValue> Success(TValue value, params IEnumerable<AsapMessage> messages)
    {
        var collected = messages.ToImmutableArray();

        if (collected.Any(static m => m.IsFailure))
        {
            throw new ArgumentException(
                "Result.Success was given a failing message. Use Result.Failure instead.",
                nameof(messages));
        }

        return new Result<TValue>(value, collected);
    }

    /// <summary>An operation that was stopped, and so produced no value.</summary>
    /// <param name="messages">The messages, at least one of which must be a failure.</param>
    /// <exception cref="ArgumentException">None of the messages is an error or a block.</exception>
    public static new Result<TValue> Failure(params IEnumerable<AsapMessage> messages)
    {
        var collected = messages.ToImmutableArray();

        if (!collected.Any(static m => m.IsFailure))
        {
            throw new ArgumentException(
                "Result.Failure needs at least one message of severity Error or Blocked.",
                nameof(messages));
        }

        return new Result<TValue>(default, collected);
    }

    /// <summary>
    /// Carries the failure of an earlier step forward without re-describing it. Lets a pipeline
    /// abandon its work while keeping the original diagnosis intact.
    /// </summary>
    /// <param name="failed">The failed result to carry forward.</param>
    /// <exception cref="ArgumentException">The given result did not actually fail.</exception>
    public static Result<TValue> FailureFrom(Result failed)
    {
        if (failed.Succeeded)
        {
            throw new ArgumentException(
                "FailureFrom was given a successful result; there is no failure to carry forward.",
                nameof(failed));
        }

        return new Result<TValue>(default, failed.Messages);
    }

    /// <summary>
    /// Reshapes the value of a successful result, leaving a failed one untouched.
    /// </summary>
    /// <typeparam name="TNext">The new value type.</typeparam>
    /// <param name="map">How to reshape the value.</param>
    public Result<TNext> Map<TNext>(Func<TValue, TNext> map)
        => Succeeded
            ? Result<TNext>.Success(map(_value!), Messages)
            : Result<TNext>.FailureFrom(this);
}
