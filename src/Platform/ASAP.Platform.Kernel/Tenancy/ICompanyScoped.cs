namespace ASAP.Platform.Kernel.Tenancy;

/// <summary>
/// Data belonging to one legal entity inside a tenant. A company owns its own chart of
/// accounts, fiscal calendar, currency and number series; nothing posted in one company is
/// visible from another except through an explicit consolidation query.
/// </summary>
public interface ICompanyScoped : ITenantScoped
{
    /// <summary>Owning company. Assigned on insert from the ambient tenant context.</summary>
    Guid CompanyId { get; set; }
}
