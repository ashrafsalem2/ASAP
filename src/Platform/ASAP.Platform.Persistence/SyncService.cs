using ASAP.Platform.Core.Sync;
using ASAP.Platform.Kernel.Sync;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Platform.Persistence;

/// <summary>How far one branch has got, as head office reads it.</summary>
/// <param name="BranchId">The branch.</param>
/// <param name="LastAppliedSequence">The last change it confirmed applying.</param>
/// <param name="Behind">How many changes it has not applied yet.</param>
/// <param name="LastPulledAtUtc">When it last asked.</param>
/// <param name="LastPushedAtUtc">When it last pushed something accepted.</param>
/// <param name="DocumentsPushed">How many documents it has pushed in total.</param>
public readonly record struct BranchSyncStatus(
    Guid BranchId,
    long LastAppliedSequence,
    int Behind,
    DateTime? LastPulledAtUtc,
    DateTime? LastPushedAtUtc,
    int DocumentsPushed);

/// <summary>What became of a pushed document.</summary>
/// <param name="IdempotencyKey">What the caller called this attempt.</param>
/// <param name="Accepted">Whether head office took it. False only when it was already held.</param>
/// <param name="WasReplay">
/// True when this key had already been seen. The caller gets the original outcome rather than a
/// refusal, because a retry after a lost response is the case this exists for.
/// </param>
/// <param name="DocumentNo">The document number it produced, if it has one.</param>
/// <param name="HeldReason">Why it is waiting, when it is.</param>
public readonly record struct SyncPushResult(
    string IdempotencyKey,
    bool Accepted,
    bool WasReplay,
    string? DocumentNo,
    string? HeldReason);

/// <summary>
/// Serves the change feed to branches and takes documents back from them.
/// </summary>
/// <remarks>
/// <para>
/// Every row has exactly one writer: master data is written at head office and copied down,
/// transactions are written at a branch and pushed up. That asymmetry is the design, not a
/// convention — see docs/architecture/branch-synchronisation.md — and it is why there is no
/// merge strategy anywhere in this file.
/// </para>
/// <para>
/// Nothing here decides anything about a pushed document. The sale happened, the money was taken,
/// the stock left a shelf in another city. Head office records it, once.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="tenantContext">Supplies the company.</param>
/// <param name="clock">Supplies the time.</param>
/// <param name="logger">Records what branches pulled and pushed.</param>
public sealed class SyncService(
    AsapDbContext context,
    ITenantContext tenantContext,
    IClock clock,
    ILogger<SyncService> logger)
{
    /// <summary>The largest page a branch can ask for, however much it asks for.</summary>
    /// <remarks>
    /// A branch that has been off for a month has thousands of changes waiting, and one response
    /// carrying all of them is a response that times out and is retried, forever. It asks again.
    /// </remarks>
    public const int MaxPageSize = 500;

    /// <summary>
    /// Everything that changed after the cursor, in order.
    /// </summary>
    /// <param name="since">The last sequence the branch applied. Zero asks for everything.</param>
    /// <param name="pageSize">How many at most, capped at <see cref="MaxPageSize"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The page, and where to ask from next.</returns>
    public async Task<SyncPage> PullAsync(
        long since,
        int pageSize = MaxPageSize,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, MaxPageSize);

        var changes = await context.SyncChanges
            .AsNoTracking()
            .Where(c => c.Sequence > since)
            .OrderBy(static c => c.Sequence)

            // One more than asked for, so "is there more" is answered by the same query rather
            // than by a second count over a table that is still being written to.
            .Take(take + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = changes.Count > take;
        var page = hasMore ? changes.Take(take).ToList() : changes;

        // The cursor moves to the last row actually returned, never past it. A branch that
        // crashes between reading and applying asks for the same page again, which is exactly
        // what should happen.
        var cursor = page.Count > 0 ? page[^1].Sequence : since;

        return new SyncPage(
            [.. page.Select(static c => new SyncChangeView(
                c.Sequence,
                c.EntityType,
                c.EntityId,
                c.DisplayNo,
                c.Operation,
                c.OccurredAtUtc))],
            cursor,
            hasMore);
    }

    /// <summary>
    /// Records that a branch has applied everything up to a sequence.
    /// </summary>
    /// <remarks>
    /// Separate from the pull on purpose. A branch that acknowledged by asking would lose a page
    /// every time it crashed between the two, and the loss would be silent.
    /// </remarks>
    /// <param name="branchId">The branch.</param>
    /// <param name="sequence">What it has applied up to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Where the branch now stands.</returns>
    public async Task<BranchSyncStatus> AcknowledgeAsync(
        Guid branchId,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        var state = await StateAsync(branchId, cancellationToken).ConfigureAwait(false);

        // Never backwards. A late reply from an earlier pull would otherwise make a branch ask
        // again for changes it has already applied, which is harmless but looks like a fault.
        state.LastAppliedSequence = Math.Max(state.LastAppliedSequence, sequence);
        state.LastPulledAtUtc = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await StatusAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes a document from a branch, once.
    /// </summary>
    /// <remarks>
    /// The key is supplied by the caller and is what makes a retry safe. A push carrying a key
    /// already in the inbox returns the original outcome rather than posting again — the same
    /// answer the caller would have had if the first response had not been lost, which is the
    /// whole point of the mechanism.
    /// </remarks>
    /// <param name="branchId">The branch pushing.</param>
    /// <param name="idempotencyKey">What the caller calls this attempt.</param>
    /// <param name="documentType">What kind of document it is.</param>
    /// <param name="documentNo">The number it carries, when it has one.</param>
    /// <param name="heldReason">Why it cannot be applied yet, when it cannot.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What became of it.</returns>
    public async Task<SyncPushResult> PushAsync(
        Guid branchId,
        string idempotencyKey,
        string documentType,
        string? documentNo = null,
        string? heldReason = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.SyncInbox
            .FirstOrDefaultAsync(
                e => e.BranchId == branchId && e.IdempotencyKey == idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            logger.LogInformation(
                "Branch {BranchId} pushed {IdempotencyKey} again; returning what happened the "
                + "first time.",
                branchId,
                idempotencyKey);

            return new SyncPushResult(
                existing.IdempotencyKey,
                Accepted: true,
                WasReplay: true,
                existing.DocumentNo,
                existing.HeldReason);
        }

        var entry = new SyncInboxEntry
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.CompanyId,
            BranchId = branchId,
            IdempotencyKey = idempotencyKey,
            DocumentType = documentType,
            DocumentNo = documentNo,
            AcceptedAtUtc = clock.UtcNow,
            IsApplied = heldReason is null,
            HeldReason = heldReason,
        };

        context.SyncInbox.Add(entry);

        var state = await StateAsync(branchId, cancellationToken).ConfigureAwait(false);

        state.LastPushedAtUtc = entry.AcceptedAtUtc;
        state.DocumentsPushed++;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Branch {BranchId} pushed {DocumentType} {DocumentNo} as {IdempotencyKey}.",
            branchId,
            documentType,
            documentNo,
            idempotencyKey);

        return new SyncPushResult(
            idempotencyKey,
            Accepted: true,
            WasReplay: false,
            documentNo,
            heldReason);
    }

    /// <summary>
    /// Which shops are behind, and by how much.
    /// </summary>
    /// <remarks>
    /// Answerable at head office without telephoning anybody, which is the reason a copy of each
    /// branch's cursor is kept here as well as at the branch that owns it.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Every branch that has ever synchronised, furthest behind first.</returns>
    public async Task<IReadOnlyList<BranchSyncStatus>> StatusAsync(
        CancellationToken cancellationToken = default)
    {
        var head = await HeadAsync(cancellationToken).ConfigureAwait(false);

        var states = await context.BranchSyncState
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. states
                .Select(s => new BranchSyncStatus(
                    s.BranchId,
                    s.LastAppliedSequence,
                    Behind(head, s.LastAppliedSequence),
                    s.LastPulledAtUtc,
                    s.LastPushedAtUtc,
                    s.DocumentsPushed))
                .OrderByDescending(static s => s.Behind)
                .ThenBy(static s => s.BranchId),
        ];
    }

    /// <summary>The sequence the feed has reached.</summary>
    public async Task<long> HeadAsync(CancellationToken cancellationToken = default)
        => await context.SyncChanges
            .AsNoTracking()
            .OrderByDescending(static c => c.Sequence)
            .Select(static c => c.Sequence)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private static int Behind(long head, long applied)
        => head > applied ? (int)Math.Min(head - applied, int.MaxValue) : 0;

    private async Task<BranchSyncStatus> StatusAsync(
        BranchSyncState state,
        CancellationToken cancellationToken)
    {
        var head = await HeadAsync(cancellationToken).ConfigureAwait(false);

        return new BranchSyncStatus(
            state.BranchId,
            state.LastAppliedSequence,
            Behind(head, state.LastAppliedSequence),
            state.LastPulledAtUtc,
            state.LastPushedAtUtc,
            state.DocumentsPushed);
    }

    private async Task<BranchSyncState> StateAsync(Guid branchId, CancellationToken cancellationToken)
    {
        var state = await context.BranchSyncState
            .FirstOrDefaultAsync(s => s.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false);

        if (state is not null)
        {
            return state;
        }

        state = new BranchSyncState
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.CompanyId,
            BranchId = branchId,
        };

        context.BranchSyncState.Add(state);

        return state;
    }
}
