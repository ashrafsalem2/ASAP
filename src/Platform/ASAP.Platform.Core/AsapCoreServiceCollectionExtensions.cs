using ASAP.Platform.Core.Cqrs;
using ASAP.Platform.Core.Events;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
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

        // What each module said branches hold a copy of. Built once, and it throws when two
        // modules claim the same published name -- a failure worth having at startup rather than
        // at a shop applying two different tables under one name.
        services.AddSingleton(new Sync.SyncRegistry(catalog.Modules));

        RegisterMessages(services, catalog);
        RegisterPermissions(services, catalog);
        RegisterSetups(services, catalog);

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

    /// <summary>
    /// Collects the settings every module declares, so the setup screen can offer them and the
    /// setup service can resolve them.
    /// </summary>
    /// <remarks>
    /// Two modules claiming one key is refused here rather than resolved by load order. Whichever
    /// won would depend on the dependency graph, so the same installation could read a different
    /// value after an unrelated module was installed.
    /// </remarks>
    private static void RegisterSetups(IServiceCollection services, IModuleCatalog catalog)
    {
        var declared = catalog.Modules.SelectMany(static m => m.Setups).ToList();

        var duplicates = declared
            .GroupBy(static s => s.Key, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "Two modules declare the same setup key, so reading it would be ambiguous: "
                + string.Join(", ", duplicates));
        }

        var invalid = declared
            .Where(static s => s.ValueType is SetupValueType.Option && s.AllowedValues.Count == 0)
            .Select(static s => s.Key)
            .ToList();

        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                "These settings are declared as Option but list no allowed values, so the setup "
                + "screen would show an empty dropdown: " + string.Join(", ", invalid));
        }

        services.AddSingleton<IReadOnlyCollection<SetupDescriptor>>(declared);
    }
}
