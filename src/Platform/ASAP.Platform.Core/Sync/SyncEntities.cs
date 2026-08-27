using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Sync;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Sync;

/// <summary>
/// One change to something a branch holds a copy of.
/// </summary>
/// <remarks>
/// <para>
/// Written by the context on save rather than by the module that made the change. A module that
/// had to remember to publish would one day forget, and the failure mode is a branch quietly
/// running on last month's prices — which nobody notices until a customer is charged the wrong
/// amount and is right about it.
/// </para>
/// <para>
/// The payload is not stored. A branch asks for the rows by key once it knows they changed, which
/// keeps the feed small, keeps one copy of the truth, and means a change captured before a column
/// was added still carries that column when it is finally read.
/// </para>
/// </remarks>
public sealed class SyncChange : Entity, ITenantScoped
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>The company it belongs to.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>
    /// Its place in the order.
    /// </summary>
    /// <remarks>
    /// Database-generated and monotonic. This is the cursor a branch keeps, so it has to be
    /// assigned by the one thing that can order concurrent writers against each other.
    /// </remarks>
    public long Sequence { get; set; }

    /// <summary>What kind of thing changed, as the stable published name.</summary>
    public required string EntityType { get; set; }

    /// <summary>Which one.</summary>
    public Guid EntityId { get; set; }

    /// <summary>What a person would call it, so a sync report reads.</summary>
    public string? DisplayNo { get; set; }

    /// <summary>Whether it exists or is gone.</summary>
    public SyncOperation Operation { get; set; }

    /// <summary>When it changed at head office.</summary>
    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>
/// How far one branch has got.
/// </summary>
/// <remarks>
/// Kept at head office as well as at the branch. The branch's own cursor is what it asks from;
/// this copy is what lets somebody at head office answer "which shops are behind, and by how
/// much" without telephoning them.
/// </remarks>
public sealed class BranchSyncState : Entity, ITenantScoped
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>The company.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>The branch this describes.</summary>
    public Guid BranchId { get; set; }

    /// <summary>The last sequence the branch confirmed it had applied.</summary>
    public long LastAppliedSequence { get; set; }

    /// <summary>When it last asked for changes.</summary>
    public DateTime? LastPulledAtUtc { get; set; }

    /// <summary>When it last pushed a document that was accepted.</summary>
    public DateTime? LastPushedAtUtc { get; set; }

    /// <summary>How many documents it has pushed in total.</summary>
    public int DocumentsPushed { get; set; }
}

/// <summary>
/// A document a branch has pushed, recorded so a replay is not posted twice.
/// </summary>
/// <remarks>
/// Keyed by what the caller supplied rather than by the document number, because the two answer
/// different questions. A document number says which document this is; an idempotency key says
/// which attempt this is, and that is what lets a till whose connection dropped mid-post retry
/// without anybody having to work out whether the first attempt landed.
/// </remarks>
public sealed class SyncInboxEntry : Entity, ITenantScoped
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>The company.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>The branch that pushed it.</summary>
    public Guid BranchId { get; set; }

    /// <summary>What the caller called this attempt. Unique per branch.</summary>
    public required string IdempotencyKey { get; set; }

    /// <summary>What kind of document it is, for example <c>Pos.Receipt</c>.</summary>
    public required string DocumentType { get; set; }

    /// <summary>The document number it produced, once it had one.</summary>
    public string? DocumentNo { get; set; }

    /// <summary>When head office accepted it.</summary>
    public DateTime AcceptedAtUtc { get; set; }

    /// <summary>
    /// Whether the document could be applied, or is waiting on master data the branch has and
    /// head office has not seen yet.
    /// </summary>
    public bool IsApplied { get; set; }

    /// <summary>Why it is waiting, when it is.</summary>
    public string? HeldReason { get; set; }
}
