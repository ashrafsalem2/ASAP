using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ASAP.Platform.Tests.Modules;

/// <summary>A module that exists only to be ordered.</summary>
internal sealed class FakeModule(string id, params string[] dependsOn) : IAsapModule
{
    public string ModuleId { get; } = id;

    public LocalizedText DisplayName => ModuleId;

    public LocalizedText Description => $"Fake {ModuleId} module";

    public Version Version { get; } = new(1, 0, 0);

    public IReadOnlyCollection<string> DependsOn { get; } = dependsOn;

    public string? LicenseFeature { get; init; } = id;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Nothing to register; these tests are about ordering, not composition.
    }
}

public sealed class ModuleDependencyResolverTests
{
    private static List<string> Ids(IEnumerable<IAsapModule> modules)
        => [.. modules.Select(static m => m.ModuleId)];

    [Fact]
    public void Puts_a_dependency_before_the_module_that_needs_it()
    {
        var ordered = ModuleDependencyResolver.Sort(
        [
            new FakeModule("Inventory", "Finance"),
            new FakeModule("Finance"),
        ]);

        Ids(ordered).ShouldBe(["Finance", "Inventory"]);
    }

    [Fact]
    public void Orders_a_realistic_erp_graph()
    {
        // The order ASAP actually loads in: finance underneath everything, inventory before the
        // modules that move stock, point of sale after sales.
        var ordered = ModuleDependencyResolver.Sort(
        [
            new FakeModule("Pos", "Sales", "Inventory"),
            new FakeModule("Sales", "Inventory", "Finance"),
            new FakeModule("Purchasing", "Inventory", "Finance"),
            new FakeModule("Inventory", "Finance"),
            new FakeModule("Finance"),
        ]);

        var ids = Ids(ordered);

        ids.IndexOf("Finance").ShouldBeLessThan(ids.IndexOf("Inventory"));
        ids.IndexOf("Inventory").ShouldBeLessThan(ids.IndexOf("Sales"));
        ids.IndexOf("Sales").ShouldBeLessThan(ids.IndexOf("Pos"));
        ids.IndexOf("Inventory").ShouldBeLessThan(ids.IndexOf("Purchasing"));
    }

    [Fact]
    public void Produces_the_same_order_however_the_modules_arrive()
    {
        // Assembly discovery order is not stable across machines. If load order followed it, a
        // load-order bug would reproduce on one developer machine and not another.
        var forwards = Ids(ModuleDependencyResolver.Sort(
        [
            new FakeModule("Finance"),
            new FakeModule("Hr"),
            new FakeModule("Inventory", "Finance"),
            new FakeModule("Crm"),
        ]));

        var backwards = Ids(ModuleDependencyResolver.Sort(
        [
            new FakeModule("Crm"),
            new FakeModule("Inventory", "Finance"),
            new FakeModule("Hr"),
            new FakeModule("Finance"),
        ]));

        forwards.ShouldBe(backwards);
    }

    [Fact]
    public void Reports_a_missing_dependency_by_name()
    {
        var resolved = ModuleDependencyResolver.TrySort(
            [new FakeModule("Pos", "Inventory")],
            out _,
            out var problems);

        resolved.ShouldBeFalse();
        problems.ShouldHaveSingleItem();
        problems[0].ShouldContain("'Pos' depends on 'Inventory', which is not loaded");
    }

    [Fact]
    public void Reports_every_missing_dependency_at_once()
    {
        // Being told about one, fixing it, restarting, and being told about the next is a poor
        // way to spend an afternoon.
        var resolved = ModuleDependencyResolver.TrySort(
        [
            new FakeModule("Pos", "Inventory"),
            new FakeModule("Sales", "Finance"),
        ],
            out _,
            out var problems);

        resolved.ShouldBeFalse();
        problems.Count.ShouldBe(2);
    }

    [Fact]
    public void Reports_a_cycle_and_names_what_is_stuck()
    {
        var resolved = ModuleDependencyResolver.TrySort(
        [
            new FakeModule("Sales", "Inventory"),
            new FakeModule("Inventory", "Sales"),
        ],
            out _,
            out var problems);

        resolved.ShouldBeFalse();
        problems[0].ShouldContain("cycle");
        problems[0].ShouldContain("Sales");
        problems[0].ShouldContain("Inventory");
    }

    [Fact]
    public void Reports_a_module_loaded_twice()
    {
        var resolved = ModuleDependencyResolver.TrySort(
        [
            new FakeModule("Finance"),
            new FakeModule("Finance"),
        ],
            out _,
            out var problems);

        resolved.ShouldBeFalse();
        problems[0].ShouldContain("loaded twice");
    }

    [Fact]
    public void Refuses_to_start_on_a_broken_graph()
    {
        // Startup is the right place to fail. An ERP that boots into an incoherent module graph
        // discovers the problem in the middle of a month-end close instead.
        var act = () => ModuleDependencyResolver.Sort([new FakeModule("Pos", "Inventory")]);

        act.ShouldThrow<InvalidOperationException>()
           .Message.ShouldContain("will not start");
    }

    [Fact]
    public void Handles_an_empty_set()
    {
        ModuleDependencyResolver.Sort([]).ShouldBeEmpty();
    }

    [Fact]
    public void Treats_a_dependency_name_case_insensitively()
    {
        // A module author writing "finance" rather than "Finance" should not produce a startup
        // failure that reads as a missing module.
        var ordered = ModuleDependencyResolver.Sort(
        [
            new FakeModule("Inventory", "finance"),
            new FakeModule("Finance"),
        ]);

        Ids(ordered).ShouldBe(["Finance", "Inventory"]);
    }
}

public sealed class ModuleCatalogTests
{
    private sealed class LicenseStub(params string[] licensed) : IModuleLicenseCheck
    {
        private readonly HashSet<string> _licensed = new(licensed, StringComparer.OrdinalIgnoreCase);

        public bool IsLicensed(string licenseFeature, Guid? tenantId) => _licensed.Contains(licenseFeature);
    }

    [Fact]
    public void Finds_a_module_by_id_without_regard_to_case()
    {
        var catalog = new ModuleCatalog([new FakeModule("Finance")]);

        catalog.Find("finance").ShouldNotBeNull().ModuleId.ShouldBe("Finance");
    }

    [Fact]
    public void Reports_an_unknown_module_as_unavailable()
    {
        var catalog = new ModuleCatalog([new FakeModule("Finance")]);

        catalog.Find("Payroll").ShouldBeNull();
        catalog.IsAvailable("Payroll").ShouldBeFalse();
    }

    [Fact]
    public void Treats_everything_as_available_when_nothing_checks_licences()
    {
        // The single-tenant on-premise case: the binaries present are the binaries bought.
        var catalog = new ModuleCatalog([new FakeModule("Finance"), new FakeModule("Pos", "Finance")]);

        catalog.IsAvailable("Pos").ShouldBeTrue();
    }

    [Fact]
    public void Reports_an_unlicensed_module_as_unavailable()
    {
        var catalog = new ModuleCatalog(
            [new FakeModule("Finance"), new FakeModule("Payroll")],
            new LicenseStub("Finance"));

        catalog.IsAvailable("Finance").ShouldBeTrue();
        catalog.IsAvailable("Payroll").ShouldBeFalse();
    }

    [Fact]
    public void A_module_is_unavailable_when_something_it_depends_on_is_not_licensed()
    {
        // A tenant licensed for Point of Sale but not Inventory would otherwise load a till that
        // cannot resolve a stock level, and the failure would appear at the counter as a
        // confusing error rather than as a clear licensing message.
        var catalog = new ModuleCatalog(
        [
            new FakeModule("Finance"),
            new FakeModule("Inventory", "Finance"),
            new FakeModule("Pos", "Inventory"),
        ],
            new LicenseStub("Finance", "Pos"));

        catalog.IsAvailable("Pos").ShouldBeFalse();
    }

    [Fact]
    public void A_platform_module_needs_no_licence()
    {
        var catalog = new ModuleCatalog(
            [new FakeModule("Core") { LicenseFeature = null }],
            new LicenseStub());

        catalog.IsAvailable("Core").ShouldBeTrue();
    }
}
