using ASAP.Platform.Kernel.Modules;

namespace ASAP.Platform.Core.Modules;

/// <summary>
/// Decides whether a tenant has licensed a module.
/// </summary>
/// <remarks>
/// Separate from the catalogue because licensing is a commercial question that will change shape
/// -- a subscription service, a signed licence file, a per-seat count -- while "which modules are
/// loaded" will not.
/// </remarks>
public interface IModuleLicenseCheck
{
    /// <summary>
    /// Whether a tenant may use a module.
    /// </summary>
    /// <param name="licenseFeature">The feature the module requires, from its declaration.</param>
    /// <param name="tenantId">The tenant, or null for the active one.</param>
    bool IsLicensed(string licenseFeature, Guid? tenantId);
}

/// <summary>
/// The modules this instance has loaded, in dependency order.
/// </summary>
/// <remarks>
/// Built once at startup and read-only thereafter, so it is safe to share as a singleton.
/// </remarks>
public sealed class ModuleCatalog : IModuleCatalog
{
    private readonly Dictionary<string, IAsapModule> _byId;
    private readonly IModuleLicenseCheck? _licenseCheck;

    /// <summary>
    /// Builds the catalogue, ordering the modules by dependency.
    /// </summary>
    /// <param name="modules">Every discovered module, in any order.</param>
    /// <param name="licenseCheck">
    /// Decides what a tenant has bought. Null treats every loaded module as available, which is
    /// what a single-tenant on-premise install wants.
    /// </param>
    /// <exception cref="InvalidOperationException">The module graph does not hold together.</exception>
    public ModuleCatalog(IEnumerable<IAsapModule> modules, IModuleLicenseCheck? licenseCheck = null)
    {
        Modules = ModuleDependencyResolver.Sort(modules);
        _licenseCheck = licenseCheck;
        _byId = Modules.ToDictionary(static m => m.ModuleId, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<IAsapModule> Modules { get; }

    /// <inheritdoc />
    public IAsapModule? Find(string moduleId)
        => string.IsNullOrWhiteSpace(moduleId) ? null : _byId.GetValueOrDefault(moduleId);

    /// <inheritdoc />
    public bool IsAvailable(string moduleId, Guid? tenantId = null)
    {
        if (Find(moduleId) is not { } module)
        {
            return false;
        }

        // A module with no licence feature is part of the platform and always available.
        if (module.LicenseFeature is not { } feature)
        {
            return true;
        }

        if (_licenseCheck is null)
        {
            return true;
        }

        // A module is only usable if everything it depends on is too. Without this, a tenant
        // licensed for Point of Sale but not Inventory would load a till that cannot resolve a
        // stock level, and the failure would surface as a confusing error at the counter rather
        // than as a clear licensing message.
        foreach (var dependency in module.DependsOn)
        {
            if (!IsAvailable(dependency, tenantId))
            {
                return false;
            }
        }

        return _licenseCheck.IsLicensed(feature, tenantId);
    }
}
