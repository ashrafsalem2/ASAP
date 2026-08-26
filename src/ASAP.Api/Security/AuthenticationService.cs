using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Security;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ASAP.Api.Security;

/// <summary>Why a sign-in attempt did not succeed.</summary>
public enum SignInFailure
{
    /// <summary>The name or the password was wrong. Which one is deliberately not distinguished.</summary>
    InvalidCredentials,

    /// <summary>Too many failed attempts; the account is locked for a while.</summary>
    LockedOut,

    /// <summary>The account or its tenant has been deactivated.</summary>
    Deactivated,
}

/// <summary>The outcome of a sign-in.</summary>
/// <param name="Succeeded">Whether the caller is now signed in.</param>
/// <param name="Failure">Why not, when they are not.</param>
/// <param name="LockedUntilUtc">When a lockout lifts.</param>
/// <param name="Tokens">The issued tokens, on success.</param>
/// <param name="User">The signed-in user, on success.</param>
public readonly record struct SignInOutcome(
    bool Succeeded,
    SignInFailure? Failure = null,
    DateTime? LockedUntilUtc = null,
    TokenPair Tokens = default,
    User? User = null);

/// <summary>
/// Signs users in, refreshes their sessions, and signs them out.
/// </summary>
/// <param name="context">The unit of work.</param>
/// <param name="tokens">Issues access and refresh tokens.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="options">Lockout and lifetime configuration.</param>
public sealed class AuthenticationService(
    AsapDbContext context,
    TokenService tokens,
    IClock clock,
    IOptions<SignInPolicyOptions> options)
{
    private readonly SignInPolicyOptions _options = options.Value;

    /// <summary>
    /// Verifies a password and issues tokens.
    /// </summary>
    /// <param name="userName">The login name.</param>
    /// <param name="password">The password as typed.</param>
    /// <param name="ipAddress">Where the request came from, for the audit trail.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<SignInOutcome> SignInAsync(
        string userName,
        string password,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        // Sign-in happens before any tenant is known, so the filters are stepped past
        // deliberately. The login name is unique across the installation.
        var user = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                u => u.UserName == userName && !u.IsDeleted,
                cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            // Hash anyway. Returning immediately for an unknown name makes sign-in measurably
            // faster for names that do not exist, which is enough to enumerate the user list.
            _ = BCrypt.Net.BCrypt.Verify(password, DummyHash);

            // Saved before returning. A run of these against names that do not exist is the
            // signature of someone enumerating the user list, and an audit row that is never
            // written is no use to whoever goes looking for it afterwards.
            await RecordAsync(null, userName, "Sign-in failed: no such user", ipAddress, cancellationToken)
                .ConfigureAwait(false);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new SignInOutcome(false, SignInFailure.InvalidCredentials);
        }

        if (user.LockedUntilUtc is { } lockedUntil && lockedUntil > now)
        {
            await RecordAsync(user.Id, userName, "Sign-in refused: account locked", ipAddress, cancellationToken)
                .ConfigureAwait(false);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new SignInOutcome(false, SignInFailure.LockedOut, lockedUntil);
        }

        if (!user.IsActive)
        {
            await RecordAsync(user.Id, userName, "Sign-in refused: account inactive", ipAddress, cancellationToken)
                .ConfigureAwait(false);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new SignInOutcome(false, SignInFailure.Deactivated);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;

            if (_options.MaxFailedAttempts > 0 && user.FailedLoginCount >= _options.MaxFailedAttempts)
            {
                user.LockedUntilUtc = now.AddMinutes(_options.LockoutMinutes);
                user.FailedLoginCount = 0;
            }

            await RecordAsync(user.Id, userName, "Sign-in failed: wrong password", ipAddress, cancellationToken)
                .ConfigureAwait(false);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return user.LockedUntilUtc is { } locked && locked > now
                ? new SignInOutcome(false, SignInFailure.LockedOut, locked)
                : new SignInOutcome(false, SignInFailure.InvalidCredentials);
        }

        var tenant = await context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == user.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null || !tenant.IsActive)
        {
            return new SignInOutcome(false, SignInFailure.Deactivated);
        }

        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        user.LastLoginAtUtc = now;

        var company = user.DefaultCompanyId is { } companyId
            ? await context.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    c => c.Id == companyId && !c.IsDeleted && c.IsActive,
                    cancellationToken)
                .ConfigureAwait(false)
            : null;

        var issued = tokens.Issue(user, company, user.DefaultBranchId, Guid.CreateVersion7());

        await StoreRefreshTokenAsync(user, issued, Guid.CreateVersion7(), ipAddress, cancellationToken)
            .ConfigureAwait(false);

        await RecordAsync(user.Id, userName, "Signed in", ipAddress, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SignInOutcome(true, Tokens: issued, User: user);
    }

    /// <summary>
    /// Redeems a refresh token for a new pair.
    /// </summary>
    /// <param name="refreshToken">The token as issued.</param>
    /// <param name="companyId">A company to switch into, or null to keep the current one.</param>
    /// <param name="branchId">A branch to switch to.</param>
    /// <param name="ipAddress">Where the request came from.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A new pair, or a failure when the token is spent, revoked or expired.</returns>
    public async Task<SignInOutcome> RefreshAsync(
        string refreshToken,
        Guid? companyId,
        Guid? branchId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var hash = TokenService.HashRefreshToken(refreshToken);

        var stored = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return new SignInOutcome(false, SignInFailure.InvalidCredentials);
        }

        if (stored.UsedAtUtc is not null)
        {
            // A token redeemed twice means one copy was not the user's. Which copy is unknowable,
            // so the whole chain goes. Signing the real user out is the correct outcome here:
            // something has gone wrong and they should authenticate again.
            await RevokeSessionAsync(stored.SessionId, "Refresh token reused", cancellationToken)
                .ConfigureAwait(false);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new SignInOutcome(false, SignInFailure.InvalidCredentials);
        }

        if (!stored.IsRedeemable(now) || stored.User is not { IsActive: true } user)
        {
            return new SignInOutcome(false, SignInFailure.InvalidCredentials);
        }

        stored.UsedAtUtc = now;

        var company = await ResolveCompanyAsync(user, companyId, cancellationToken).ConfigureAwait(false);
        var issued = tokens.Issue(user, company, branchId ?? user.DefaultBranchId, stored.SessionId);

        await StoreRefreshTokenAsync(user, issued, stored.SessionId, ipAddress, cancellationToken)
            .ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SignInOutcome(true, Tokens: issued, User: user);
    }

    /// <summary>Ends a session, invalidating every token in its chain.</summary>
    /// <param name="refreshToken">Any token from the session.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task SignOutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = TokenService.HashRefreshToken(refreshToken);

        var stored = await context.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return;
        }

        await RevokeSessionAsync(stored.SessionId, "Signed out", cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the companies a user may work in.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<IReadOnlyList<Company>> AccessibleCompaniesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return [];
        }

        var companies = await context.Companies
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == user.TenantId && c.IsActive && !c.IsDeleted)
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (user.IsSuperUser)
        {
            return companies;
        }

        var assignments = await context.UserPermissionAssignments
            .IgnoreQueryFilters()
            .Where(a => a.UserId == userId)
            .Select(a => a.CompanyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A null company on an assignment means every company in the tenant, which is how a
        // group accountant is granted access once rather than once per company.
        if (assignments.Exists(static c => c is null))
        {
            return companies;
        }

        var allowed = assignments.OfType<Guid>().ToHashSet();

        return [.. companies.Where(c => allowed.Contains(c.Id))];
    }

    private async Task<Company?> ResolveCompanyAsync(
        User user,
        Guid? companyId,
        CancellationToken cancellationToken)
    {
        var wanted = companyId ?? user.DefaultCompanyId;

        if (wanted is not { } id)
        {
            return null;
        }

        // Checked against what the user may actually reach, not merely against what exists. The
        // company claim is what the query filters trust, so a caller must never be able to name
        // one they have no assignment in.
        var accessible = await AccessibleCompaniesAsync(user.Id, cancellationToken).ConfigureAwait(false);

        return accessible.FirstOrDefault(c => c.Id == id);
    }

    private async Task StoreRefreshTokenAsync(
        User user,
        TokenPair issued,
        Guid sessionId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        context.RefreshTokens.Add(new RefreshToken
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            TokenHash = TokenService.HashRefreshToken(issued.RefreshToken),
            SessionId = sessionId,
            IssuedAtUtc = clock.UtcNow,
            ExpiresAtUtc = issued.RefreshExpiresAtUtc,
            IssuedToIp = ipAddress,
        });

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task RevokeSessionAsync(Guid sessionId, string reason, CancellationToken cancellationToken)
    {
        var chain = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.SessionId == sessionId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in chain)
        {
            token.RevokedAtUtc = clock.UtcNow;
            token.RevokedReason = reason;
        }
    }

    private async Task RecordAsync(
        Guid? userId,
        string userName,
        string what,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var tenantId = userId is { } id
            ? await context.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == id)
                .Select(u => u.TenantId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : Guid.Empty;

        context.AuditLog.Add(new AuditLogEntry
        {
            TenantId = tenantId,
            UserId = userId,
            UserName = userName,
            OccurredAtUtc = clock.UtcNow,
            Action = AuditAction.Authentication,
            Changes = what,
            IpAddress = ipAddress,
        });
    }

    /// <summary>
    /// A real BCrypt hash of a throwaway value, used to keep the timing of a failed sign-in the
    /// same whether or not the account exists.
    /// </summary>
    private const string DummyHash = "$2a$12$C6UzMDM.H6dfI/f/IKcEe.3Xx3Xy3q4V0Yqf0lY7CkVQ8vGZ0nA1O";
}

/// <summary>Sign-in policy.</summary>
public sealed class SignInPolicyOptions
{
    /// <summary>Configuration section these are bound from.</summary>
    public const string SectionName = "Asap:Authentication";

    /// <summary>Wrong passwords in a row before the account locks. Zero never locks.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>How long a lockout lasts.</summary>
    public int LockoutMinutes { get; set; } = 15;
}
