using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Security;

/// <summary>
/// A named bundle of permissions, such as Cashier, Accountant or Branch Manager.
/// </summary>
/// <remarks>
/// <para>
/// Permissions are never granted to a person one key at a time. An administrator builds a set
/// once, gives it a name a manager would recognise, and assigns it. That is the whole of the
/// ASAP permission model: <b>define a set, assign it to a person for a company, optionally
/// narrow it to a branch.</b> Three steps, and the same three regardless of module.
/// </para>
/// <para>
/// Sets belong to the tenant rather than to a company, so the Cashier set is defined once and
/// used in every company. A set marked <see cref="IsSystemDefined"/> ships with ASAP; it can be
/// copied and edited, but not changed in place, so an upgrade can keep it current without
/// overwriting anyone customisation.
/// </para>
/// </remarks>
public sealed class PermissionSet : AuditableEntity, ITenantScoped, ISoftDeletable, IConcurrencyAware
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>Short stable code, for example <c>CASHIER</c>.</summary>
    public required string Code { get; set; }

    /// <summary>Name a manager would recognise, such as "Branch Cashier".</summary>
    public required string Name { get; set; }

    /// <summary>Name in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>What this set is for, shown when choosing one to assign.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// True for a set that ships with ASAP. Read-only in the UI: copy it to make changes, so
    /// upgrades can refresh the original without discarding local edits.
    /// </summary>
    public bool IsSystemDefined { get; set; }

    /// <summary>
    /// Sets this one includes wholesale. A Branch Manager set can include Cashier rather than
    /// restating its keys, so a change to Cashier reaches every set built on it.
    /// </summary>
    public ICollection<PermissionSetInclusion> Includes { get; set; } = [];

    /// <summary>The permission keys this set grants directly.</summary>
    public ICollection<PermissionSetEntry> Entries { get; set; } = [];

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; set; }

    /// <inheritdoc />
    public byte[]? RowVersion { get; set; }
}

/// <summary>One permission key granted by a set.</summary>
public sealed class PermissionSetEntry : Entity
{
    /// <summary>The set granting it.</summary>
    public Guid PermissionSetId { get; set; }

    /// <summary>Navigation to the owning set.</summary>
    public PermissionSet? PermissionSet { get; set; }

    /// <summary>
    /// The permission key, for example <c>Finance.Journal.Post</c>. Stored as text rather than
    /// as a foreign key so a set can keep referring to a permission belonging to an extension
    /// that happens to be uninstalled at the moment, and start working again when it returns.
    /// </summary>
    public required string PermissionKey { get; set; }
}

/// <summary>One set included wholesale by another.</summary>
public sealed class PermissionSetInclusion : Entity
{
    /// <summary>The set doing the including.</summary>
    public Guid PermissionSetId { get; set; }

    /// <summary>Navigation to the including set.</summary>
    public PermissionSet? PermissionSet { get; set; }

    /// <summary>The set being included.</summary>
    public Guid IncludedPermissionSetId { get; set; }

    /// <summary>Navigation to the included set.</summary>
    public PermissionSet? IncludedPermissionSet { get; set; }
}
