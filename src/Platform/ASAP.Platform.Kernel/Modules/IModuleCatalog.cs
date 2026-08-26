namespace ASAP.Platform.Kernel.Modules;

/// <summary>
/// Every module the running instance has loaded, in dependency order.
/// </summary>
public interface IModuleCatalog
{
    /// <summary>
    /// The loaded modules, ordered so that a module always appears after everything it depends
    /// on. Startup routines walk this order when applying schema and seed data.
    /// </summary>
    IReadOnlyList<IAsapModule> Modules { get; }

    /// <summary>Finds a module by identifier.</summary>
    /// <param name="moduleId">The module identifier, matched case-insensitively.</param>
    /// <returns>The module, or null when it is not loaded.</returns>
    IAsapModule? Find(string moduleId);

    /// <summary>
    /// Whether a module is loaded and licensed for a given tenant.
    /// </summary>
    /// <param name="moduleId">The module identifier.</param>
    /// <param name="tenantId">The tenant to check, or null for the active one.</param>
    /// <remarks>
    /// Loaded and licensed are different things. A binary can be present on the server while a
    /// particular tenant has not bought it, which is exactly what selling ASAP module by module
    /// requires. Menu building and request authorisation both go through this.
    /// </remarks>
    bool IsAvailable(string moduleId, Guid? tenantId = null);
}
