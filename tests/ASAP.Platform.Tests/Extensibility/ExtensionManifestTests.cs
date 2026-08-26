using System.Reflection;
using ASAP.Platform.Extensibility;
using Shouldly;

namespace ASAP.Platform.Tests.Extensibility;

public sealed class ExtensionManifestTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "asap-manifest-tests",
        Guid.CreateVersion7().ToString("N"));

    public ExtensionManifestTests() => Directory.CreateDirectory(_folder);

    private string WriteManifest(string json)
    {
        var path = Path.Combine(_folder, ExtensionManifest.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    private static ExtensionManifest Valid() => new()
    {
        Id = "Altuwijri.LoyaltyPoints",
        Name = "Loyalty Points",
        Version = "1.2.0",
        Publisher = "Altuwijri IT",
        Assembly = "Altuwijri.LoyaltyPoints.dll",
        PlatformVersion = "1.0",
    };

    [Fact]
    public void Reads_a_well_formed_manifest()
    {
        var path = WriteManifest("""
            {
              "id": "Altuwijri.LoyaltyPoints",
              "name": "Loyalty Points",
              "version": "1.2.0",
              "publisher": "Altuwijri IT",
              "assembly": "Altuwijri.LoyaltyPoints.dll",
              "platformVersion": "1.0",
              "requires": [ "Sales", "Pos" ]
            }
            """);

        ExtensionManifest.TryLoad(path, out var manifest, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        manifest.ShouldNotBeNull();
        manifest.Id.ShouldBe("Altuwijri.LoyaltyPoints");
        manifest.Requires.ShouldBe(["Sales", "Pos"]);
    }

    [Fact]
    public void Rejects_an_assembly_entry_that_is_a_path()
    {
        // The manifest arrives with the extension. A path here would let a dropped-in folder
        // point the loader at any file on the server.
        var manifest = Valid() with { Assembly = "../../../Windows/System32/evil.dll" };

        manifest.Validate().ShouldContain(p => p.Contains("plain file name"));
    }

    [Theory]
    [InlineData("sub/folder/plugin.dll")]
    [InlineData("sub\\folder\\plugin.dll")]
    [InlineData("..\\plugin.dll")]
    public void Rejects_any_directory_separator_in_the_assembly_name(string assembly)
    {
        (Valid() with { Assembly = assembly }).Validate().ShouldNotBeEmpty();
    }

    [Fact]
    public void Rejects_an_assembly_that_is_not_a_dll()
    {
        (Valid() with { Assembly = "plugin.exe" }).Validate()
            .ShouldContain(p => p.Contains("must name a .dll"));
    }

    [Fact]
    public void Rejects_a_version_that_is_not_a_version()
    {
        (Valid() with { Version = "latest" }).Validate()
            .ShouldContain(p => p.Contains("not a version number"));
    }

    [Fact]
    public void Accepts_a_sound_manifest()
    {
        Valid().Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Reports_unreadable_json_rather_than_throwing()
    {
        var path = WriteManifest("{ this is not json");

        ExtensionManifest.TryLoad(path, out var manifest, out var error).ShouldBeFalse();

        manifest.ShouldBeNull();
        error.ShouldNotBeNull().ShouldContain("could not be read");
    }

    [Fact]
    public void Reports_a_missing_file_rather_than_throwing()
    {
        var path = Path.Combine(_folder, "does-not-exist.json");

        ExtensionManifest.TryLoad(path, out _, out var error).ShouldBeFalse();

        error.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("1.0", "1.0", true)]   // exact match
    [InlineData("1.0", "1.4", true)]   // platform has moved on within the major version
    [InlineData("1.2", "1.0", false)]  // extension needs a newer minor than the platform has
    [InlineData("1.0", "2.0", false)]  // kernel contracts changed
    [InlineData("2.0", "1.9", false)]  // extension is from the future
    public void Decides_compatibility_by_major_match_and_minor_floor(
        string required,
        string running,
        bool compatible)
    {
        // The major version must match exactly: a change there means the kernel contracts moved,
        // and loading anyway would surface as a MissingMethodException at some later and much
        // less explicable moment.
        var manifest = Valid() with { PlatformVersion = required };

        manifest.IsCompatibleWith(Version.Parse(running)).ShouldBe(compatible);
    }

    [Fact]
    public void An_unparseable_platform_version_is_never_compatible()
    {
        (Valid() with { PlatformVersion = "any" }).IsCompatibleWith(new Version(1, 0)).ShouldBeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}

public sealed class ExtensionLoadContextTests
{
    [Theory]
    [InlineData("ASAP.Platform.Kernel")]
    [InlineData("asap.platform.kernel")]
    [InlineData("ASAP.Extensions.Sdk")]
    [InlineData("Microsoft.Extensions.DependencyInjection.Abstractions")]
    public void Contract_assemblies_come_from_the_host(string name)
    {
        // If an extension loaded its own copy of the kernel, the IAsapModule it implements would
        // be a different type from the one the host looks for, and the cast would fail with a
        // message that reads as nonsense.
        ExtensionLoadContext.IsSharedContract(new AssemblyName(name)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Newtonsoft.Json")]
    [InlineData("ASAP.Platform.Core")]
    [InlineData("Altuwijri.LoyaltyPoints")]
    [InlineData("SomeVendor.Utilities")]
    public void Everything_else_is_private_to_the_extension(string name)
    {
        // Two vendors must each be able to carry their own version of a third-party library
        // without one of them losing to whichever loaded first.
        ExtensionLoadContext.IsSharedContract(new AssemblyName(name)).ShouldBeFalse();
    }
}
