using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Core.Numbering;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Platform.Persistence;

/// <summary>
/// Issues document numbers from the series an administrator defined.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of the series is that numbering policy belongs to the administrator rather than
/// to whichever module happens to be posting. Modules ask for a number; where it starts, how wide
/// the counter is, whether it may have gaps and whether it restarts each January are all settled
/// here, once.
/// </para>
/// <para>
/// <b>Gapless allocation.</b> A series with <see cref="NumberSeries.AllowGaps"/> off takes its
/// number under a row lock inside the caller's transaction, so abandoning the post hands the
/// number back and the sequence stays unbroken -- which is what a tax authority requires of an
/// invoice series. A gap-tolerant series does the same work without the lock, which is faster
/// under load and simply leaves a hole when a document is abandoned. Both paths advance the same
/// row; the difference is only whether concurrent callers queue for it. The lock depends on the
/// caller being inside a transaction -- see the remarks on the locking method.
/// </para>
/// <para>
/// Nothing here saves. The advanced counter is written when the caller saves, which is what makes
/// the gapless promise true: the number and the document it went on commit or roll back together.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="tenantContext">Supplies the company and branch asking.</param>
/// <param name="messages">Renders refusals.</param>
public sealed class NumberSeriesService(
    AsapDbContext context,
    ITenantContext tenantContext,
    IMessageCatalog messages)
    : INumberSeriesService
{
    /// <inheritdoc />
    public Task<Result<string>> NextAsync(
        string seriesCode,
        DateOnly documentDate,
        CancellationToken cancellationToken = default)
        => AllocateAsync(seriesCode, documentDate, take: true, cancellationToken);

    /// <inheritdoc />
    public Task<Result<string>> PeekAsync(
        string seriesCode,
        DateOnly documentDate,
        CancellationToken cancellationToken = default)
        => AllocateAsync(seriesCode, documentDate, take: false, cancellationToken);

    /// <inheritdoc />
    public async Task<Result> ValidateManualAsync(
        string seriesCode,
        string number,
        DateOnly documentDate,
        CancellationToken cancellationToken = default)
    {
        var found = await FindAsync(seriesCode, documentDate, cancellationToken).ConfigureAwait(false);

        if (found.Failed)
        {
            return found;
        }

        var (series, line) = found.Value;

        var arguments = Arguments(seriesCode, documentDate);
        arguments["Number"] = number;

        if (!series.AllowManualEntry)
        {
            return Result.Failure(messages.Render(PlatformMessages.NumberSeriesManualNotAllowed, arguments));
        }

        // Only what the series can actually know. It records the last number it issued, not which
        // documents exist, so "already used" here means "at or behind the counter" -- said plainly
        // rather than dressed up as a uniqueness check it cannot perform.
        if (line.LastNumberUsed is { } last
            && DocumentNumberFormatter.TryReadCounter(last, out var issued)
            && DocumentNumberFormatter.TryReadCounter(number, out var typed)
            && typed <= issued
            && string.Equals(
                DocumentNumberFormatter.ReadPrefix(last),
                DocumentNumberFormatter.ReadPrefix(number),
                StringComparison.OrdinalIgnoreCase))
        {
            arguments["LastNumber"] = last;
            return Result.Failure(messages.Render(PlatformMessages.NumberSeriesNumberInUse, arguments));
        }

        return Result.Success();
    }

    private async Task<Result<string>> AllocateAsync(
        string seriesCode,
        DateOnly documentDate,
        bool take,
        CancellationToken cancellationToken)
    {
        var found = await FindAsync(seriesCode, documentDate, cancellationToken).ConfigureAwait(false);

        if (found.Failed)
        {
            return Result<string>.FailureFrom(found);
        }

        var (series, line) = found.Value;
        var arguments = Arguments(seriesCode, documentDate);

        if (take && !series.AllowGaps)
        {
            await LockAsync(line, cancellationToken).ConfigureAwait(false);
        }

        if (series.EnforceDateOrder && line.LastDateUsed is { } lastDate && documentDate < lastDate)
        {
            arguments["LastDate"] = lastDate;
            return Result<string>.Failure(messages.Render(PlatformMessages.NumberSeriesDateOrder, arguments));
        }

        var next = NextNumber(line, documentDate);

        if (next is null)
        {
            arguments["LastNumber"] = line.LastNumberUsed ?? line.StartingNumber;
            return Result<string>.Failure(messages.Render(PlatformMessages.NumberSeriesExhausted, arguments));
        }

        // Past the ceiling the range was registered with. Refused rather than issued, because a
        // pre-printed or pre-declared range that runs over is a problem outside ASAP.
        if (line.EndingNumber is { } ending
            && DocumentNumberFormatter.TryReadCounter(ending, out var ceiling)
            && DocumentNumberFormatter.TryReadCounter(next, out var candidate)
            && candidate > ceiling)
        {
            arguments["LastNumber"] = line.LastNumberUsed ?? ending;
            return Result<string>.Failure(messages.Render(PlatformMessages.NumberSeriesExhausted, arguments));
        }

        if (!take)
        {
            return Result<string>.Success(next);
        }

        line.LastNumberUsed = next;
        line.LastDateUsed = documentDate;

        var warnings = new List<AsapMessage>();

        if (line.WarnWhenRemainingBelow is { } threshold
            && line.Remaining() is { } remaining
            && remaining <= threshold)
        {
            arguments["Remaining"] = remaining;
            warnings.Add(messages.Render(PlatformMessages.NumberSeriesRunningLow, arguments));
        }

        return Result<string>.Success(next, warnings);
    }

    /// <summary>
    /// Takes an exclusive lock on the line, so a gapless series issues to one caller at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Held until the caller's transaction ends, which is what serialises two tills posting at the
    /// same instant: the second waits, reads the advanced counter, and takes the number after it.
    /// Without this both would read the same last number and both claim the next one.
    /// </para>
    /// <para>
    /// The lock is only as good as the transaction around it. Called outside one, SQL Server
    /// releases it as the statement ends and a gapless series behaves like a gap-tolerant one --
    /// so anything issuing statutory numbers must post through the dispatcher, which wraps every
    /// command in a transaction.
    /// </para>
    /// <para>
    /// Skipped on a non-relational provider, which is only ever the in-memory one used by tests.
    /// </para>
    /// </remarks>
    private async Task LockAsync(NumberSeriesLine line, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
        {
            return;
        }

        await context.Database
            .ExecuteSqlAsync(
                $@"SELECT TOP 1 Id FROM asap.NumberSeriesLines WITH (UPDLOCK, HOLDLOCK)
                   WHERE Id = {line.Id}",
                cancellationToken)
            .ConfigureAwait(false);

        // Re-read what the lock now protects. The tracked copy was loaded before the wait, so
        // advancing it would hand out a number another caller has already taken.
        await context.Entry(line).ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Works out the next number for a line, from what it last issued or from where it starts.
    /// </summary>
    private static string? NextNumber(NumberSeriesLine line, DateOnly documentDate)
    {
        // A line that has issued nothing yet gives out its starting number rather than advancing
        // past it, or every series would silently skip its own first number.
        if (line.LastNumberUsed is null)
        {
            return DocumentNumberFormatter.ApplyDate(line.StartingNumber, documentDate);
        }

        return DocumentNumberFormatter.TryAdvance(line.LastNumberUsed, Math.Max(1, line.Increment), out var next)
            ? next
            : null;
    }

    private async Task<Result<(NumberSeries Series, NumberSeriesLine Line)>> FindAsync(
        string seriesCode,
        DateOnly documentDate,
        CancellationToken cancellationToken)
    {
        var arguments = Arguments(seriesCode, documentDate);

        var series = await context.Set<NumberSeries>()
            .Include(s => s.Lines)
            .Where(s => s.Code == seriesCode && s.IsActive)

            // A branch series wins over the company-wide one of the same code, so point-of-sale
            // receipts number per till without every caller having to know that.
            .OrderByDescending(s => s.BranchId == tenantContext.BranchId)
            .FirstOrDefaultAsync(
                s => s.BranchId == null || s.BranchId == tenantContext.BranchId,
                cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            return Result<(NumberSeries, NumberSeriesLine)>.Failure(
                messages.Render(PlatformMessages.NumberSeriesUnavailable, arguments));
        }

        // The latest line that has started. Dating lines is how a series restarts each January
        // without losing what the previous year issued.
        var line = series.Lines
            .Where(l => l.IsOpen && l.StartingDate <= documentDate)
            .OrderByDescending(l => l.StartingDate)
            .FirstOrDefault();

        if (line is null)
        {
            return Result<(NumberSeries, NumberSeriesLine)>.Failure(
                messages.Render(PlatformMessages.NumberSeriesNoLine, arguments));
        }

        return Result<(NumberSeries, NumberSeriesLine)>.Success((series, line));
    }

    private static Dictionary<string, object?> Arguments(string seriesCode, DateOnly documentDate)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Series"] = seriesCode,
            ["DocumentDate"] = documentDate,
        };
}
