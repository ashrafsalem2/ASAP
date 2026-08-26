namespace ASAP.Platform.Kernel.Tenancy;

/// <summary>
/// The tenant, company and branch the current request is acting in. Resolved once per request
/// from the caller's token and the company they selected, then consumed by the query filters,
/// the number series, the posting engine and every module.
/// </summary>
public interface ITenantContext
{
    /// <summary>Active tenant, or null on unauthenticated requests such as login.</summary>
    Guid? TenantId { get; }

    /// <summary>Active company, or null before the caller has chosen one.</summary>
    Guid? CompanyId { get; }

    /// <summary>Active branch, or null when the caller is working at head office.</summary>
    Guid? BranchId { get; }

    /// <summary>
    /// True while a maintenance operation is deliberately running across every tenant —
    /// migrations, the seeder, cross-company consolidation. Query filters relax only here,
    /// and only the platform can turn it on.
    /// </summary>
    bool IsCrossTenantOperation { get; }

    /// <summary>
    /// Active tenant, or a thrown exception if the request has none. Use where a tenant is a
    /// precondition, so the failure is a clear message instead of a null reference later on.
    /// </summary>
    Guid RequireTenantId();

    /// <summary>Active company, or a thrown exception if the caller has not selected one.</summary>
    Guid RequireCompanyId();
}
