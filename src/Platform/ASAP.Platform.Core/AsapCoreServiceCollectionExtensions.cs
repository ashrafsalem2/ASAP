using ASAP.Platform.Core.Cqrs;
using ASAP.Platform.Core.Events;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ASAP.Platform.Core;

/// <summary>Registers the ASAP platform core.</summary>
public static class AsapCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the platform services and lets each module register its own.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Host configuration, handed to each module.</param>
    /// <param name="modules">Every discovered module, in any order.</param>
    /// <returns>The module catalogue, in load order, so the host can go on to apply schema and seed.</returns>
    /// <exception cref="InvalidOperationException">
    /// The module graph does not hold together, or a module declared something incoherent. Both
    /// are refused here rather than discovered by a user mid-month-end.
    /// </exception>
    public static IModuleCatalog AddAsapCore(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<IAsapModule> modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);

        // Ordering happens first, and throws on a bad graph.
        var catalog = new ModuleCatalog(modules);

        services.AddSingleton<IModuleCatalog>(catalog);

        RegisterMessages(services, catalog);
        RegisterPermissions(services, catalog);

        services.TryAddScoped<IDispatcher, Dispatcher>();
        services.TryAddScoped<IEventPublisher, EventPublisher>();

        // Open generic, so every request gets the permission check without anyone registering
        // it per request type -- which is precisely the sort of thing that gets forgotten.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PermissionBehavior<,>));

        foreach (var module in catalog.Modules)
        {
            module.ConfigureServices(services, configuration);
        }

        return catalog;
    }

    /// <summary>
    /// Builds the message catalogue from the platform plus every module, and validates it now.
    /// </summary>
    /// <remarks>
    /// Constructed once during registration purely to force validation. A blocking message with
    /// no resolution, or two modules claiming one code, stops the host here rather than surfacing
    /// on the day someone first triggers it.
    /// </remarks>
    private static void RegisterMessages(IServiceCollection services, IModuleCatalog catalog)
    {
        var definitions = PlatformMessages.All
            .Concat(catalog.Modules.SelectMany(static m => m.Messages))
            .ToList();

        _ = new MessageCatalog(definitions);

        services.AddSingleton<IReadOnlyCollection<MessageDefinition>>(definitions);

        // Scoped, not singleton: rendering reads the caller language, so one instance cannot
        // serve an English accountant and an Arabic cashier at the same time.
        services.TryAddScoped<IMessageCatalog>(provider => new MessageCatalog(
            provider.GetRequiredService<IReadOnlyCollection<MessageDefinition>>(),
            provider.GetService<IUserContext>()));
    }

    /// <summary>
    /// Collects the permissions every module declares, so the administration screen can offer
    /// them and the resolver can follow their implications.
    /// </summary>
    private static void RegisterPermissions(IServiceCollection services, IModuleCatalog catalog)
    {
        var declared = catalog.Modules.SelectMany(static m => m.Permissions).ToList();

        var duplicates = declared
            .GroupBy(static p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "Two modules declare the same permission key, so a grant would be ambiguous: "
                + string.Join(", ", duplicates));
        }

        services.AddSingleton<IReadOnlyCollection<PermissionDescriptor>>(declared);
        services.TryAddSingleton(new Security.PermissionResolver(declared));
    }
}
