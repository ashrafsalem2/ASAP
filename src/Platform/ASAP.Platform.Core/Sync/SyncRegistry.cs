using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Sync;

namespace ASAP.Platform.Core.Sync;

/// <summary>
/// What every loaded module has said it synchronises.
/// </summary>
/// <remarks>
/// <para>
/// Built once at startup from the modules that implement <see cref="ISyncContributor"/>. A module
/// that says nothing synchronises nothing, which is the right default: silently replicating an
/// entity nobody thought about is how a branch comes to hold a copy of the audit log.
/// </para>
/// <para>
/// Two modules claiming the same published name fails here rather than at a branch. A branch
/// receiving <c>Inventory.Item</c> changes from two different tables would apply them over each
/// other, and the symptom would appear a long way from the cause.
/// </para>
/// </remarks>
public sealed class SyncRegistry
{
    private readonly Dictionary<Type, SyncEntityDescriptor> _byClrType = [];
    private readonly Dictionary<string, SyncEntityDescriptor> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the registry from the loaded modules.</summary>
    /// <param name="modules">Every module the host has loaded.</param>
    /// <exception cref="InvalidOperationException">Two modules claim the same published name.</exception>
    public SyncRegistry(IEnumerable<IAsapModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var descriptor in modules
                     .OfType<ISyncContributor>()
                     .SelectMany(static m => m.SyncEntities))
        {
            if (_byName.TryGetValue(descriptor.EntityType, out var existing))
            {
                throw new InvalidOperationException(
                    $"Both {existing.Module} and {descriptor.Module} publish sync changes as "
                    + $"'{descriptor.EntityType}'. A branch applying two different tables under "
                    + "one name would overwrite one with the other, and the symptom would appear "
                    + "a long way from the cause. Give one of them a different name.");
            }

            _byName[descriptor.EntityType] = descriptor;
            _byClrType[descriptor.ClrType] = descriptor;
        }

        All = [.. _byName.Values.OrderBy(static d => d.EntityType, StringComparer.Ordinal)];
    }

    /// <summary>Everything that synchronises, in a stable order.</summary>
    public IReadOnlyList<SyncEntityDescriptor> All { get; }

    /// <summary>Everything head office sends to branches.</summary>
    public IEnumerable<SyncEntityDescriptor> Downward
        => All.Where(static d => d.Direction is SyncDirection.Down);

    /// <summary>Everything branches send to head office.</summary>
    public IEnumerable<SyncEntityDescriptor> Upward
        => All.Where(static d => d.Direction is SyncDirection.Up);

    /// <summary>
    /// What this saved entity publishes as, if anything.
    /// </summary>
    /// <remarks>
    /// Walks the base types as well as the exact one, so a module can register a base class and
    /// have every proxy or subclass of it captured. An entity nobody registered returns null and
    /// is not captured, which is how the audit log stays out of the feed.
    /// </remarks>
    /// <param name="clrType">The entity type as saved.</param>
    /// <returns>Its descriptor, or null when it does not synchronise.</returns>
    public SyncEntityDescriptor? Describe(Type clrType)
    {
        ArgumentNullException.ThrowIfNull(clrType);

        for (var candidate = clrType; candidate is not null; candidate = candidate.BaseType)
        {
            if (_byClrType.TryGetValue(candidate, out var descriptor))
            {
                return descriptor;
            }
        }

        return null;
    }

    /// <summary>What this published name means, if this deployment knows it.</summary>
    /// <param name="entityType">The published name.</param>
    /// <returns>Its descriptor, or null when no loaded module publishes it.</returns>
    public SyncEntityDescriptor? Describe(string entityType)
        => _byName.GetValueOrDefault(entityType);
}
