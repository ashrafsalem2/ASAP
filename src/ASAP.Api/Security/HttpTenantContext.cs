using System.Security.Claims;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Api.Security;

/// <summary>
/// Reads the tenant, company and branch for the current request off the caller access token.
/// </summary>
/// <remarks>
/// <para>
/// Scoped to the request, so the values are fixed for its whole lifetime. That matters: the query
/// filters read them on every query, and a value that could shift mid-request would mean one
/// operation reading from two companies.
/// </para>
/// <para>
/// The company comes from the token rather than from a header or a query parameter. A caller who
/// could name their own company on each request could name one they have no assignment in, and
/// the filters would happily serve it.
/// </para>
/// </remarks>
/// <param name="accessor">Supplies the current request.</param>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private bool _crossTenant;

    /// <inheritdoc />
    public Guid? TenantId => ReadGuid(AsapClaims.TenantId);

    /// <inheritdoc />
    public Guid? CompanyId => ReadGuid(AsapClaims.CompanyId);

    /// <inheritdoc />
    public Guid? BranchId => ReadGuid(AsapClaims.BranchId);

    /// <inheritdoc />
    public bool IsCrossTenantOperation => _crossTenant;

    /// <inheritdoc />
    public Guid RequireTenantId()
        => TenantId ?? throw new InvalidOperationException(
            "This operation needs a tenant, and the request carries none. It is either "
            + "unauthenticated or the token is missing its tenant claim.");

    /// <inheritdoc />
    public Guid RequireCompanyId()
        => CompanyId ?? throw new InvalidOperationException(
            "This operation needs a company, and none is selected on the request.");

    /// <summary>
    /// Relaxes the company filters for the duration of the returned scope.
    /// </summary>
    /// <remarks>
    /// Used by the seeder, by migrations and by deliberate cross-company consolidation. It is a
    /// method on the concrete type rather than on <see cref="ITenantContext"/> so that module code,
    /// which only ever sees the interface, has no way to reach it.
    /// </remarks>
    public IDisposable BeginCrossTenantScope()
    {
        _crossTenant = true;
        return new Scope(this);
    }

    private Guid? ReadGuid(string claimType)
    {
        var value = accessor.HttpContext?.User.FindFirstValue(claimType);

        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private sealed class Scope(HttpTenantContext owner) : IDisposable
    {
        public void Dispose() => owner._crossTenant = false;
    }
}
