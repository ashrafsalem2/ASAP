using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASAP.Extensions.Sdk;

/// <summary>
/// The base an extension is written on.
/// </summary>
/// <remarks>
/// <para>
/// An extension is an <see cref="IAsapModule"/> and nothing more, so this class adds no power. It
/// exists because the interface has fourteen members and a first extension needs four of them,
/// and a page of empty overrides between the author and the thing they came to write is a page
/// that teaches them nothing.
/// </para>
/// <para>
/// Everything shipped in ASAP is a module declared the same way. That is not a coincidence and it
/// is the point of the arrangement: an extension is not a lesser kind of thing bolted to the side,
/// it is the same kind of thing, and anything ASAP's own modules can do an extension can do too.
/// </para>
/// </remarks>
public abstract class AsapExtension : IAsapModule
{
    /// <inheritdoc />
    public abstract string ModuleId { get; }

    /// <inheritdoc />
    public abstract LocalizedText DisplayName { get; }

    /// <inheritdoc />
    public virtual LocalizedText Description => DisplayName;

    /// <inheritdoc />
    public virtual Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public abstract string Publisher { get; }

    /// <summary>
    /// The modules this one needs. Empty for an extension that stands alone.
    /// </summary>
    /// <remarks>
    /// Naming a module here is a promise the platform keeps: the load order is worked out from
    /// these, and an extension whose dependency is missing is refused at startup rather than
    /// failing later on the one screen that needed it.
    /// </remarks>
    public virtual IReadOnlyCollection<string> DependsOn => [];

    /// <summary>
    /// The licence feature this extension is sold under, or null when it is always on.
    /// </summary>
    /// <remarks>
    /// Defaults to null rather than to the module id, which is the opposite of what ASAP's own
    /// modules do. An extension somebody wrote for their own company should not need a licence
    /// entry before it will start; one written to be sold sets this.
    /// </remarks>
    public virtual string? LicenseFeature => null;

    /// <inheritdoc />
    public virtual IReadOnlyCollection<PermissionDescriptor> Permissions => [];

    /// <inheritdoc />
    public virtual IReadOnlyCollection<MessageDefinition> Messages => [];

    /// <inheritdoc />
    public virtual IReadOnlyCollection<SetupDescriptor> Setups => [];

    /// <inheritdoc />
    public virtual IReadOnlyCollection<NavigationItem> Navigation => [];

    /// <summary>
    /// Registers whatever the extension needs from the container.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <remarks>
    /// Does nothing by default. An extension that only declares permissions, settings or messages
    /// has nothing to register, and having to write an empty method to say so teaches the author
    /// that the framework is in charge rather than them.
    /// </remarks>
    public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Nothing by default.
    }

    /// <summary>
    /// Builds a permission key belonging to this extension.
    /// </summary>
    /// <param name="resource">What is being acted on, for example <c>Warranty</c>.</param>
    /// <param name="action">What may be done to it.</param>
    /// <returns>The key, such as <c>Acme.Warranty.Read</c>.</returns>
    /// <remarks>
    /// Built rather than typed. A permission key spelled one way in the declaration and another in
    /// the check is a permission that can be granted and never takes effect, and the difference is
    /// a capital letter nobody sees.
    /// </remarks>
    protected string Permission(string resource, PermissionAction action)
        => PermissionDescriptor.BuildKey(ModuleId, resource, action);

    /// <summary>
    /// Builds a setting key belonging to this extension.
    /// </summary>
    /// <param name="name">What the setting is called, for example <c>Warranty.Months</c>.</param>
    /// <returns>The key, prefixed with the module id.</returns>
    /// <remarks>
    /// Settings are stored by key across every module in the installation, so the prefix is what
    /// keeps two extensions from quietly sharing one.
    /// </remarks>
    protected string Setting(string name) => $"{ModuleId}.{name}";

    /// <summary>
    /// Builds a message code belonging to this extension.
    /// </summary>
    /// <param name="area">The part of the extension it comes from.</param>
    /// <param name="name">What went wrong, in capitals with underscores.</param>
    /// <returns>The code, such as <c>ACME.WARRANTY.EXPIRED</c>.</returns>
    protected MessageCode Code(string area, string name)
        => new($"{ModuleId.ToUpperInvariant()}.{area.ToUpperInvariant()}.{name.ToUpperInvariant()}");
}
