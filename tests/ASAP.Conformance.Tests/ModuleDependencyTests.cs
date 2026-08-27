using System.Reflection;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Modules;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// Holds the modules to the dependency rule in docs/architecture/module-dependencies.md.
/// </summary>
/// <remarks>
/// <para>
/// A module may depend on modules below it, must declare every such dependency in
/// <see cref="IAsapModule.DependsOn"/>, and may never take part in a cycle.
/// </para>
/// <para>
/// The declaration is what drives load order and licence gating, so a module that references
/// another without declaring it will load in the wrong order the first time the order matters --
/// and will work perfectly until then. That is the worst kind of defect to leave lying around,
/// because the code that eventually breaks is not the code that was wrong.
/// </para>
/// </remarks>
public sealed class ModuleDependencyTests
{
    private static readonly IAsapModule[] Modules =
    [
        new PlatformModule(),
        new ASAP.Modules.Finance.FinanceModule(),
        new ASAP.Modules.Inventory.InventoryModule(),
        new ASAP.Modules.Purchasing.PurchasingModule(),
        new ASAP.Modules.Sales.SalesModule(),
        new ASAP.Modules.Pos.PosModule(),
    ];

    private static readonly Dictionary<string, IAsapModule> ById =
        Modules.ToDictionary(static m => m.ModuleId, StringComparer.OrdinalIgnoreCase);

    /// <summary>Which module each module's assembly belongs to, for reading references.</summary>
    private static readonly Dictionary<string, string> ModuleByAssembly =
        Modules.ToDictionary(
            static m => m.GetType().Assembly.GetName().Name!,
            static m => m.ModuleId,
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Every_reference_between_modules_is_declared()
    {
        var undeclared = new List<string>();

        foreach (var module in Modules)
        {
            var declared = module.DependsOn.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var referenced = module.GetType().Assembly
                .GetReferencedAssemblies()
                .Select(static a => a.Name!)
                .Select(name => ModuleByAssembly.GetValueOrDefault(name))
                .Where(id => id is not null && id != module.ModuleId)
                .Select(static id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var dependency in referenced.Where(d => !declared.Contains(d)))
            {
                undeclared.Add($"{module.ModuleId} references {dependency} without declaring it");
            }
        }

        undeclared.ShouldBeEmpty(
            $"{undeclared.Count} undeclared dependency(s). Add them to DependsOn:"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", undeclared));
    }

    [Fact]
    public void Every_declared_dependency_names_a_module_that_exists()
    {
        // A typo here is invisible: the resolver has nothing to order against, so the module
        // simply loads whenever it likes.
        var dangling = Modules
            .SelectMany(static m => m.DependsOn.Select(d => (Module: m.ModuleId, Dependency: d)))
            .Where(static pair => !ById.ContainsKey(pair.Dependency))
            .Select(static pair => $"{pair.Module} depends on {pair.Dependency}, which does not exist")
            .ToList();

        dangling.ShouldBeEmpty(string.Join(Environment.NewLine, dangling));
    }

    [Fact]
    public void Inventory_and_finance_stay_siblings()
    {
        // The rule that keeps both saleable on their own. A warehouse tracking stock for a parent
        // company that keeps its own books needs Inventory without Finance; most service
        // businesses need Finance without Inventory. They trade through the kernel instead.
        Referenced("Inventory").ShouldNotContain("Finance");
        Referenced("Finance").ShouldNotContain("Inventory");
    }

    [Fact]
    public void No_module_depends_on_itself_directly_or_through_others()
    {
        foreach (var module in Modules)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>(module.DependsOn);

            while (pending.Count > 0)
            {
                var next = pending.Dequeue();

                next.ShouldNotBe(
                    module.ModuleId,
                    $"{module.ModuleId} reaches itself through its dependencies");

                if (!seen.Add(next) || !ById.TryGetValue(next, out var dependency))
                {
                    continue;
                }

                foreach (var further in dependency.DependsOn)
                {
                    pending.Enqueue(further);
                }
            }
        }
    }

    private static List<string> Referenced(string moduleId)
        => [.. ById[moduleId].GetType().Assembly
            .GetReferencedAssemblies()
            .Select(static a => a.Name!)
            .Select(name => ModuleByAssembly.GetValueOrDefault(name))
            .Where(id => id is not null && id != moduleId)
            .Select(static id => id!)];
}
