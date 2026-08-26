using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Core.Setup;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Platform.Persistence;

/// <summary>
/// Reads and writes ASAP setup values.
/// </summary>
/// <remarks>
/// <para>
/// Resolution walks outwards from the narrowest scope that has a value: user, then branch, then
/// company, then tenant, then the value the module declared as its default. That is what lets
/// head office set a policy once for the company while one branch overrides only the part that
/// genuinely differs.
/// </para>
/// <para>
/// Only overrides are stored. A setting left alone has no row at all, so a fresh company runs
/// correctly with an empty table, and an upgrade that changes a default reaches every customer
/// who never overrode it.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="declared">Every setting the loaded modules declared.</param>
/// <param name="tenantContext">Supplies the scopes a lookup resolves through.</param>
/// <param name="userContext">Checks permission to change a setting.</param>
/// <param name="messages">Renders refusals.</param>
public sealed class SetupService(
    AsapDbContext context,
    IReadOnlyCollection<SetupDescriptor> declared,
    ITenantContext tenantContext,
    IUserContext userContext,
    IMessageCatalog messages) : ISetupService
{
    private readonly Dictionary<string, SetupDescriptor> _declared =
        declared.ToDictionary(static d => d.Key, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyCollection<SetupDescriptor> Declared => declared;

    /// <inheritdoc />
    public SetupDescriptor? Describe(string key)
        => string.IsNullOrWhiteSpace(key) ? null : _declared.GetValueOrDefault(key);

    /// <inheritdoc />
    public async ValueTask<TValue> GetAsync<TValue>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Require(key);

        var rows = await LoadAsync(key, cancellationToken).ConfigureAwait(false);

        // Narrowest first. The first scope with a row wins; nothing found falls through to the
        // declared default.
        var value =
            Find(rows, SetupScope.User, userContext.UserId)
            ?? Find(rows, SetupScope.Branch, tenantContext.BranchId)
            ?? Find(rows, SetupScope.Company, tenantContext.CompanyId)
            ?? Find(rows, SetupScope.Tenant, null)
            ?? descriptor.DefaultValue;

        return SetupValueConverter.Parse<TValue>(value)!;
    }

    /// <inheritdoc />
    public async ValueTask<TValue?> GetAtScopeAsync<TValue>(
        string key,
        SetupScope scope,
        Guid? scopeId = null,
        CancellationToken cancellationToken = default)
    {
        Require(key);

        var rows = await LoadAsync(key, cancellationToken).ConfigureAwait(false);

        return SetupValueConverter.Parse<TValue>(Find(rows, scope, scopeId));
    }

    /// <inheritdoc />
    public async Task<Result> SetAsync(
        string key,
        string? value,
        SetupScope scope = SetupScope.Company,
        Guid? scopeId = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Require(key);

        if (Refuse(descriptor, value, scope) is { } refusal)
        {
            return refusal;
        }

        if (descriptor.IsLockedAfterFirstPosting
            && await HasPostedEntriesAsync(cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(messages.Render(
                PlatformMessages.SetupLocked,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Setting"] = descriptor.DisplayName.For(userContext.Culture),
                    ["Company"] = "this company",
                },
                MessageTarget.OnField(key)));
        }

        var effectiveScopeId = scope is SetupScope.Tenant ? null : scopeId ?? DefaultScopeId(scope);

        var existing = await context.SetupValues
            .FirstOrDefaultAsync(
                s => s.Key == key && s.Scope == scope && s.ScopeId == effectiveScopeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (value is null)
        {
            // Clearing an override removes the row so the wider scope applies again. Storing a
            // null would be indistinguishable from a deliberate empty string.
            if (existing is not null)
            {
                context.SetupValues.Remove(existing);
            }
        }
        else if (existing is null)
        {
            context.SetupValues.Add(new SetupValue
            {
                TenantId = tenantContext.TenantId ?? Guid.Empty,
                Key = key,
                Scope = scope,
                ScopeId = effectiveScopeId,
                Value = value,
                IsEncrypted = descriptor.ValueType is SetupValueType.Secret,
            });
        }
        else
        {
            existing.Value = value;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Checks everything that would make a change unacceptable, before touching the database.
    /// </summary>
    private Result? Refuse(SetupDescriptor descriptor, string? value, SetupScope scope)
    {
        if (descriptor.RequiresPermission is { } permission
            && !userContext.Has(permission)
            && !userContext.IsSuperUser)
        {
            return Result.Failure(messages.Render(
                PlatformMessages.PermissionDenied,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Operation"] = $"Changing '{descriptor.DisplayName.For(userContext.Culture)}'",
                    ["Permission"] = permission,
                    ["Company"] = "this company",
                },
                MessageTarget.OnField(descriptor.Key)));
        }

        // A setting declared at company scope must not be overridden per branch. Costing method
        // is the case this exists for: letting one shop value stock differently from the company
        // it belongs to would make the inventory account impossible to reconcile.
        if (scope > descriptor.Scope && !descriptor.AllowsNarrowerOverride)
        {
            return Result.Failure(messages.Render(
                PlatformMessages.SetupValueInvalid,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Setting"] = descriptor.DisplayName.For(userContext.Culture),
                    ["Expected"] = $"a value set at {descriptor.Scope} level, not per {scope}",
                    ["Value"] = value ?? string.Empty,
                },
                MessageTarget.OnField(descriptor.Key)));
        }

        if (SetupValueConverter.Validate(descriptor, value) is { } problem)
        {
            return Result.Failure(messages.Render(
                PlatformMessages.SetupValueInvalid,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Setting"] = descriptor.DisplayName.For(userContext.Culture),
                    ["Expected"] = problem,
                    ["Value"] = value ?? string.Empty,
                },
                MessageTarget.OnField(descriptor.Key)));
        }

        return null;
    }

    private SetupDescriptor Require(string key)
        => Describe(key) ?? throw new KeyNotFoundException(
            $"No module declares the setting '{key}'. Reading or writing an undeclared setting is "
            + "a bug: add a SetupDescriptor to the module that owns it, so it appears on the "
            + "setup screen and in the documentation.");

    private Guid? DefaultScopeId(SetupScope scope) => scope switch
    {
        SetupScope.Company => tenantContext.CompanyId,
        SetupScope.Branch => tenantContext.BranchId,
        SetupScope.User => userContext.UserId,
        _ => null,
    };

    private async Task<List<SetupValue>> LoadAsync(string key, CancellationToken cancellationToken)
        => await context.SetupValues
            .AsNoTracking()
            .Where(s => s.Key == key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static string? Find(List<SetupValue> rows, SetupScope scope, Guid? scopeId)
    {
        // A scope with nothing to identify it -- no branch on the request, say -- cannot match a
        // row, so it is skipped rather than matching every branch row indiscriminately.
        if (scope is not SetupScope.Tenant && scopeId is null)
        {
            return null;
        }

        return rows.Find(s => s.Scope == scope && s.ScopeId == scopeId)?.Value;
    }

    private async Task<bool> HasPostedEntriesAsync(CancellationToken cancellationToken)
        => tenantContext.CompanyId is { } companyId
           && await context.Companies
               .AsNoTracking()
               .Where(c => c.Id == companyId)
               .Select(static c => c.HasPostedEntries)
               .FirstOrDefaultAsync(cancellationToken)
               .ConfigureAwait(false);
}
