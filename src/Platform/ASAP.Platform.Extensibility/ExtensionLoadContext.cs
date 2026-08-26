using System.Reflection;
using System.Runtime.Loader;

namespace ASAP.Platform.Extensibility;

/// <summary>
/// Loads one extension and its private dependencies in isolation from the host and from every
/// other extension.
/// </summary>
/// <remarks>
/// <para>
/// Isolation is what lets two extensions from two vendors each carry their own version of some
/// third-party library without one of them losing. Without it, whichever loaded first would win
/// and the other would fail in a way its author could neither reproduce nor fix.
/// </para>
/// <para>
/// The exception is the contract assemblies. Those must be shared with the host, or the
/// <c>IAsapModule</c> the extension implements would be a different type from the
/// <c>IAsapModule</c> the host is looking for, and the cast would fail with a message that
/// reads as nonsense: a type not being assignable to an interface it plainly implements. See
/// <see cref="IsSharedContract"/>.
/// </para>
/// </remarks>
public sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>
    /// Assemblies that always come from the host rather than from the extension folder.
    /// </summary>
    /// <remarks>
    /// Kept deliberately short. Every assembly on this list is one an extension cannot upgrade
    /// independently, so it should hold the contracts and nothing else.
    /// </remarks>
    private static readonly string[] SharedContractPrefixes =
    [
        "ASAP.Platform.Kernel",
        "ASAP.Extensions.Sdk",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Configuration.Abstractions",
    ];

    /// <summary>
    /// Opens a load context for one extension.
    /// </summary>
    /// <param name="extensionAssemblyPath">Full path to the extension main assembly.</param>
    public ExtensionLoadContext(string extensionAssemblyPath)
        // Collectible, so an extension can be unloaded and replaced without restarting ASAP.
        // A shop cannot be asked to close the tills because head office updated a plugin.
        : base(name: Path.GetFileNameWithoutExtension(extensionAssemblyPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(extensionAssemblyPath);
    }

    /// <summary>
    /// Whether an assembly must come from the host rather than the extension folder.
    /// </summary>
    /// <param name="assemblyName">The assembly being resolved.</param>
    /// <remarks>
    /// A shared assembly is one whose types cross the boundary between host and extension. If the
    /// extension loaded its own copy, the two sides would be looking at two distinct types with
    /// the same name, and nothing would match.
    /// </remarks>
    public static bool IsSharedContract(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        if (assemblyName.Name is not { } name)
        {
            return false;
        }

        return Array.Exists(
            SharedContractPrefixes,
            prefix => string.Equals(name, prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Returning null hands resolution back to the default context, which is how the host
        // copy of a shared contract gets used.
        if (IsSharedContract(assemblyName))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);

        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <inheritdoc />
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
