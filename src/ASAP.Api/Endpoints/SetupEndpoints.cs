using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace ASAP.Api.Endpoints;

/// <summary>What a client sends to change a setting.</summary>
/// <param name="Value">
/// The new value in its string form, or null to clear the override and fall back to the wider
/// scope. Clearing is not the same as setting the default: a cleared setting follows the default
/// if the default later changes, and a set one does not.
/// </param>
/// <param name="Scope">Which level to set it at.</param>
/// <param name="ScopeId">Which company, branch or user. Ignored for tenant scope.</param>
public sealed record ChangeSettingRequest(
    string? Value,
    SetupScope Scope = SetupScope.Company,
    Guid? ScopeId = null);

/// <summary>
/// Every setting the installation has, and what each one is currently set to.
/// </summary>
/// <remarks>
/// <para>
/// Generated from what the modules declare, not written by hand. A setting cannot exist in code
/// without a name, an explanation, a type, a default and a permission — and because this screen
/// reads the same declarations, it cannot exist in code without appearing here either. That is
/// the whole point of declaring them: there is no hidden configuration to find out about from
/// somebody who remembers it.
/// </para>
/// <para>
/// An extension's settings arrive on the same screen under the extension's own heading, without
/// the extension having to build a screen.
/// </para>
/// </remarks>
public static class SetupEndpoints
{
    /// <summary>Maps the setup endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/setup").RequireAuthorization().WithTags("Setup");

        group.MapGet("/", ListAsync)
             .WithName("Settings")
             .WithSummary("Every declared setting, its value, and whether the caller may change it.");

        group.MapPut("/{key}", ChangeAsync)
             .WithName("ChangeSetting")
             .WithSummary("Changes one setting, or clears it back to the wider scope.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        ISetupService setup,
        IUserContext user,
        ITenantContext tenant,
        [FromQuery] string? module,
        CancellationToken cancellationToken)
    {
        var declared = setup.Declared
            .Where(d => module is null || string.Equals(d.Module, module, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static d => d.Module, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static d => d.Key, StringComparer.OrdinalIgnoreCase);

        var rows = new List<object>();

        foreach (var descriptor in declared)
        {
            // Read at the company the caller is working in. The service resolves the narrower
            // scopes over the wider ones, so this is the value that is actually in force.
            var value = await setup
                .GetAtScopeAsync<string>(descriptor.Key, SetupScope.Company, tenant.CompanyId, cancellationToken)
                .ConfigureAwait(false);

            rows.Add(new
            {
                key = descriptor.Key,
                module = descriptor.Module,
                group = descriptor.Group.For(user.Culture),
                displayName = descriptor.DisplayName.For(user.Culture),

                // Shown next to the input rather than behind a help icon. A setting whose effect
                // has to be guessed at is one somebody changes to see what happens.
                description = descriptor.Description.For(user.Culture),
                valueType = descriptor.ValueType.ToString(),
                scope = descriptor.Scope.ToString(),
                defaultValue = descriptor.DefaultValue,
                value,
                isSet = value is not null,
                referencedEntityType = descriptor.ReferencedEntityType,
                allowedValues = descriptor.AllowedValues
                    .Select(o => new { value = o.Value, label = o.Label.For(user.Culture) }),
                requiresPermission = descriptor.RequiresPermission,

                // Sent rather than left for the client to work out from the permission, so one
                // rule decides it and the screen cannot disagree with the endpoint.
                canChange = MayChange(user, descriptor),
                helpTopic = descriptor.HelpTopic,
            });
        }

        return Results.Ok(rows);
    }

    private static async Task<IResult> ChangeAsync(
        string key,
        ChangeSettingRequest request,
        ISetupService setup,
        ASAP.Platform.Kernel.Messaging.IMessageCatalog messages,
        ITenantContext tenant,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The service throws for an undeclared key, which is right when a module asks: reading a
        // setting nobody declared is a bug in that module. Here the key came off a URL, so it is
        // somebody's typing, and an answer beats a stack trace.
        if (setup.Describe(key) is null)
        {
            var unknown = messages.Render(
                ASAP.Platform.Core.Messaging.PlatformMessages.SetupUnknownKey,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Setting"] = key });

            return Results.Json(
                Infrastructure.AsapProblem.From(
                    ASAP.Platform.Kernel.Results.Result.Failure(unknown),
                    StatusCodes.Status404NotFound,
                    http.Request.Path),
                statusCode: StatusCodes.Status404NotFound);
        }

        // The permission, the type, the bounds and the scope are all checked by the service. It
        // is the same path a module takes, so a setting cannot be changed one way here and
        // another way in code.
        var result = await setup
            .SetAsync(
                key,
                request.Value,
                request.Scope,
                request.ScopeId ?? tenant.CompanyId,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return Results.Json(
                Infrastructure.AsapProblem.From(
                    result,
                    Infrastructure.AsapProblem.StatusFor(result.Messages),
                    http.Request.Path),
                statusCode: Infrastructure.AsapProblem.StatusFor(result.Messages));
        }

        var value = await setup
            .GetAtScopeAsync<string>(key, SetupScope.Company, tenant.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            key,
            value,
            isSet = value is not null,
            messages = Infrastructure.MessagePayload.FromAll(result.Messages),
        });
    }

    private static bool MayChange(IUserContext user, SetupDescriptor descriptor)
        => descriptor.RequiresPermission is not { Length: > 0 } permission
           || user.IsSuperUser
           || user.Has(permission);
}
