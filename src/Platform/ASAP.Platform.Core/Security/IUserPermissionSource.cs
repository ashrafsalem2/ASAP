namespace ASAP.Platform.Core.Security;

/// <summary>
/// Reads what a user is allowed to do in a company, from wherever assignments are stored.
/// </summary>
/// <remarks>
/// Permissions are resolved per request rather than carried in the access token. A token issued
/// this morning would otherwise still grant access this afternoon after someone revoked it, and
/// revocation that takes effect at the next sign-in is not revocation.
/// </remarks>
public interface IUserPermissionSource
{
    /// <summary>
    /// Resolves every permission key a user holds, with inclusions and implications expanded.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="companyId">The company being worked in.</param>
    /// <param name="branchId">The branch being worked at, or null at head office.</param>
    /// <param name="asOf">Date to test time-limited assignments against.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<IReadOnlySet<string>> ResolveAsync(
        Guid userId,
        Guid companyId,
        Guid? branchId,
        DateOnly asOf,
        CancellationToken cancellationToken = default);
}
