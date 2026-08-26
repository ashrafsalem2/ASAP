using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Api.Endpoints;

/// <summary>One entry in the menu the client draws.</summary>
/// <param name="Id">Stable identifier, for remembering which groups the user left open.</param>
/// <param name="Module">The module that contributed it.</param>
/// <param name="DisplayName">The label, already in the user's language.</param>
/// <param name="Kind">Page, report, setup, task or group.</param>
/// <param name="Route">Where it navigates to. Null for a group.</param>
/// <param name="Icon">Icon name from the ASAP icon set.</param>
/// <param name="Children">Entries nested under this one.</param>
public sealed record MenuNode(
    string Id,
    string Module,
    string DisplayName,
    string Kind,
    string? Route,
    string? Icon,
    IReadOnlyList<MenuNode> Children);

/// <summary>The menu, and what the client needs to render the shell around it.</summary>
public static class NavigationEndpoints
{
    /// <summary>Maps the navigation endpoints.</summary>
    /// <param name="app">The route builder.</param>
    public static IEndpointRouteBuilder MapNavigationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/navigation", Menu)
           .RequireAuthorization()
           .WithTags("Navigation")
           .WithName("Navigation")
           .WithSummary("Returns the menu, filtered to what the caller may actually open.");

        return app;
    }

    /// <summary>
    /// Assembles the menu from what every loaded module declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Filtered to what this user may open, so nobody is shown a screen that will refuse them on
    /// arrival. That is a small thing that makes a large difference to how an ERP feels: a
    /// cashier's menu is six entries, not sixty with fifty-four dead ends.
    /// </para>
    /// <para>
    /// Assembled per request rather than cached, because it depends on the caller's permissions
    /// and their active company. It is a walk over a few dozen declarations held in memory.
    /// </para>
    /// <para>
    /// Needs no permission of its own beyond being signed in: every user needs a menu, and its
    /// contents are already narrowed to what they may open.
    /// </para>
    /// </remarks>
    private static IResult Menu(
        IModuleCatalog modules,
        IUserContext user,
        ITenantContext tenant)
    {
        var visible = modules.Modules
            .Where(m => modules.IsAvailable(m.ModuleId, tenant.TenantId))
            .SelectMany(static m => m.Navigation)
            .Where(item => item.RequiresPermission is null || user.Has(item.RequiresPermission))
            .ToList();

        var byParent = visible
            .Where(static i => i.ParentId is not null)
            .GroupBy(static i => i.ParentId!)
            .ToDictionary(static g => g.Key, static g => g.OrderBy(static i => i.Order).ToList());

        var roots = visible
            .Where(static i => i.ParentId is null)
            .OrderBy(static i => i.Order)
            .Select(item => Build(item, byParent, user.Culture))

            // A group whose every child was filtered away is an empty heading. Dropping it is
            // what keeps a restricted menu looking deliberate rather than broken.
            .Where(static node => node.Kind != nameof(NavigationKind.Group) || node.Children.Count > 0)
            .ToList();

        return Results.Ok(roots);
    }

    private static MenuNode Build(
        NavigationItem item,
        Dictionary<string, List<NavigationItem>> byParent,
        string? culture)
    {
        var children = byParent.TryGetValue(item.Id, out var nested)
            ? nested.Select(child => Build(child, byParent, culture)).ToList()
            : [];

        return new MenuNode(
            item.Id,
            item.Module,
            item.DisplayName.For(culture),
            item.Kind.ToString(),
            item.Route,
            item.Icon,
            children);
    }
}
