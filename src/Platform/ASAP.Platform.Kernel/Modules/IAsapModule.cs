using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Platform.Kernel.Modules;

/// <summary>
/// One sellable, independently deployable piece of ASAP: Finance, Inventory, Sales, Point of
/// Sale, HR, or a third-party extension.
/// </summary>
/// <remarks>
/// <para>
/// A module declares everything it contributes rather than reaching into the host to register
/// it. That declaration is what makes ASAP genuinely modular: the host can enumerate what a
/// module brings before loading it, refuse to load one whose dependencies or licence are not
/// satisfied, and produce the permission screen, the setup screen, the menu and the developer
/// documentation from the declarations alone.
/// </para>
/// <para>
/// This interface deliberately mentions no database type. A module that only adds behaviour
/// never has to reference Entity Framework; one that adds tables implements
/// <c>IModuleSchema</c> from the persistence layer as well.
/// </para>
/// </remarks>
public interface IAsapModule
{
    /// <summary>
    /// Stable identifier, for example <c>Finance</c>. Used in permission keys, setup keys and
    /// licensing, so it must never change once the module has shipped.
    /// </summary>
    string ModuleId { get; }

    /// <summary>Name shown wherever the module is listed.</summary>
    LocalizedText DisplayName { get; }

    /// <summary>What the module is for, shown on the module management screen.</summary>
    LocalizedText Description { get; }

    /// <summary>Module version, used to order schema upgrades and to report compatibility.</summary>
    Version Version { get; }

    /// <summary>Who publishes the module. <c>ASAP</c> for the built-in ones.</summary>
    string Publisher => "ASAP";

    /// <summary>
    /// Identifiers of modules that must load first. The host sorts modules topologically and
    /// refuses to start on a missing or circular dependency, rather than failing later in a way
    /// that is hard to diagnose.
    /// </summary>
    IReadOnlyCollection<string> DependsOn => [];

    /// <summary>
    /// The licence feature that enables this module. Null means it is part of the platform and
    /// always available. This is what lets a customer buy Finance and Inventory without Payroll.
    /// </summary>
    string? LicenseFeature => ModuleId;

    /// <summary>
    /// Permissions the module offers. Collected at startup into the permission screen, so a
    /// module cannot guard something with a permission an administrator has no way to grant.
    /// </summary>
    IReadOnlyCollection<PermissionDescriptor> Permissions => [];

    /// <summary>
    /// Messages the module can raise. Registering them up front means every diagnostic is
    /// translatable and documented before it is ever shown, and the startup check rejects a
    /// blocking message that offers the user no way forward.
    /// </summary>
    IReadOnlyCollection<MessageDefinition> Messages => [];

    /// <summary>Settings the module offers. Collected at startup into the setup screen.</summary>
    IReadOnlyCollection<SetupDescriptor> Setups => [];

    /// <summary>Menu entries the module contributes.</summary>
    IReadOnlyCollection<NavigationItem> Navigation => [];

    /// <summary>
    /// Registers the module services, handlers and event subscribers in the container.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Host configuration, for connection strings and the like.</param>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
