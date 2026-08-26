using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Setup;

/// <summary>
/// One setting that has actually been given a value, at one scope.
/// </summary>
/// <remarks>
/// <para>
/// Only overrides are stored. A setting left at its declared default has no row here at all,
/// which means a fresh company starts with an empty table and still runs correctly, and it
/// means an upgrade that changes a default reaches every customer who never overrode it.
/// </para>
/// <para>
/// Reading a setting walks outwards from the narrowest scope -- user, branch, company, tenant --
/// and takes the first row it finds, falling back to the declared default when there is none.
/// </para>
/// </remarks>
public sealed class SetupValue : AuditableEntity, ITenantScoped, IConcurrencyAware
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>The setting key, for example <c>Inventory.Costing.AllowNegativeStock</c>.</summary>
    public required string Key { get; set; }

    /// <summary>Which level this value was set at.</summary>
    public SetupScope Scope { get; set; } = SetupScope.Company;

    /// <summary>
    /// The company, branch or user the value belongs to. Null for a tenant-scoped value, which
    /// needs no further identification.
    /// </summary>
    public Guid? ScopeId { get; set; }

    /// <summary>
    /// The value in its string form, parsed on read according to the declared type. Null clears
    /// the override and lets the wider scope apply again.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// True when <see cref="Value"/> holds ciphertext rather than the value itself, which is the
    /// case for a setting declared as <see cref="SetupValueType.Secret"/>. Such a value is never
    /// returned to a client; the setup screen shows only whether one is present.
    /// </summary>
    public bool IsEncrypted { get; set; }

    /// <inheritdoc />
    public byte[]? RowVersion { get; set; }
}
