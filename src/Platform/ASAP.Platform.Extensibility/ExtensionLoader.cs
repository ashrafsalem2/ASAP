using System.Reflection;
using ASAP.Platform.Kernel.Modules;
using Microsoft.Extensions.Logging;

namespace ASAP.Platform.Extensibility;

/// <summary>How the host looks for extensions.</summary>
public sealed class ExtensionOptions
{
    /// <summary>
    /// Folder holding one subfolder per installed extension, relative to the host or absolute.
    /// </summary>
    public string Directory { get; set; } = "extensions";

    /// <summary>
    /// Whether an extension assembly must be digitally signed to load.
    /// </summary>
    /// <remarks>
    /// On by default. An extension runs with the full authority of the ASAP process: it reads
    /// every company books and can veto or alter any posting. Loading an unsigned assembly from
    /// a folder on disk means anyone who can write to that folder can do the same.
    /// </remarks>
    public bool RequireSignedAssemblies { get; set; } = true;

    /// <summary>Extension identifiers to skip, for taking a misbehaving one out of service.</summary>
    public IList<string> Disabled { get; } = [];
}

/// <summary>One extension that was found, whether or not it could be loaded.</summary>
/// <param name="Manifest">What it declared about itself.</param>
/// <param name="Path">Folder it was found in.</param>
/// <param name="Module">The module it contributed, or null when it did not load.</param>
/// <param name="Problem">Why it did not load, or null when it did.</param>
public sealed record ExtensionLoadResult(
    ExtensionManifest Manifest,
    string Path,
    IAsapModule? Module,
    string? Problem)
{
    /// <summary>Whether the extension loaded and contributed a module.</summary>
    public bool Loaded => Module is not null;
}

/// <summary>
/// Finds and loads the extensions installed alongside ASAP.
/// </summary>
/// <remarks>
/// A failing extension never stops the host. It is reported, skipped, and shown as failed on the
/// extension management screen. The alternative -- refusing to start because a third-party plugin
/// is broken -- would mean a vendor mistake closes the customer shops.
/// </remarks>
/// <param name="options">Where to look and what to require.</param>
/// <param name="platformVersion">The running platform version, checked against each manifest.</param>
/// <param name="logger">Records what was found and what was refused.</param>
public sealed class ExtensionLoader(
    ExtensionOptions options,
    Version platformVersion,
    ILogger<ExtensionLoader> logger)
{
    /// <summary>
    /// Scans the extension folder and loads everything usable.
    /// </summary>
    /// <param name="availableModuleIds">
    /// Module identifiers already loaded, so an extension requiring one that is absent is
    /// refused with a clear reason rather than failing when it first reaches for it.
    /// </param>
    /// <returns>Every extension found, loaded or not.</returns>
    public IReadOnlyList<ExtensionLoadResult> Load(IReadOnlySet<string> availableModuleIds)
    {
        ArgumentNullException.ThrowIfNull(availableModuleIds);

        var root = Path.GetFullPath(options.Directory);

        if (!System.IO.Directory.Exists(root))
        {
            logger.LogInformation("No extension folder at {Path}; running with built-in modules only.", root);
            return [];
        }

        var results = new List<ExtensionLoadResult>();

        foreach (var folder in System.IO.Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(folder, ExtensionManifest.FileName);

            if (!File.Exists(manifestPath))
            {
                logger.LogWarning(
                    "Folder {Folder} holds no {FileName} and was skipped.",
                    folder,
                    ExtensionManifest.FileName);
                continue;
            }

            if (!ExtensionManifest.TryLoad(manifestPath, out var manifest, out var error))
            {
                logger.LogError("Extension manifest rejected: {Error}", error);
                continue;
            }

            results.Add(LoadOne(manifest!, folder, availableModuleIds));
        }

        var loaded = results.Count(static r => r.Loaded);
        logger.LogInformation(
            "Extensions: {Loaded} loaded, {Failed} refused, from {Path}.",
            loaded,
            results.Count - loaded,
            root);

        return results;
    }

    private ExtensionLoadResult LoadOne(
        ExtensionManifest manifest,
        string folder,
        IReadOnlySet<string> availableModuleIds)
    {
        ExtensionLoadResult Refuse(string problem)
        {
            logger.LogError("Extension {Id} was not loaded: {Problem}", manifest.Id, problem);
            return new ExtensionLoadResult(manifest, folder, null, problem);
        }

        if (options.Disabled.Contains(manifest.Id, StringComparer.OrdinalIgnoreCase))
        {
            return Refuse("It is disabled in configuration.");
        }

        if (!manifest.IsCompatibleWith(platformVersion))
        {
            return Refuse(
                $"It targets ASAP platform {manifest.PlatformVersion}, and this is {platformVersion}. "
                + "Install a build of the extension that matches.");
        }

        var missing = manifest.Requires
            .Where(required => !availableModuleIds.Contains(required))
            .ToList();

        if (missing.Count > 0)
        {
            return Refuse(
                $"It requires module(s) that are not available here: {string.Join(", ", missing)}.");
        }

        var assemblyPath = Path.Combine(folder, manifest.Assembly);

        if (!File.Exists(assemblyPath))
        {
            return Refuse($"Its assembly '{manifest.Assembly}' is not in the folder.");
        }

        if (options.RequireSignedAssemblies && !AssemblySignature.IsSigned(assemblyPath, out var signatureProblem))
        {
            return Refuse(
                $"It is not signed, and this instance requires signed extensions ({signatureProblem}). "
                + "An extension runs with full access to every company books.");
        }

        try
        {
            var context = new ExtensionLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);

            var moduleType = assembly.GetTypes().FirstOrDefault(static t =>
                typeof(IAsapModule).IsAssignableFrom(t)
                && t is { IsAbstract: false, IsInterface: false });

            if (moduleType is null)
            {
                return Refuse(
                    $"'{manifest.Assembly}' contains no public class implementing IAsapModule. "
                    + "An extension must contribute exactly one module.");
            }

            if (Activator.CreateInstance(moduleType) is not IAsapModule module)
            {
                return Refuse($"'{moduleType.Name}' could not be constructed. It needs a parameterless constructor.");
            }

            logger.LogInformation(
                "Loaded extension {Id} {Version} by {Publisher}, contributing module {ModuleId}.",
                manifest.Id,
                manifest.Version,
                manifest.Publisher,
                module.ModuleId);

            return new ExtensionLoadResult(manifest, folder, module, null);
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Almost always a dependency the extension did not ship. Naming the missing pieces
            // saves its author a long afternoon.
            var detail = string.Join(
                "; ",
                ex.LoaderExceptions.Where(static e => e is not null).Select(static e => e!.Message).Distinct());

            return Refuse($"Its types could not be loaded, usually a missing dependency: {detail}");
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or TypeLoadException)
        {
            return Refuse($"Its assembly could not be loaded: {ex.Message}");
        }
    }
}
