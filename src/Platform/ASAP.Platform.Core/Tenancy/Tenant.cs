using ASAP.Platform.Kernel.Entities;

namespace ASAP.Platform.Core.Tenancy;

/// <summary>
/// One ASAP subscriber: the organisation that bought the system.
/// </summary>
/// <remarks>
/// A tenant owns one or more companies and is the boundary that licensing and isolation are
/// drawn around. A single installation can serve many tenants, or exactly one when ASAP is
/// deployed on a customer own server; the code is identical either way, which keeps the
/// on-premise and hosted products from drifting apart.
/// </remarks>
public sealed class Tenant : AuditableEntity, IConcurrencyAware
{
    /// <summary>Short stable code, for example <c>ALTUWIJRI</c>. Used in URLs and support tickets.</summary>
    public required string Code { get; set; }

    /// <summary>Organisation name as it should appear on screen.</summary>
    public required string Name { get; set; }

    /// <summary>Organisation name in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>Whether the tenant may sign in at all. Suspending one locks every user it owns.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Default language for new users, for example <c>en</c> or <c>ar</c>.</summary>
    public string DefaultCulture { get; set; } = "en";

    /// <summary>
    /// IANA time zone the tenant operates in, for example <c>Asia/Riyadh</c>. Drives what
    /// "today" means when defaulting a posting date, which is a calendar date rather than an instant.
    /// </summary>
    public string TimeZoneId { get; set; } = "Asia/Riyadh";

    /// <summary>
    /// Module identifiers this tenant has licensed. Empty means every loaded module is
    /// available, which is the normal state of a single-tenant on-premise install.
    /// </summary>
    public List<string> LicensedModules { get; set; } = [];

    /// <summary>When the licence lapses, or null for a perpetual one.</summary>
    public DateOnly? LicenseExpiresOn { get; set; }

    /// <summary>Companies this tenant owns.</summary>
    public ICollection<Company> Companies { get; set; } = [];

    /// <inheritdoc />
    public byte[]? RowVersion { get; set; }
}
