using System.Reflection;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using Microsoft.AspNetCore.Mvc;

namespace ASAP.Api.Endpoints;

/// <summary>
/// The developer reference, generated from what the installation actually declares.
/// </summary>
/// <remarks>
/// <para>
/// Every message, permission, setting and event ASAP has is declared in code and registered at
/// startup. Writing a reference by hand from those declarations would be transcription, and a
/// transcription is out of date the first time somebody adds a message and forgets the document.
/// This reads the same registries the running system does, so it cannot be out of date without
/// the system being wrong.
/// </para>
/// <para>
/// It describes <em>this</em> installation, extensions included. An extension's messages,
/// permissions and settings appear here beside the shipped ones, which is the answer to "what
/// can I actually integrate with" for somebody holding a deployment rather than a source tree.
/// </para>
/// </remarks>
public static class ReferenceEndpoints
{
    /// <summary>Maps the developer reference endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/reference").RequireAuthorization().WithTags("Reference");

        group.MapGet("/", SummaryAsync)
             .WithName("Reference")
             .WithSummary("What this installation declares: modules, and how much each brings.");

        group.MapGet("/modules/{moduleId}", ModuleAsync)
             .WithName("ModuleReference")
             .WithSummary("Everything one module declares: messages, permissions, settings, menu.");

        group.MapGet("/messages", MessagesAsync)
             .WithName("MessageReference")
             .WithSummary("Every message code, its severity, and what it says in both languages.");

        group.MapGet("/events", Events)
             .WithName("EventReference")
             .WithSummary("Every domain event an extension can subscribe to or raise.");

        return app;
    }

    private static IResult SummaryAsync(
        IModuleCatalog modules,
        IReadOnlyCollection<MessageDefinition> messages,
        IUserContext user)
    {
        var byModule = messages
            .GroupBy(static m => m.Code.Value.Split('.')[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return Results.Ok(new
        {
            modules = modules.Modules.Select(m => new
            {
                moduleId = m.ModuleId,
                displayName = m.DisplayName.For(user.Culture),
                description = m.Description.For(user.Culture),
                version = m.Version.ToString(),
                dependsOn = m.DependsOn,
                messages = m.Messages.Count,
                permissions = m.Permissions.Count,
                settings = m.Setups.Count,
                menuEntries = m.Navigation.Count,
            }),

            // The platform's own, which belong to no module and are otherwise invisible.
            platform = new
            {
                messages = PlatformMessages.All.Count,
                totalMessages = messages.Count,
                byPrefix = byModule.OrderBy(static p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static p => new { prefix = p.Key, count = p.Value }),
            },
        });
    }

    private static IResult ModuleAsync(
        string moduleId,
        IModuleCatalog modules,
        IUserContext user)
    {
        var module = modules.Modules
            .FirstOrDefault(m => string.Equals(m.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));

        if (module is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            moduleId = module.ModuleId,
            displayName = module.DisplayName.For(user.Culture),
            description = module.Description.For(user.Culture),
            version = module.Version.ToString(),
            dependsOn = module.DependsOn,

            permissions = module.Permissions.Select(p => new
            {
                key = p.Key,
                displayName = p.DisplayName.For(user.Culture),
                description = p.Description?.For(user.Culture),
                isSensitive = p.IsSensitive,
                implies = p.Implies,
            }),

            settings = module.Setups.Select(s => new
            {
                key = s.Key,
                displayName = s.DisplayName.For(user.Culture),
                description = s.Description.For(user.Culture),
                valueType = s.ValueType.ToString(),
                scope = s.Scope.ToString(),
                defaultValue = s.DefaultValue,
                requiresPermission = s.RequiresPermission,
                helpTopic = s.HelpTopic,
            }),

            messages = module.Messages.Select(m => new
            {
                code = m.Code.Value,
                severity = m.Severity.ToString(),
                title = m.Title.For(user.Culture),

                // The placeholders are what an integrator needs: they are the contract between
                // the message and whatever raises it, and the only part of a message that is not
                // free to change.
                placeholders = Placeholders(m),
                overridePermission = m.OverridePermission,
                helpTopic = m.HelpTopic,
            }),

            menu = module.Navigation.Select(n => new
            {
                id = n.Id,
                displayName = n.DisplayName.For(user.Culture),
                kind = n.Kind.ToString(),
                route = n.Route,
                requiresPermission = n.RequiresPermission,
            }),
        });
    }

    private static IResult MessagesAsync(
        IReadOnlyCollection<MessageDefinition> messages,
        [FromQuery] string? prefix)
    {
        var matching = messages
            .Where(m => prefix is null
                        || m.Code.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static m => m.Code.Value, StringComparer.OrdinalIgnoreCase);

        return Results.Ok(matching.Select(m => new
        {
            code = m.Code.Value,
            severity = m.Severity.ToString(),

            // Both languages, because the reference is read by somebody deciding whether to
            // branch on a code or on its wording, and seeing both makes the answer obvious.
            title = new { en = m.Title.English, ar = m.Title.Arabic },
            detail = m.Detail is { } detail ? new { en = detail.English, ar = detail.Arabic } : null,
            resolution = m.Resolution is { } resolution
                ? new { en = resolution.English, ar = resolution.Arabic }
                : null,
            placeholders = Placeholders(m),
            overridePermission = m.OverridePermission,
            helpTopic = m.HelpTopic,
        }));
    }

    /// <summary>
    /// Every domain event, found by looking for what implements the contract.
    /// </summary>
    /// <remarks>
    /// Reflected over the loaded assemblies rather than declared on a module, because an event is
    /// a type and a module that had to list its own would one day not. What an extension can
    /// subscribe to is exactly what is loaded, so that is what is asked.
    /// </remarks>
    private static IResult Events()
    {
        var events = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(static a => a.GetName().Name?.StartsWith("ASAP", StringComparison.Ordinal) == true)
            .SelectMany(static a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch (ReflectionTypeLoadException loaded)
                {
                    // A half-loaded extension should narrow the list rather than empty it.
                    return loaded.Types.Where(static t => t is not null)!;
                }
            })
            .Where(static t => t is { IsAbstract: false, IsInterface: false }
                               && typeof(IDomainEvent).IsAssignableFrom(t))
            .OrderBy(static t => t!.FullName, StringComparer.Ordinal)
            .Select(static t => new
            {
                type = t!.FullName,
                assembly = t.Assembly.GetName().Name,

                // Whether an extension can stop it. A vetoable event is a decision point; an
                // ordinary one has already happened and is being announced.
                isVetoable = typeof(ASAP.Platform.Kernel.Events.VetoableEvent).IsAssignableFrom(t),
                properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(static p => new { name = p.Name, type = Readable(p.PropertyType) }),
            });

        return Results.Ok(events);
    }

    /// <summary>The placeholder names a message uses, across all three of its parts.</summary>
    private static IReadOnlyCollection<string> Placeholders(MessageDefinition message)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var text in new[] { message.Title, message.Detail, message.Resolution })
        {
            if (text is not { } value)
            {
                continue;
            }

            foreach (var name in MessageTemplateRenderer.PlaceholdersIn(value.English))
            {
                names.Add(name);
            }
        }

        return [.. names.OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A type name a person can read, rather than one with backticks in it.</summary>
    private static string Readable(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);

        if (underlying is not null)
        {
            return $"{Readable(underlying)}?";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        var arguments = string.Join(", ", type.GetGenericArguments().Select(Readable));

        return $"{name}<{arguments}>";
    }
}
