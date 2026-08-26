using System.Security.Claims;
using ASAP.Platform.Core.Security;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;

namespace ASAP.Api.Security;

/// <summary>
/// Who is making the current request, and what they may do in the company they are working in.
/// </summary>
/// <remarks>
/// <para>
/// Identity comes from the access token; permissions do not. They are read from the database on
/// first use and held for the rest of the request. That costs one query on any request that
/// actually checks a permission, and nothing at all on one that does not.
/// </para>
/// <para>
/// The alternative -- putting permissions in the token -- was rejected because a token issued in
/// the morning would still grant revoked access in the afternoon. Revocation that waits for the
/// next sign-in is not revocation, and in a business where someone can be walked off site at
/// eleven o clock that distinction is the whole point.
/// </para>
/// </remarks>
/// <param name="accessor">Supplies the current request.</param>
/// <param name="tenantContext">Which company permissions should be resolved for.</param>
/// <param name="services">
/// Resolves the permission source on demand rather than at construction.
/// </param>
/// <param name="clock">Supplies today, for time-limited assignments.</param>
/// <remarks>
/// <para>
/// The permission source is resolved lazily to break a genuine dependency cycle:
/// <c>AsapDbContext</c> needs an <see cref="IUserContext"/> to stamp who wrote a row, and reading
/// permissions needs the database. Taking <see cref="IUserPermissionSource"/> as a constructor
/// parameter makes the container refuse to build either one.
/// </para>
/// <para>
/// The cycle exists because this interface carries two things of very different cost: identity,
/// which is a claim lookup, and authorisation, which is a query. Resolving the second on first
/// use is what lets both live behind one contract, and means a request that never checks a
/// permission never touches the database for it.
/// </para>
/// </remarks>
public sealed class HttpUserContext(
    IHttpContextAccessor accessor,
    ITenantContext tenantContext,
    IServiceProvider services,
    IClock clock) : IUserContext
{
    private IReadOnlySet<string>? _resolved;

    /// <inheritdoc />
    public Guid? UserId => ReadGuid(AsapClaims.UserId);

    /// <inheritdoc />
    public string? UserName => Principal?.Identity?.Name;

    /// <inheritdoc />
    public string? DisplayName => Principal?.FindFirstValue(AsapClaims.DisplayName);

    /// <inheritdoc />
    public string? Culture => Principal?.FindFirstValue(AsapClaims.Culture);

    /// <inheritdoc />
    public bool IsSuperUser =>
        string.Equals(Principal?.FindFirstValue(AsapClaims.SuperUser), "true", StringComparison.Ordinal);

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions => _resolved ??= Resolve();

    /// <inheritdoc />
    public bool Has(string permissionKey)
    {
        if (string.IsNullOrWhiteSpace(permissionKey))
        {
            return false;
        }

        return IsSuperUser || Permissions.Contains(permissionKey);
    }

    /// <inheritdoc />
    public Guid RequireUserId()
        => UserId ?? throw new InvalidOperationException(
            "This operation needs a signed-in user, and the request is anonymous.");

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    private Guid? ReadGuid(string claimType)
    {
        var value = Principal?.FindFirstValue(claimType);

        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Reads the permissions for this user in the active company.
    /// </summary>
    /// <remarks>
    /// Blocks on the asynchronous source because <see cref="IUserContext"/> is synchronous, and
    /// it is synchronous because it is consulted from query filters and expression trees where an
    /// await is not available. The call is a single indexed read against a connection this
    /// request already holds. If it ever shows up in a profile, the fix is a per-request cache
    /// primed by middleware, not an async interface that its callers cannot use.
    /// </remarks>
    private IReadOnlySet<string> Resolve()
    {
        if (UserId is not { } userId || tenantContext.CompanyId is not { } companyId)
        {
            // No user, or no company chosen yet. Holding nothing is the safe reading: a caller
            // who has not selected a company should fail a permission check, not pass one.
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return services
            .GetRequiredService<IUserPermissionSource>()
            .ResolveAsync(userId, companyId, tenantContext.BranchId, clock.Today)
            .GetAwaiter()
            .GetResult();
    }
}
