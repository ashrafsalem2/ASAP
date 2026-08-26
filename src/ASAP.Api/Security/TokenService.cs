using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ASAP.Platform.Core.Security;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Kernel.Time;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ASAP.Api.Security;

/// <summary>An issued pair of tokens.</summary>
/// <param name="AccessToken">The short-lived token sent with every request.</param>
/// <param name="ExpiresAtUtc">When the access token stops working.</param>
/// <param name="RefreshToken">The single-use token that buys the next access token.</param>
/// <param name="RefreshExpiresAtUtc">When the session ends if it is not refreshed.</param>
public readonly record struct TokenPair(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshExpiresAtUtc);

/// <summary>Issues and rotates ASAP access tokens.</summary>
/// <param name="options">Signing and lifetime configuration.</param>
/// <param name="clock">Supplies the issue and expiry instants.</param>
public sealed class TokenService(IOptions<JwtOptions> options, IClock clock)
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>
    /// Issues a token pair for a user working in a company.
    /// </summary>
    /// <param name="user">Who is signing in.</param>
    /// <param name="company">The company they are working in, or null before one is chosen.</param>
    /// <param name="branchId">The branch they are working at, or null at head office.</param>
    /// <param name="sessionId">
    /// The session chain. Pass the existing one when refreshing, so a whole session can be revoked
    /// however many times it has rotated.
    /// </param>
    public TokenPair Issue(User user, Company? company, Guid? branchId, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(AsapClaims.UserId, user.Id.ToString()),
            new(AsapClaims.TenantId, user.TenantId.ToString()),
            new(AsapClaims.DisplayName, user.DisplayName),
        };

        if (company is not null)
        {
            claims.Add(new Claim(AsapClaims.CompanyId, company.Id.ToString()));
        }

        if (branchId is { } branch)
        {
            claims.Add(new Claim(AsapClaims.BranchId, branch.ToString()));
        }

        if (user.Culture is { } culture)
        {
            claims.Add(new Claim(AsapClaims.Culture, culture));
        }

        if (user.IsSuperUser)
        {
            claims.Add(new Claim(AsapClaims.SuperUser, "true"));
        }

        var credentials = new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return new TokenPair(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires,
            CreateRefreshToken(),
            now.AddDays(_options.RefreshTokenDays));
    }

    /// <summary>
    /// Creates a refresh token: 32 bytes of cryptographic randomness, base64url encoded.
    /// </summary>
    /// <remarks>
    /// Random rather than a signed token, because it carries no claims and needs none. Its only
    /// job is to be unguessable and to be looked up.
    /// </remarks>
    public static string CreateRefreshToken()
        => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Hashes a refresh token for storage.
    /// </summary>
    /// <remarks>
    /// Plain SHA-256 with no salt or work factor, deliberately. A password needs those because it
    /// is short, chosen by a human, and reused; this is 256 bits of randomness, so there is
    /// nothing to brute-force and nothing to guess from a rainbow table. Storing only the hash
    /// means a leaked database still mints no sessions.
    /// </remarks>
    public static byte[] HashRefreshToken(string refreshToken)
        => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken));

    /// <summary>
    /// Builds the parameters used to validate an incoming token.
    /// </summary>
    /// <param name="options">Signing and lifetime configuration.</param>
    public static TokenValidationParameters ValidationParameters(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(options.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
            NameClaimType = ClaimTypes.Name,
        };
    }

    private SymmetricSecurityKey SigningKey()
        => new(System.Text.Encoding.UTF8.GetBytes(_options.SigningKey));

    /// <summary>
    /// Formats an expiry for a client, in a form that is unambiguous across time zones.
    /// </summary>
    public static string FormatExpiry(DateTime utc)
        => utc.ToString("O", CultureInfo.InvariantCulture);
}
