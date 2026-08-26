using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Tenancy;

/// <summary>
/// One legal entity inside a tenant, and the unit almost all ASAP data hangs off.
/// </summary>
/// <remarks>
/// A company owns its own chart of accounts, fiscal calendar, base currency, number series and
/// posted history. Nothing posted in one company is visible from another except through an
/// explicit consolidation query that says so. This mirrors how Business Central draws the line,
/// and it is the reason a group can run its trading arm and its property arm in one installation
/// without their books ever touching.
/// </remarks>
public sealed class Company : AuditableEntity, ITenantScoped, ISoftDeletable, IConcurrencyAware
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>Owning tenant.</summary>
    public Tenant? Tenant { get; set; }

    /// <summary>Short stable code, for example <c>MAIN</c>. Appears in the company switcher.</summary>
    public required string Code { get; set; }

    /// <summary>Registered name of the legal entity.</summary>
    public required string Name { get; set; }

    /// <summary>Registered name in Arabic, as it must appear on a tax invoice.</summary>
    public string? NameArabic { get; set; }

    /// <summary>Commercial registration number.</summary>
    public string? RegistrationNo { get; set; }

    /// <summary>Tax registration number, printed on invoices.</summary>
    public string? TaxRegistrationNo { get; set; }

    /// <summary>
    /// ISO currency code the books are kept in, for example <c>SAR</c>. Fixed once the first
    /// entry is posted: changing it afterwards would render every posted amount meaningless.
    /// </summary>
    public required string BaseCurrencyCode { get; set; }

    /// <summary>
    /// Month the financial year opens, 1 to 12. Set to 1 for a calendar year, 4 for a year
    /// running April to March.
    /// </summary>
    public int FiscalYearStartMonth { get; set; } = 1;

    /// <summary>Whether users may select and post into this company.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True once anything has been posted. Guards the settings that must not move afterwards,
    /// such as base currency and costing method.
    /// </summary>
    public bool HasPostedEntries { get; set; }

    /// <summary>Branches belonging to this company.</summary>
    public ICollection<Branch> Branches { get; set; } = [];

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; set; }

    /// <inheritdoc />
    public byte[]? RowVersion { get; set; }
}
