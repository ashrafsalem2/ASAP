using ASAP.Api.Infrastructure;
using ASAP.Api.Security;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>What a client sends to sign in.</summary>
/// <param name="UserName">The login name.</param>
/// <param name="Password">The password.</param>
public sealed record SignInRequest(string UserName, string Password);

/// <summary>What a client sends to refresh, or to switch company.</summary>
/// <param name="RefreshToken">The refresh token from the previous response.</param>
/// <param name="CompanyId">A company to switch into, or null to stay where they are.</param>
/// <param name="BranchId">A branch to switch to.</param>
public sealed record RefreshRequest(string RefreshToken, Guid? CompanyId = null, Guid? BranchId = null);

/// <summary>A company the signed-in user may work in.</summary>
/// <param name="Id">The company key.</param>
/// <param name="Code">Its short code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its Arabic name.</param>
/// <param name="BaseCurrencyCode">The currency its books are kept in.</param>
public sealed record CompanySummary(
    Guid Id,
    string Code,
    string Name,
    string? NameArabic,
    string BaseCurrencyCode);

/// <summary>Sign-in, refresh, sign-out and company switching.</summary>
public static class AuthEndpoints
{
    /// <summary>Maps the authentication endpoints.</summary>
    /// <param name="app">The route builder.</param>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/login", SignInAsync)
             .AllowAnonymous()
             .WithName("SignIn")
             .WithSummary("Signs in and returns an access token and a refresh token.");

        group.MapPost("/refresh", RefreshAsync)
             .AllowAnonymous()
             .WithName("Refresh")
             .WithSummary("Exchanges a refresh token for a new pair, optionally switching company.");

        group.MapPost("/logout", SignOutAsync)
             .AllowAnonymous()
             .WithName("SignOut")
             .WithSummary("Ends the session, invalidating every token in its chain.");

        group.MapGet("/me", MeAsync)
             .RequireAuthorization()
             .WithName("Me")
             .WithSummary("Reports who the caller is and what they may do in the active company.");

        group.MapGet("/companies", CompaniesAsync)
             .RequireAuthorization()
             .WithName("MyCompanies")
             .WithSummary("Lists the companies the caller may work in.");

        return app;
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        AuthenticationService authentication,
        IMessageCatalog messages,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "A user name and password are required.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        var result = await authentication
            .SignInAsync(request.UserName, request.Password, ClientIp(http), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Results.Json(
                DescribeFailure(result, messages, http.Request.Path),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(Describe(result));
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        AuthenticationService authentication,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "A refresh token is required.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        var result = await authentication
            .RefreshAsync(
                request.RefreshToken,
                request.CompanyId,
                request.BranchId,
                ClientIp(http),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Deliberately vague. A spent, revoked, expired or forged token all answer the same,
            // because the difference is only useful to someone probing with tokens they hold.
            return Results.Json(
                new ProblemDetails
                {
                    Type = AsapProblem.TypeUri,
                    Title = "Your session has ended",
                    Detail = "Sign in again to continue.",
                    Status = StatusCodes.Status401Unauthorized,
                    Instance = http.Request.Path,
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(Describe(result));
    }

    private static async Task<IResult> SignOutAsync(
        RefreshRequest request,
        AuthenticationService authentication,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await authentication.SignOutAsync(request.RefreshToken, cancellationToken).ConfigureAwait(false);
        }

        // Always the same answer, whether or not the token existed. Signing out is not an
        // opportunity to learn which tokens are real.
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        IUserContext user,
        ASAP.Platform.Kernel.Tenancy.ITenantContext tenant,
        ASAP.Platform.Persistence.AsapDbContext context,
        CancellationToken cancellationToken)
    {
        // The branch name, not only its key. A screen that shows a user which branch they are
        // working in by printing a GUID has told them nothing, and this is the one place that
        // knows how to turn one into the other.
        var branch = tenant.BranchId is { } branchId
            ? await context.Branches
                .AsNoTracking()
                .Where(b => b.Id == branchId)
                .Select(b => new { b.Code, b.Name, b.NameArabic })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        return Results.Ok(new
        {
            userId = user.UserId,
            userName = user.UserName,
            displayName = user.DisplayName,
            culture = user.Culture,
            isSuperUser = user.IsSuperUser,
            tenantId = tenant.TenantId,
            companyId = tenant.CompanyId,
            branchId = tenant.BranchId,
            branchCode = branch?.Code,
            branchName = user.Culture is "ar" && branch?.NameArabic is { } arabic ? arabic : branch?.Name,
            permissions = user.Permissions.Order(StringComparer.Ordinal).ToList(),
        });
    }

    private static async Task<IResult> CompaniesAsync(
        IUserContext user,
        AuthenticationService authentication,
        CancellationToken cancellationToken)
    {
        var companies = await authentication
            .AccessibleCompaniesAsync(user.RequireUserId(), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(companies.Select(static c => new CompanySummary(
            c.Id,
            c.Code,
            c.Name,
            c.NameArabic,
            c.BaseCurrencyCode)));
    }

    private static object Describe(SignInOutcome result) => new
    {
        accessToken = result.Tokens.AccessToken,
        expiresAt = TokenService.FormatExpiry(result.Tokens.ExpiresAtUtc),
        refreshToken = result.Tokens.RefreshToken,
        refreshExpiresAt = TokenService.FormatExpiry(result.Tokens.RefreshExpiresAtUtc),
        user = new
        {
            id = result.User!.Id,
            userName = result.User.UserName,
            displayName = result.User.DisplayName,
            culture = result.User.Culture,
            isSuperUser = result.User.IsSuperUser,
            defaultCompanyId = result.User.DefaultCompanyId,
            defaultBranchId = result.User.DefaultBranchId,
        },
    };

    private static ProblemDetails DescribeFailure(
        SignInOutcome result,
        IMessageCatalog messages,
        string instance)
    {
        // A locked account is worth saying plainly: the user knows their password is right, and
        // "wrong name or password" would send them round in circles. It reveals only what someone
        // who just locked the account already knows.
        if (result.Failure == SignInFailure.LockedOut)
        {
            return new ProblemDetails
            {
                Type = AsapProblem.TypeUri,
                Title = "This account is temporarily locked",
                Detail = result.LockedUntilUtc is { } until
                    ? $"Too many failed sign-in attempts. Try again after {until:HH:mm} UTC."
                    : "Too many failed sign-in attempts.",
                Status = StatusCodes.Status401Unauthorized,
                Instance = instance,
                Extensions = { ["code"] = "SEC.SIGNIN.LOCKED_OUT" },
            };
        }

        // Wrong name and wrong password answer identically, so the response cannot be used to
        // find out which accounts exist.
        return new ProblemDetails
        {
            Type = AsapProblem.TypeUri,
            Title = "Sign-in failed",
            Detail = "The user name or password is incorrect.",
            Status = StatusCodes.Status401Unauthorized,
            Instance = instance,
            Extensions = { ["code"] = "SEC.SIGNIN.INVALID" },
        };
    }

    private static string? ClientIp(HttpContext http)
        => http.Connection.RemoteIpAddress?.ToString();
}
