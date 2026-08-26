using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Auditing;

/// <summary>What kind of thing was recorded.</summary>
public enum AuditAction
{
    /// <summary>A record was created.</summary>
    Created = 0,

    /// <summary>A record was changed.</summary>
    Updated = 1,

    /// <summary>A record was soft-deleted.</summary>
    Deleted = 2,

    /// <summary>A document was posted to the ledgers.</summary>
    Posted = 3,

    /// <summary>A posting was reversed.</summary>
    Reversed = 4,

    /// <summary>Someone signed in, or failed to.</summary>
    Authentication = 5,

    /// <summary>Permissions or a permission set were changed.</summary>
    PermissionChange = 6,

    /// <summary>A setting was changed.</summary>
    SetupChange = 7,

    /// <summary>
    /// Someone pushed past a block ASAP raised, such as approving an offer that sells below
    /// cost. Always recorded, whatever the audit settings say.
    /// </summary>
    Override = 8,

    /// <summary>Data was extracted out of ASAP.</summary>
    Export = 9,
}

/// <summary>
/// One recorded action, kept for as long as the tenant retention policy says.
/// </summary>
/// <remarks>
/// <para>
/// Audit rows are append-only. Nothing in ASAP updates or deletes one, and the entity carries no
/// soft-delete path so no code can. An audit trail that can be edited is not an audit trail.
/// </para>
/// <para>
/// Two categories are recorded regardless of how quiet an administrator has configured the log:
/// overrides, because someone deliberately pushed past a protection, and permission changes,
/// because they determine who could do so next time.
/// </para>
/// </remarks>
public sealed class AuditLogEntry : Entity, ITenantScoped
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>Company the action happened in, or null for something tenant-wide such as a sign-in.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>Branch the action happened at, or null at head office.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>Who did it, or null for a background job.</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Their login name at the time. Copied rather than joined, so the trail stays readable after
    /// the account is renamed or removed.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>When it happened, in UTC.</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>What kind of thing it was.</summary>
    public AuditAction Action { get; set; }

    /// <summary>Logical type acted on, for example <c>Finance.JournalBatch</c>.</summary>
    public string? EntityType { get; set; }

    /// <summary>Key of the record acted on.</summary>
    public Guid? EntityId { get; set; }

    /// <summary>
    /// The number a human would recognise the record by, for example <c>GJ-2026-00042</c>.
    /// Copied for the same reason as the user name.
    /// </summary>
    public string? DisplayNo { get; set; }

    /// <summary>
    /// What changed, as JSON holding the before and after of each altered field. Null for actions
    /// where the question does not arise, such as a sign-in.
    /// </summary>
    public string? Changes { get; set; }

    /// <summary>
    /// For an <see cref="AuditAction.Override"/>, the message code that was overridden, for
    /// example <c>PROMO.OFFER.BELOW_COST</c>. This is what makes "show me every time someone sold
    /// below cost last quarter" a single query.
    /// </summary>
    public string? OverriddenMessageCode { get; set; }

    /// <summary>The reason the user gave for overriding. Required when ASAP asks for one.</summary>
    public string? OverrideReason { get; set; }

    /// <summary>Address the request came from.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Which client was used: the web app, a till, or an integration.</summary>
    public string? ClientKind { get; set; }
}
