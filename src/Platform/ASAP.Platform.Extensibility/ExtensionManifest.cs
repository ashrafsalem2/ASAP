using System.Text.Json;
using System.Text.Json.Serialization;

namespace ASAP.Platform.Extensibility;

/// <summary>
/// What an extension declares about itself, in an <c>asap-extension.json</c> beside its assembly.
/// </summary>
/// <remarks>
/// <para>
/// The manifest is read before the assembly is loaded. That order matters: it lets ASAP refuse an
/// extension built against an incompatible platform, or one whose required modules are not
/// installed, without ever executing its code.
/// </para>
/// <para>
/// A worked example:
/// </para>
/// <code>
/// {
///   "id": "Altuwijri.LoyaltyPoints",
///   "name": "Loyalty Points",
///   "version": "1.2.0",
///   "publisher": "Altuwijri IT",
///   "assembly": "Altuwijri.LoyaltyPoints.dll",
///   "platformVersion": "1.0",
///   "requires": [ "Sales", "Pos" ]
/// }
/// </code>
/// </remarks>
public sealed record ExtensionManifest
{
    /// <summary>The manifest file name ASAP looks for in each extension folder.</summary>
    public const string FileName = "asap-extension.json";

    /// <summary>
    /// Stable identifier, conventionally <c>Publisher.Name</c>. Used for licensing and for the
    /// folder the extension is installed into, so it must never change once released.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Name shown on the extension management screen.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Extension version.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Who publishes it.</summary>
    [JsonPropertyName("publisher")]
    public required string Publisher { get; init; }

    /// <summary>File name of the assembly holding the module, relative to the manifest.</summary>
    [JsonPropertyName("assembly")]
    public required string Assembly { get; init; }

    /// <summary>
    /// The lowest ASAP platform version this extension works against, as <c>major.minor</c>.
    /// </summary>
    /// <remarks>
    /// Checked before loading. A major version difference means the kernel contracts have changed
    /// in a way that breaks the extension, and loading it would surface as a
    /// <see cref="MissingMethodException"/> at some later, less explicable moment.
    /// </remarks>
    [JsonPropertyName("platformVersion")]
    public required string PlatformVersion { get; init; }

    /// <summary>
    /// Identifiers of ASAP modules this extension needs. An extension adding a loyalty scheme to
    /// the till needs Sales and Point of Sale; without them it has nothing to attach to.
    /// </summary>
    [JsonPropertyName("requires")]
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>What the extension does, shown on the management screen.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Where a user goes for support with it.</summary>
    [JsonPropertyName("supportUrl")]
    public string? SupportUrl { get; init; }

    /// <summary>
    /// Checks the manifest is complete and coherent.
    /// </summary>
    /// <returns>Everything wrong with it, or empty when it is sound.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Id))
        {
            problems.Add("'id' is required.");
        }

        if (string.IsNullOrWhiteSpace(Assembly))
        {
            problems.Add("'assembly' is required.");
        }
        else if (!Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"'assembly' must name a .dll file, not '{Assembly}'.");
        }
        else if (Assembly.Contains('/', StringComparison.Ordinal)
                 || Assembly.Contains('\\', StringComparison.Ordinal)
                 || Assembly.Contains("..", StringComparison.Ordinal))
        {
            // The manifest is supplied by whoever wrote the extension. A path here would let a
            // dropped-in folder point the loader at any file on the server.
            problems.Add($"'assembly' must be a plain file name, not a path: '{Assembly}'.");
        }

        if (!System.Version.TryParse(Version, out _))
        {
            problems.Add($"'version' is not a version number: '{Version}'.");
        }

        if (!System.Version.TryParse(PlatformVersion, out _))
        {
            problems.Add($"'platformVersion' is not a version number: '{PlatformVersion}'.");
        }

        return problems;
    }

    /// <summary>
    /// Whether this extension can run against a given platform version.
    /// </summary>
    /// <param name="platformVersion">The running platform version.</param>
    /// <remarks>
    /// The major version must match exactly, because a change there means the kernel contracts
    /// moved. The platform minor version must be at least what the extension asked for, since
    /// minor releases only add.
    /// </remarks>
    public bool IsCompatibleWith(Version platformVersion)
    {
        ArgumentNullException.ThrowIfNull(platformVersion);

        if (!System.Version.TryParse(PlatformVersion, out var required))
        {
            return false;
        }

        return required.Major == platformVersion.Major && platformVersion.Minor >= required.Minor;
    }

    /// <summary>
    /// Reads a manifest from disk.
    /// </summary>
    /// <param name="path">Path to the manifest file.</param>
    /// <param name="manifest">The manifest, when it could be read.</param>
    /// <param name="error">Why it could not be read.</param>
    public static bool TryLoad(string path, out ExtensionManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;

        try
        {
            var json = File.ReadAllText(path);
            manifest = JsonSerializer.Deserialize<ExtensionManifest>(json, SerializerOptions);

            if (manifest is null)
            {
                error = $"'{path}' is empty or contains only null.";
                return false;
            }

            var problems = manifest.Validate();

            if (problems.Count > 0)
            {
                manifest = null;
                error = $"'{path}' is invalid: {string.Join(" ", problems)}";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"'{path}' could not be read: {ex.Message}";
            return false;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
