using System.ComponentModel.DataAnnotations;

namespace ASAP.Api.Security;

/// <summary>How ASAP issues and validates access tokens.</summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section these are bound from.</summary>
    public const string SectionName = "Asap:Jwt";

    /// <summary>Who issued the token. Validated on every request.</summary>
    [Required]
    public string Issuer { get; set; } = "asap-erp";

    /// <summary>Who the token is for. Validated on every request.</summary>
    [Required]
    public string Audience { get; set; } = "asap-client";

    /// <summary>
    /// The symmetric signing key, at least 32 bytes.
    /// </summary>
    /// <remarks>
    /// Never committed. Supplied through user secrets in development and through the environment
    /// or a secret store in production. Startup refuses a key that is missing, short, or still
    /// set to the development placeholder, because a signing key someone can guess means anyone
    /// can mint a token for any user in any company.
    /// </remarks>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// How long an access token lasts.
    /// </summary>
    /// <remarks>
    /// Short on purpose. Permissions are resolved per request, so a revoked permission stops
    /// working immediately, but a disabled <em>account</em> keeps its token until this expires.
    /// Fifteen minutes bounds that window without making the refresh chatter.
    /// </remarks>
    [Range(1, 720)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// How long a refresh token lasts, and therefore how long an idle session survives.
    /// </summary>
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// Tolerance for clock difference between the token issuer and the validating host. Kept at
    /// zero: both are the same process today, and a default five-minute skew would silently
    /// extend every token.
    /// </summary>
    public int ClockSkewSeconds { get; set; }
}
