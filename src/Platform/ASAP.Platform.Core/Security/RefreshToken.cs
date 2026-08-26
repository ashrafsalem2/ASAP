using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Security;

/// <summary>
/// A long-lived token that buys a new access token without asking for the password again.
/// </summary>
/// <remarks>
/// <para>
/// Access tokens are deliberately short-lived, which would mean signing in every few minutes
/// were it not for these. A refresh token is single-use: redeeming one issues a replacement and
/// marks the old one used.
/// </para>
/// <para>
/// Single use is what makes theft detectable. If a stolen token is redeemed and the real user
/// later redeems the same one, ASAP sees a token used twice and revokes the whole chain, which
/// signs out both the thief and the user. Signing the real user out is the correct outcome:
/// something has gone wrong and they should authenticate again.
/// </para>
/// <para>
/// Only the hash is stored. Anyone reading the table cannot mint a session from it.
/// </para>
/// </remarks>
public sealed class RefreshToken : Entity, ITenantScoped
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>The user this token signs in.</summary>
    public Guid UserId { get; set; }

    /// <summary>Navigation to the user.</summary>
    public User? User { get; set; }

    /// <summary>SHA-256 of the token. The token itself is never stored.</summary>
    public required byte[] TokenHash { get; set; }

    /// <summary>
    /// The chain this token belongs to, carried forward through every rotation. Lets ASAP revoke
    /// a whole session, however many times it has been refreshed, rather than one link of it.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>When it was issued, in UTC.</summary>
    public DateTime IssuedAtUtc { get; set; }

    /// <summary>When it stops working, in UTC.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>When it was redeemed, in UTC. Null while still unused.</summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>When it was revoked, in UTC. Null while still valid.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Why it was revoked: signed out, replaced, or reused after being redeemed.</summary>
    public string? RevokedReason { get; set; }

    /// <summary>Address the token was issued to, recorded for the audit trail.</summary>
    public string? IssuedToIp { get; set; }

    /// <summary>Whether the token can still be redeemed at a given moment.</summary>
    /// <param name="utcNow">The current instant.</param>
    public bool IsRedeemable(DateTime utcNow)
        => UsedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > utcNow;
}
