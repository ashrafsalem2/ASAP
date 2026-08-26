namespace ASAP.Api.Security;

/// <summary>
/// Claim names ASAP puts in its access tokens.
/// </summary>
/// <remarks>
/// Permissions are deliberately absent. Carrying them in the token would mean a token issued this
/// morning still grants access this afternoon after someone revoked it, and revocation that takes
/// effect at the next sign-in is not revocation. They are resolved per request instead.
/// </remarks>
public static class AsapClaims
{
    /// <summary>The signed-in user.</summary>
    public const string UserId = "asap:uid";

    /// <summary>The tenant they belong to.</summary>
    public const string TenantId = "asap:tid";

    /// <summary>The company they are currently working in.</summary>
    public const string CompanyId = "asap:cid";

    /// <summary>The branch they are working at, absent at head office.</summary>
    public const string BranchId = "asap:bid";

    /// <summary>Their preferred language.</summary>
    public const string Culture = "asap:culture";

    /// <summary>Present only on the installation owner.</summary>
    public const string SuperUser = "asap:super";

    /// <summary>Their display name, for greeting them without a database round trip.</summary>
    public const string DisplayName = "asap:name";
}
