namespace ASAP.Platform.Kernel.Sync;

/// <summary>Which way a kind of data travels between head office and a branch.</summary>
/// <remarks>
/// There is no third value on purpose. Data that could travel both ways would have two writers,
/// and two writers is the problem branch synchronisation exists to avoid rather than to solve.
/// See docs/architecture/branch-synchronisation.md.
/// </remarks>
public enum SyncDirection
{
    /// <summary>
    /// Written at head office, copied to every branch. Items, prices, customers, tax codes.
    /// </summary>
    Down = 0,

    /// <summary>
    /// Written at a branch, pushed to head office. Receipts, sessions, stock movements.
    /// </summary>
    Up = 1,
}

/// <summary>What happened to a row.</summary>
public enum SyncOperation
{
    /// <summary>
    /// It exists and here is what it looks like. Covers both insert and update, because a branch
    /// applying a change has no use for the difference and every use for idempotency.
    /// </summary>
    Upsert = 0,

    /// <summary>It is gone, or has been soft-deleted, and the branch should stop offering it.</summary>
    Delete = 1,
}

/// <summary>
/// One kind of thing a module wants synchronised.
/// </summary>
/// <param name="EntityType">
/// The stable name a change is published under, for example <c>Inventory.Item</c>. Deliberately a
/// name rather than a CLR type: it travels between two deployments that may be a version apart,
/// and a branch that could not read a feed because a class had been moved to another namespace
/// would be a branch that stops selling.
/// </param>
/// <param name="ClrType">The type as this deployment knows it, used to recognise saved changes.</param>
/// <param name="Direction">Which way it travels.</param>
/// <param name="Module">The module that owns it, for reporting.</param>
/// <param name="VolatileProperties">
/// Columns whose changing does not mean the row changed, as far as a branch is concerned.
/// <para>
/// Running balances and on-hand quantities live on master data for the convenience of screens,
/// and they move on every posting. Without this, a busy day publishes thousands of feed entries
/// for accounts and items whose definition nobody touched, and a branch spends its evening
/// applying its own trading back to itself.
/// </para>
/// <para>
/// A row is still published when one of these changes alongside something that matters. The rule
/// is only that they cannot be the whole reason.
/// </para>
/// </param>
public sealed record SyncEntityDescriptor(
    string EntityType,
    Type ClrType,
    SyncDirection Direction,
    string Module,
    IReadOnlyCollection<string>? VolatileProperties = null);

/// <summary>
/// Declared by a module that has something to synchronise.
/// </summary>
/// <remarks>
/// Optional. A module that says nothing here synchronises nothing, which is the right default:
/// silently replicating an entity nobody thought about is how a branch comes to hold a copy of
/// the audit log.
/// </remarks>
public interface ISyncContributor
{
    /// <summary>What this module publishes, and which way.</summary>
    IReadOnlyCollection<SyncEntityDescriptor> SyncEntities { get; }
}

/// <summary>One change, as a branch reads it off the feed.</summary>
/// <param name="Sequence">Its place in the order. The cursor a branch keeps.</param>
/// <param name="EntityType">What kind of thing changed.</param>
/// <param name="EntityId">Which one.</param>
/// <param name="DisplayNo">What a person would call it, for reporting.</param>
/// <param name="Operation">Whether it exists or is gone.</param>
/// <param name="OccurredAtUtc">When it changed at head office.</param>
public readonly record struct SyncChangeView(
    long Sequence,
    string EntityType,
    Guid EntityId,
    string? DisplayNo,
    SyncOperation Operation,
    DateTime OccurredAtUtc);

/// <summary>A page of the feed, with where to ask from next.</summary>
/// <param name="Changes">What changed, in order.</param>
/// <param name="Cursor">
/// The sequence to ask from next time. Returned even when the page is empty, so a branch that is
/// up to date still learns where the feed has got to.
/// </param>
/// <param name="HasMore">Whether asking again immediately would return more.</param>
public readonly record struct SyncPage(
    IReadOnlyList<SyncChangeView> Changes,
    long Cursor,
    bool HasMore);
