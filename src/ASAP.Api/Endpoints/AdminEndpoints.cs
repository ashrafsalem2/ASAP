using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Core.Security;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>What a client sends to create a user account.</summary>
/// <param name="UserName">What they will sign in with.</param>
/// <param name="DisplayName">What the audit log and the screens will call them.</param>
/// <param name="TemporaryPassword">
/// A password to start with, which they are made to change on first sign-in. Given by the
/// administrator rather than generated here so it can be handed over in person, and marked for
/// change so it stops being a shared secret the moment it has been used once.
/// </param>
/// <param name="Email">Their email, if there is one.</param>
/// <param name="PermissionSetCodes">The sets to assign.</param>
public sealed record CreateUserRequest(
    string UserName,
    string DisplayName,
    string TemporaryPassword,
    string? Email = null,
    IReadOnlyList<string>? PermissionSetCodes = null);

/// <summary>What a client sends to change a user account.</summary>
/// <param name="DisplayName">A new display name, or null to leave it.</param>
/// <param name="Email">A new email, or null to leave it.</param>
/// <param name="IsActive">Whether the account may sign in, or null to leave it.</param>
/// <param name="Culture">Their language, or null to leave it.</param>
public sealed record UpdateUserRequest(
    string? DisplayName = null,
    string? Email = null,
    bool? IsActive = null,
    string? Culture = null);

/// <summary>What a client sends to set which permission sets a user holds.</summary>
/// <param name="PermissionSetCodes">The complete list. Anything not named is taken away.</param>
public sealed record AssignSetsRequest(IReadOnlyList<string> PermissionSetCodes);

/// <summary>What a client sends to reset somebody's password.</summary>
/// <param name="TemporaryPassword">The password to set, which they must then change.</param>
public sealed record ResetPasswordRequest(string TemporaryPassword);

/// <summary>What a client sends to write a permission set.</summary>
/// <param name="Code">Its code. Ignored when editing.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Permissions">Every permission it grants.</param>
public sealed record SavePermissionSetRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    string? Description = null,
    IReadOnlyList<string>? Permissions = null);

/// <summary>
/// Users, permission sets, and what every permission actually means.
/// </summary>
/// <remarks>
/// <para>
/// The administration menu has declared these pages since the platform shipped and none of them
/// existed. A permission system nobody can see is a permission system that gets worked around:
/// the way an installation ends up with six people signed in as the administrator is that
/// granting them anything less was harder than not.
/// </para>
/// <para>
/// Two rules here are worth more than the rest. Nobody can turn off the account they are signed
/// in with, and nothing may leave the installation without an account able to administer it —
/// the second having no override, because there is no way back from it that does not involve a
/// database.
/// </para>
/// </remarks>
public static class AdminEndpoints
{
    private const string UserReadPermission = "Platform.User.Read";
    private const string UserWritePermission = "Platform.User.Update";
    private const string UserCreatePermission = "Platform.User.Create";
    private const string SetReadPermission = "Platform.PermissionSet.Read";
    private const string SetWritePermission = "Platform.PermissionSet.Update";
    private const string SetCreatePermission = "Platform.PermissionSet.Create";
    private const string SetDeletePermission = "Platform.PermissionSet.Delete";

    /// <summary>The shortest password the installation accepts.</summary>
    /// <remarks>
    /// Length rather than a character-class rule. A rule demanding a symbol produces
    /// <c>Password1!</c> on every installation that has one; length is what actually costs an
    /// attacker anything.
    /// </remarks>
    private const int MinimumPasswordLength = 12;

    /// <summary>Maps the administration endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/admin").RequireAuthorization().WithTags("Administration");

        group.MapGet("/permissions", PermissionsAsync)
             .WithName("Permissions")
             .WithSummary("Every permission the installed modules declare, and what each one means.");

        group.MapGet("/users", UsersAsync)
             .WithName("Users")
             .WithSummary("Lists user accounts and the permission sets each holds.");

        group.MapPost("/users", CreateUserAsync)
             .WithName("CreateUser")
             .WithSummary("Creates a user account with a password they must change.");

        group.MapPut("/users/{userName}", UpdateUserAsync)
             .WithName("UpdateUser")
             .WithSummary("Changes a user's details or turns the account off.");

        group.MapPut("/users/{userName}/permission-sets", AssignSetsAsync)
             .WithName("AssignPermissionSets")
             .WithSummary("Sets which permission sets a user holds, replacing what they had.");

        group.MapPost("/users/{userName}/reset-password", ResetPasswordAsync)
             .WithName("ResetPassword")
             .WithSummary("Gives somebody a new password they must change on first use.");

        group.MapGet("/permission-sets", PermissionSetsAsync)
             .WithName("PermissionSets")
             .WithSummary("Lists permission sets and everything each one grants.");

        group.MapPost("/permission-sets", CreatePermissionSetAsync)
             .WithName("CreatePermissionSet")
             .WithSummary("Creates a permission set.");

        group.MapPut("/permission-sets/{code}", UpdatePermissionSetAsync)
             .WithName("UpdatePermissionSet")
             .WithSummary("Rewrites what a permission set grants.");

        group.MapDelete("/permission-sets/{code}", DeletePermissionSetAsync)
             .WithName("DeletePermissionSet")
             .WithSummary("Removes a permission set nobody holds.");

        return app;
    }

    private static IResult PermissionsAsync(IModuleCatalog modules, IUserContext user, HttpContext http)
    {
        if (!Can(user, SetReadPermission) && !Can(user, UserReadPermission))
        {
            return Forbidden(SetReadPermission, "see the permission list", http);
        }

        var rows = modules.Modules
            .SelectMany(m => m.Permissions)
            .OrderBy(static p => p.Module, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new
            {
                key = p.Key,
                module = p.Module,
                resource = p.Resource,
                action = p.Action.ToString(),
                displayName = p.DisplayName.For(user.Culture),
                description = p.Description?.For(user.Culture),

                // Flagged, not hidden. Somebody assembling a set needs to see which lines are the
                // ones worth arguing about, and hiding them would only mean granting them blind.
                isSensitive = p.IsSensitive,
                implies = p.Implies,
            });

        return Results.Ok(rows);
    }

    private static async Task<IResult> UsersAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, UserReadPermission))
        {
            return Forbidden(UserReadPermission, "see user accounts", http);
        }

        var users = await context.Set<User>()
            .AsNoTracking()
            .Include(u => u.Assignments)
            .ThenInclude(a => a.PermissionSet)
            .OrderBy(static u => u.UserName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(users.Select(View));
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        AsapDbContext context,
        IMessageCatalog messages,
        ITenantContext tenant,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UserCreatePermission))
        {
            return Forbidden(UserCreatePermission, "create user accounts", http);
        }

        if (TooShort(request.TemporaryPassword, messages) is { } tooShort)
        {
            return Refused(tooShort, http);
        }

        var existing = await context.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserName == request.UserName, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Refused(
                messages.Render(
                    PlatformMessages.UserNameTaken,
                    Args(("UserName", request.UserName), ("ExistingName", existing.DisplayName))),
                http);
        }

        var created = new User
        {
            TenantId = tenant.TenantId ?? Guid.Empty,
            UserName = request.UserName,
            DisplayName = request.DisplayName,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.TemporaryPassword, workFactor: 12),

            // Given by somebody and known to them, so it is a shared secret until it is changed.
            MustChangePassword = true,
            DefaultCompanyId = tenant.CompanyId,
        };

        context.Set<User>().Add(created);

        var assigned = await AssignAsync(
            context,
            messages,
            tenant,
            created,
            request.PermissionSetCodes ?? [],
            cancellationToken).ConfigureAwait(false);

        if (assigned is not null)
        {
            return Refused(assigned, http);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(View(created));
    }

    private static async Task<IResult> UpdateUserAsync(
        string userName,
        UpdateUserRequest request,
        AsapDbContext context,
        IMessageCatalog messages,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UserWritePermission))
        {
            return Forbidden(UserWritePermission, "change user accounts", http);
        }

        var target = await LoadAsync(context, userName, cancellationToken).ConfigureAwait(false);

        if (target is null)
        {
            return Refused(NotFound(messages, userName), http);
        }

        if (request.IsActive is false)
        {
            if (target.Id == user.UserId)
            {
                return Refused(
                    messages.Render(PlatformMessages.CannotDisableSelf, Args(("UserName", userName))),
                    http);
            }

            if (await WouldStrandAsync(context, target, cancellationToken).ConfigureAwait(false))
            {
                return Refused(
                    messages.Render(PlatformMessages.LastAdministrator, Args(("UserName", userName))),
                    http);
            }
        }

        target.DisplayName = request.DisplayName ?? target.DisplayName;
        target.Email = request.Email ?? target.Email;
        target.Culture = request.Culture ?? target.Culture;

        if (request.IsActive is { } active)
        {
            target.IsActive = active;

            // A reactivated account starts its count again. Leaving it would lock somebody out
            // on their first mistake after being let back in.
            if (active)
            {
                target.FailedLoginCount = 0;
                target.LockedUntilUtc = null;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(View(target));
    }

    private static async Task<IResult> AssignSetsAsync(
        string userName,
        AssignSetsRequest request,
        AsapDbContext context,
        IMessageCatalog messages,
        ITenantContext tenant,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UserWritePermission))
        {
            return Forbidden(UserWritePermission, "change what a user may do", http);
        }

        var target = await LoadAsync(context, userName, cancellationToken).ConfigureAwait(false);

        if (target is null)
        {
            return Refused(NotFound(messages, userName), http);
        }

        context.Set<UserPermissionAssignment>().RemoveRange(target.Assignments);
        target.Assignments.Clear();

        var assigned = await AssignAsync(
            context,
            messages,
            tenant,
            target,
            request.PermissionSetCodes,
            cancellationToken).ConfigureAwait(false);

        if (assigned is not null)
        {
            return Refused(assigned, http);
        }

        // Checked after the change is staged rather than before, because what matters is whether
        // anybody can administer the installation afterwards, not whether this account could
        // before.
        if (await WouldStrandAsync(context, target, cancellationToken).ConfigureAwait(false))
        {
            return Refused(
                messages.Render(PlatformMessages.LastAdministrator, Args(("UserName", userName))),
                http);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(View(target));
    }

    private static async Task<IResult> ResetPasswordAsync(
        string userName,
        ResetPasswordRequest request,
        AsapDbContext context,
        IMessageCatalog messages,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UserWritePermission))
        {
            return Forbidden(UserWritePermission, "reset passwords", http);
        }

        var target = await LoadAsync(context, userName, cancellationToken).ConfigureAwait(false);

        if (target is null)
        {
            return Refused(NotFound(messages, userName), http);
        }

        if (TooShort(request.TemporaryPassword, messages) is { } tooShort)
        {
            return Refused(tooShort, http);
        }

        target.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.TemporaryPassword, workFactor: 12);
        target.MustChangePassword = true;

        // A reset is also how somebody locked out gets back in, so it clears the lock. Otherwise
        // the administrator does the obvious thing and it appears not to have worked.
        target.FailedLoginCount = 0;
        target.LockedUntilUtc = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(View(target));
    }

    private static async Task<IResult> PermissionSetsAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, SetReadPermission))
        {
            return Forbidden(SetReadPermission, "see permission sets", http);
        }

        var sets = await context.Set<PermissionSet>()
            .AsNoTracking()
            .Include(s => s.Entries)
            .OrderBy(static s => s.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var assigned = await context.Set<UserPermissionAssignment>()
            .AsNoTracking()
            .GroupBy(static a => a.PermissionSetId)
            .Select(static g => new { SetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(static x => x.SetId, static x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(sets.Select(s => new
        {
            code = s.Code,
            name = s.Name,
            nameArabic = s.NameArabic,
            description = s.Description,

            // A set ASAP maintains cannot be edited, and saying so up front is better than a
            // refusal after somebody has spent five minutes on it.
            isSystemDefined = s.IsSystemDefined,
            assignedTo = assigned.GetValueOrDefault(s.Id),
            permissions = s.Entries.Select(static e => e.PermissionKey).OrderBy(static k => k),
        }));
    }

    private static async Task<IResult> CreatePermissionSetAsync(
        SavePermissionSetRequest request,
        AsapDbContext context,
        IModuleCatalog modules,
        IMessageCatalog messages,
        ITenantContext tenant,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, SetCreatePermission))
        {
            return Forbidden(SetCreatePermission, "create permission sets", http);
        }

        var existing = await context.Set<PermissionSet>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == request.Code, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Refused(
                messages.Render(
                    PlatformMessages.PermissionSetCodeTaken,
                    Args(("Code", request.Code), ("ExistingName", existing.Name))),
                http);
        }

        if (Unknown(modules, messages, request.Permissions ?? []) is { } unknown)
        {
            return Refused(unknown, http);
        }

        var set = new PermissionSet
        {
            TenantId = tenant.TenantId ?? Guid.Empty,
            Code = request.Code,
            Name = request.Name,
            NameArabic = request.NameArabic,
            Description = request.Description,
        };

        foreach (var permission in Distinct(request.Permissions))
        {
            set.Entries.Add(new PermissionSetEntry
            {
                PermissionSetId = set.Id,
                PermissionKey = permission,
            });
        }

        context.Set<PermissionSet>().Add(set);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { code = set.Code, permissions = set.Entries.Count });
    }

    private static async Task<IResult> UpdatePermissionSetAsync(
        string code,
        SavePermissionSetRequest request,
        AsapDbContext context,
        IModuleCatalog modules,
        IMessageCatalog messages,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, SetWritePermission))
        {
            return Forbidden(SetWritePermission, "change permission sets", http);
        }

        var set = await context.Set<PermissionSet>()
            .Include(s => s.Entries)
            .FirstOrDefaultAsync(s => s.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (set is null)
        {
            return Refused(
                messages.Render(PlatformMessages.PermissionSetNotFound, Args(("Code", code))),
                http);
        }

        if (set.IsSystemDefined)
        {
            return Refused(
                messages.Render(PlatformMessages.PermissionSetIsSystem, Args(("Code", code))),
                http);
        }

        if (Unknown(modules, messages, request.Permissions ?? []) is { } unknown)
        {
            return Refused(unknown, http);
        }

        set.Name = request.Name;
        set.NameArabic = request.NameArabic;
        set.Description = request.Description;

        context.Set<PermissionSetEntry>().RemoveRange(set.Entries);
        set.Entries.Clear();

        foreach (var permission in Distinct(request.Permissions))
        {
            context.Set<PermissionSetEntry>().Add(new PermissionSetEntry
            {
                PermissionSetId = set.Id,
                PermissionKey = permission,
            });
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { code = set.Code, permissions = Distinct(request.Permissions).Count });
    }

    private static async Task<IResult> DeletePermissionSetAsync(
        string code,
        AsapDbContext context,
        IMessageCatalog messages,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, SetDeletePermission))
        {
            return Forbidden(SetDeletePermission, "remove permission sets", http);
        }

        var set = await context.Set<PermissionSet>()
            .Include(s => s.Entries)
            .FirstOrDefaultAsync(s => s.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (set is null)
        {
            return Refused(
                messages.Render(PlatformMessages.PermissionSetNotFound, Args(("Code", code))),
                http);
        }

        if (set.IsSystemDefined)
        {
            return Refused(
                messages.Render(PlatformMessages.PermissionSetIsSystem, Args(("Code", code))),
                http);
        }

        var holders = await context.Set<UserPermissionAssignment>()
            .CountAsync(a => a.PermissionSetId == set.Id, cancellationToken)
            .ConfigureAwait(false);

        if (holders > 0)
        {
            return Refused(
                messages.Render(
                    PlatformMessages.PermissionSetInUse,
                    Args(("Code", code), ("Count", holders))),
                http);
        }

        context.Set<PermissionSet>().Remove(set);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static Task<User?> LoadAsync(
        AsapDbContext context,
        string userName,
        CancellationToken cancellationToken)
        => context.Set<User>()
            .Include(u => u.Assignments)
            .ThenInclude(a => a.PermissionSet)
            .FirstOrDefaultAsync(u => u.UserName == userName, cancellationToken);

    private static async Task<AsapMessage?> AssignAsync(
        AsapDbContext context,
        IMessageCatalog messages,
        ITenantContext tenant,
        User target,
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        foreach (var code in codes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Tracked, deliberately. It is hung off the assignment below so the response can
            // name the set, and an untracked entity reached through a navigation is one EF has
            // never seen -- with a key already set, which it reads as a row to insert. The same
            // rule that makes a new child look like an existing row makes an existing parent
            // look like a new one.
            var set = await context.Set<PermissionSet>()
                .FirstOrDefaultAsync(s => s.Code == code, cancellationToken)
                .ConfigureAwait(false);

            if (set is null)
            {
                return messages.Render(PlatformMessages.PermissionSetNotFound, Args(("Code", code)));
            }

            var assignment = new UserPermissionAssignment
            {
                TenantId = tenant.TenantId ?? Guid.Empty,
                UserId = target.Id,
                PermissionSetId = set.Id,

                // Carried so the response can name the set. Without it the caller is handed back
                // a user with an empty list, having just been told the assignment succeeded --
                // which reads as a failure that returned two hundred.
                PermissionSet = set,
            };

            context.Set<UserPermissionAssignment>().Add(assignment);
            target.Assignments.Add(assignment);
        }

        return null;
    }

    /// <summary>
    /// Whether the change on the table would leave nobody able to administer the installation.
    /// </summary>
    /// <remarks>
    /// Counted over what is in the change tracker as well as the database, so it answers the
    /// question after the change rather than before it. A superuser counts; that is what a
    /// superuser is for.
    /// </remarks>
    private static async Task<bool> WouldStrandAsync(
        AsapDbContext context,
        User changing,
        CancellationToken cancellationToken)
    {
        var granting = await context.Set<PermissionSet>()
            .AsNoTracking()
            .Where(s => s.Entries.Any(e => e.PermissionKey == UserWritePermission
                                           || e.PermissionKey == "Platform.User.Create"))
            .Select(static s => s.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var others = await context.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id != changing.Id && u.IsActive)
            .Select(static u => new
            {
                u.IsSuperUser,
                SetIds = u.Assignments.Select(a => a.PermissionSetId).ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (others.Exists(u => u.IsSuperUser || u.SetIds.Exists(granting.Contains)))
        {
            return false;
        }

        // Nobody else can, so it comes down to whether this account still can once the change is
        // saved. A superuser who is being stripped of every set is not being stranded -- being a
        // superuser is the thing that was never in a set. Read from the tracked entity, which is
        // the change as it will be, not as it was.
        var stillActive = changing.IsActive;
        var stillGrants = changing.IsSuperUser
                          || changing.Assignments.Any(a => granting.Contains(a.PermissionSetId));

        return !(stillActive && stillGrants);
    }

    private static AsapMessage? Unknown(
        IModuleCatalog modules,
        IMessageCatalog messages,
        IReadOnlyList<string> permissions)
    {
        var declared = modules.Modules
            .SelectMany(static m => m.Permissions)
            .Select(static p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = permissions.FirstOrDefault(p => !declared.Contains(p));

        return missing is null
            ? null
            : messages.Render(PlatformMessages.PermissionUnknown, Args(("Permission", missing)));
    }

    private static AsapMessage? TooShort(string? password, IMessageCatalog messages)
        => (password?.Length ?? 0) >= MinimumPasswordLength
            ? null
            : messages.Render(
                PlatformMessages.PasswordTooShort,
                Args(("Minimum", MinimumPasswordLength), ("Length", password?.Length ?? 0)));

    private static AsapMessage NotFound(IMessageCatalog messages, string userName)
        => messages.Render(PlatformMessages.UserNotFound, Args(("UserName", userName)));

    private static List<string> Distinct(IReadOnlyList<string>? permissions)
        => [.. (permissions ?? []).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static object View(User user)
        => new
        {
            userName = user.UserName,
            displayName = user.DisplayName,
            email = user.Email,
            isActive = user.IsActive,
            isSuperUser = user.IsSuperUser,
            mustChangePassword = user.MustChangePassword,
            culture = user.Culture,
            lastLoginAtUtc = user.LastLoginAtUtc,
            lockedUntilUtc = user.LockedUntilUtc,
            permissionSets = user.Assignments
                .Select(static a => a.PermissionSet?.Code)
                .Where(static c => c is not null)
                .OrderBy(static c => c),
        };

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            Infrastructure.AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult Refused(AsapMessage message, HttpContext http)
    {
        var result = Result.Failure(message);

        return Results.Json(
            Infrastructure.AsapProblem.From(
                result,
                Infrastructure.AsapProblem.StatusFor(result.Messages),
                http.Request.Path),
            statusCode: Infrastructure.AsapProblem.StatusFor(result.Messages));
    }
}
