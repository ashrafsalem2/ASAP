using ASAP.Platform.Kernel.Modules;

namespace ASAP.Platform.Core.Modules;

/// <summary>
/// Puts modules in an order where nothing loads before what it depends on.
/// </summary>
/// <remarks>
/// <para>
/// Load order decides the order schema is applied, seed data is written and event handlers are
/// registered. Inventory expects Finance chart of accounts to exist before it can name a stock
/// account; Point of Sale expects both. Getting the order wrong produces a failure a long way
/// from its cause, so the graph is resolved once at startup and the host refuses to serve traffic
/// if it does not hold together.
/// </para>
/// <para>
/// Every problem found is reported at once. Being told about one missing dependency, fixing it,
/// restarting, and being told about the next is a poor way to spend an afternoon.
/// </para>
/// </remarks>
public static class ModuleDependencyResolver
{
    /// <summary>
    /// Orders modules by dependency.
    /// </summary>
    /// <param name="modules">The loaded modules, in any order.</param>
    /// <param name="ordered">The modules, dependencies first.</param>
    /// <param name="problems">Everything wrong with the graph, or empty when it is sound.</param>
    /// <returns>True when the graph resolved.</returns>
    public static bool TrySort(
        IEnumerable<IAsapModule> modules,
        out IReadOnlyList<IAsapModule> ordered,
        out IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var all = modules.ToList();
        var found = new List<string>();

        var byId = new Dictionary<string, IAsapModule>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in all)
        {
            if (!byId.TryAdd(module.ModuleId, module))
            {
                found.Add(
                    $"Module '{module.ModuleId}' is loaded twice, from "
                    + $"'{byId[module.ModuleId].GetType().Assembly.GetName().Name}' and "
                    + $"'{module.GetType().Assembly.GetName().Name}'. Module identifiers must be unique.");
            }
        }

        foreach (var module in all)
        {
            foreach (var dependency in module.DependsOn)
            {
                if (!byId.ContainsKey(dependency))
                {
                    found.Add(
                        $"Module '{module.ModuleId}' depends on '{dependency}', which is not loaded. "
                        + "Either install it or remove the dependency.");
                }
            }
        }

        if (found.Count > 0)
        {
            ordered = [];
            problems = found;
            return false;
        }

        // Kahn's algorithm. Ties are broken by module identifier rather than by whatever order
        // the assemblies happened to be discovered in, so the same set of modules always loads
        // in the same order and a load-order bug reproduces instead of coming and going.
        var remainingDependencies = all.ToDictionary(
            static m => m.ModuleId,
            static m => new HashSet<string>(m.DependsOn, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in all)
        {
            foreach (var dependency in module.DependsOn)
            {
                if (!dependents.TryGetValue(dependency, out var list))
                {
                    dependents[dependency] = list = [];
                }

                list.Add(module.ModuleId);
            }
        }

        var ready = new PriorityQueue<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, dependencies) in remainingDependencies.Where(static kvp => kvp.Value.Count == 0))
        {
            ready.Enqueue(id, id);
        }

        var result = new List<IAsapModule>(all.Count);

        while (ready.TryDequeue(out var id, out _))
        {
            result.Add(byId[id]);

            if (!dependents.TryGetValue(id, out var waiting))
            {
                continue;
            }

            foreach (var dependent in waiting)
            {
                var pending = remainingDependencies[dependent];
                pending.Remove(id);

                if (pending.Count == 0)
                {
                    ready.Enqueue(dependent, dependent);
                }
            }
        }

        if (result.Count != all.Count)
        {
            // Whatever is left is caught in a cycle, or depends on something that is.
            var stuck = remainingDependencies
                .Where(kvp => kvp.Value.Count > 0)
                .OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => $"  - '{kvp.Key}' still waiting on: {string.Join(", ", kvp.Value.Order())}");

            ordered = [];
            problems =
            [
                "Module dependencies form a cycle, so no load order exists:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, stuck),
            ];
            return false;
        }

        ordered = result;
        problems = [];
        return true;
    }

    /// <summary>
    /// Orders modules by dependency, refusing to continue if the graph does not hold together.
    /// </summary>
    /// <param name="modules">The loaded modules, in any order.</param>
    /// <returns>The modules, dependencies first.</returns>
    /// <exception cref="InvalidOperationException">
    /// A dependency is missing, duplicated or circular. Thrown at startup, deliberately, because
    /// an ERP that boots into an incoherent module graph will discover the problem in the middle
    /// of a month-end close instead.
    /// </exception>
    public static IReadOnlyList<IAsapModule> Sort(IEnumerable<IAsapModule> modules)
    {
        if (TrySort(modules, out var ordered, out var problems))
        {
            return ordered;
        }

        throw new InvalidOperationException(
            "ASAP cannot determine a module load order and will not start:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(static p => "  " + p)));
    }
}
