using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Modules;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// Holds the menu to the promises it makes about itself.
/// </summary>
/// <remarks>
/// <para>
/// The menu is assembled from every installed module, so no single file shows the whole of it and
/// nobody reviewing one module can see a collision with another. The client keys its rendering on
/// the entry id, which means a duplicate id produces a menu that renders wrongly and warns in a
/// console nobody is reading.
/// </para>
/// <para>
/// This exists because of exactly that: one entry was declared twice, identically, in the same
/// file. It shipped, it worked well enough to look right, and the only complaint was a browser
/// warning during an unrelated piece of work.
/// </para>
/// </remarks>
public sealed class NavigationTests
{
    /// <summary>Every module ASAP ships.</summary>
    private static readonly IAsapModule[] Modules =
    [
        new PlatformModule(),
        new ASAP.Modules.Finance.FinanceModule(),
        new ASAP.Modules.Inventory.InventoryModule(),
        new ASAP.Modules.Purchasing.PurchasingModule(),
        new ASAP.Modules.Promotions.PromotionsModule(),
        new ASAP.Modules.Hr.HrModule(),
        new ASAP.Modules.Sales.SalesModule(),
        new ASAP.Modules.Pos.PosModule(),
    ];

    private static readonly List<NavigationItem> Menu =
        [.. Modules.SelectMany(static m => m.Navigation)];

    [Fact]
    public void No_menu_entry_is_declared_twice()
    {
        var duplicates = Menu
            .GroupBy(static i => i.Id, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => $"{g.Key} ×{g.Count()} — {string.Join(" / ", g.Select(static i => i.Route ?? "no route"))}")
            .ToList();

        duplicates.ShouldBeEmpty(
            $"{duplicates.Count} menu id(s) are declared more than once, which the client keys "
            + $"its rendering on:" + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", duplicates));
    }

    [Fact]
    public void Every_entry_hangs_off_a_group_that_exists()
    {
        var ids = new HashSet<string>(Menu.Select(static i => i.Id), StringComparer.OrdinalIgnoreCase);

        var orphans = Menu
            .Where(static i => i.ParentId is { Length: > 0 })
            .Where(i => !ids.Contains(i.ParentId!))
            .Select(static i => $"{i.Id} hangs off {i.ParentId}, which nothing declares")
            .ToList();

        // An entry under a group that does not exist is an entry nobody will ever see: the menu
        // is built by walking down from the roots, and it is never reached.
        orphans.ShouldBeEmpty(
            $"{orphans.Count} menu entr(ies) point at a parent that is not there:"
            + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", orphans));
    }

    [Fact]
    public void Every_entry_asks_for_a_permission_that_exists()
    {
        var declared = new HashSet<string>(
            Modules.SelectMany(static m => m.Permissions).Select(static p => p.Key),
            StringComparer.OrdinalIgnoreCase);

        var unreachable = Menu
            .Where(static i => i.RequiresPermission is { Length: > 0 })
            .Where(i => !declared.Contains(i.RequiresPermission!))
            .Select(static i => $"{i.Id} needs {i.RequiresPermission}, which nothing declares")
            .ToList();

        // An entry guarded by a permission nobody declares is an entry nobody can be granted, so
        // it is invisible to every user including the administrator. It looks like a feature that
        // was never finished, and it is usually a feature that was finished and misspelt.
        unreachable.ShouldBeEmpty(
            $"{unreachable.Count} menu entr(ies) ask for a permission that does not exist:"
            + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", unreachable));
    }

    [Fact]
    public void No_two_entries_lead_to_the_same_screen()
    {
        var duplicates = Menu
            .Where(static i => i.Route is { Length: > 0 })
            .GroupBy(static i => i.Route!, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => $"{g.Key} — {string.Join(", ", g.Select(static i => i.Id))}")
            .ToList();

        // Two entries on one route is a menu that lists the same screen twice under different
        // names, which reads as two features and is one.
        duplicates.ShouldBeEmpty(
            $"{duplicates.Count} route(s) are reached from more than one menu entry:"
            + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", duplicates));
    }
}
