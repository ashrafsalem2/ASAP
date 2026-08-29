using System.Text.RegularExpressions;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Modules;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// Holds every menu entry to the promise it makes: that clicking it arrives somewhere.
/// </summary>
/// <remarks>
/// <para>
/// The menu is declared in C# and the screens are declared in TypeScript, so nothing the compiler
/// does can connect the two. An entry naming a route the client does not have is a link a user
/// clicks and is bounced home from — the product's own menu advertising a feature that is not
/// there.
/// </para>
/// <para>
/// This found eight of them at once, which is what happens when the only thing joining two
/// languages is somebody remembering. Parsed as text rather than executed, for the same reason
/// the translation checks are: standing up a TypeScript runtime inside a .NET test to read a list
/// of string literals would be a great deal of machinery for a job a regular expression does
/// exactly.
/// </para>
/// </remarks>
public sealed partial class MenuReachesAScreenTests
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

    [Fact]
    public void Every_menu_entry_leads_to_a_screen_the_client_has()
    {
        var routes = ClientRoutes();

        routes.ShouldNotBeEmpty("the client's routes were not found, so this test proves nothing");

        var dead = Modules
            .SelectMany(static m => m.Navigation)
            .Where(static i => i.Route is { Length: > 0 })
            .Where(i => !routes.Contains(i.Route!.TrimStart('/')))
            .Select(static i => $"{i.Id} points at {i.Route}")
            .Order()
            .ToList();

        dead.ShouldBeEmpty(
            $"{dead.Count} menu entr(ies) point at a screen the client does not have, so clicking "
            + $"them goes nowhere:" + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", dead));
    }

    /// <summary>Every path the Angular router knows.</summary>
    private static HashSet<string> ClientRoutes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ASAP.slnx")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(
            directory?.FullName ?? string.Empty,
            "frontend", "src", "app", "app.routes.ts");

        if (!File.Exists(path))
        {
            return [];
        }

        return
        [
            .. PathPattern()
                .Matches(File.ReadAllText(path))
                .Select(static m => m.Groups["path"].Value),
        ];
    }

    [GeneratedRegex(@"path:\s*'(?<path>[^']*)'")]
    private static partial Regex PathPattern();
}
