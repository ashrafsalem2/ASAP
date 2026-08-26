using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Platform.Kernel.Security;

/// <summary>
/// One permission a module offers, declared at startup so it can appear on the permission
/// screen without anyone maintaining a separate list.
/// </summary>
/// <remarks>
/// Modules declare permissions rather than the platform enumerating them. That is what lets a
/// third-party extension add its own permissions and have them show up in the administration
/// UI, grouped under the extension, alongside the built-in ones.
/// </remarks>
public sealed record PermissionDescriptor
{
    /// <summary>
    /// The permission key, shaped <c>Module.Resource.Action</c>, for example
    /// <c>Finance.Journal.Post</c>. Case-insensitive, and stable across versions.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Module that owns the permission, for example <c>Finance</c>.</summary>
    public required string Module { get; init; }

    /// <summary>Resource the permission guards, for example <c>Journal</c>.</summary>
    public required string Resource { get; init; }

    /// <summary>The verb being granted.</summary>
    public required PermissionAction Action { get; init; }

    /// <summary>Name shown on the permission screen.</summary>
    public required LocalizedText DisplayName { get; init; }

    /// <summary>What granting this actually lets someone do, in plain language for the administrator.</summary>
    public LocalizedText? Description { get; init; }

    /// <summary>
    /// Permissions this one automatically carries. Granting <c>Finance.Journal.Post</c> implies
    /// <c>Finance.Journal.Read</c>, since posting something you cannot see is meaningless. Saves
    /// administrators from assembling every grant by hand and from the mistakes that invites.
    /// </summary>
    public IReadOnlyCollection<string> Implies { get; init; } = [];

    /// <summary>
    /// True for permissions that carry real risk: overriding margin protection, posting into a
    /// prior period, deleting master data. The permission screen highlights these, and the
    /// audit log records every use.
    /// </summary>
    public bool IsSensitive { get; init; }

    /// <summary>Builds a key from its parts.</summary>
    /// <param name="module">Owning module.</param>
    /// <param name="resource">Guarded resource.</param>
    /// <param name="action">Verb granted.</param>
    public static string BuildKey(string module, string resource, PermissionAction action)
        => $"{module}.{resource}.{action}";

    /// <summary>
    /// Declares a permission, deriving its key from the parts so the two can never drift apart.
    /// </summary>
    /// <param name="module">Owning module, for example <c>Finance</c>.</param>
    /// <param name="resource">Guarded resource, for example <c>Journal</c>.</param>
    /// <param name="action">Verb granted.</param>
    /// <param name="displayName">Name shown on the permission screen.</param>
    /// <param name="description">What granting this lets someone do.</param>
    /// <param name="implies">Permissions this one automatically carries.</param>
    /// <param name="isSensitive">True for permissions that carry real risk.</param>
    public static PermissionDescriptor Define(
        string module,
        string resource,
        PermissionAction action,
        LocalizedText displayName,
        LocalizedText? description = null,
        IReadOnlyCollection<string>? implies = null,
        bool isSensitive = false)
        => new()
        {
            Key = BuildKey(module, resource, action),
            Module = module,
            Resource = resource,
            Action = action,
            DisplayName = displayName,
            Description = description,
            Implies = implies ?? [],
            IsSensitive = isSensitive,
        };

    /// <inheritdoc />
    public override string ToString() => Key;
}
