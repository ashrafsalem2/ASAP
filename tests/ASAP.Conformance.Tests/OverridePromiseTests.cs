using System.Reflection;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Persistence;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// Covers a promise the platform prints and a module has to keep.
/// </summary>
/// <remarks>
/// <para>
/// When somebody pushes past an overridable block, the text they are shown says the override has
/// been recorded against their name. That sentence is written once, in the platform, and shipped
/// by every module that declares an overridable message — so a module that never writes an audit
/// row turns it into a lie without changing a word of it.
/// </para>
/// <para>
/// This has now happened twice. Inventory carried the message for a fortnight with a comment
/// claiming it audited and nothing that did; Promotions shipped the same gap on its first
/// afternoon, and it was found by reading the audit log rather than by reading the code. Twice is
/// enough to stop relying on remembering.
/// </para>
/// <para>
/// What is checked is necessarily coarse: that a module offering an override has something that
/// takes an <see cref="OverrideAuditor"/>. It cannot prove the call happens on the right path.
/// It can prove nobody forgot the auditor entirely, which is the failure that actually occurred
/// both times.
/// </para>
/// </remarks>
public sealed class OverridePromiseTests
{
    private static readonly IReadOnlyList<IAsapModule> Modules =
    [
        new PlatformModule(),
        new ASAP.Modules.Finance.FinanceModule(),
        new ASAP.Modules.Inventory.InventoryModule(),
        new ASAP.Modules.Purchasing.PurchasingModule(),
        new ASAP.Modules.Promotions.PromotionsModule(),
        new ASAP.Modules.Sales.SalesModule(),
        new ASAP.Modules.Pos.PosModule(),
    ];

    [Fact]
    public void A_module_that_offers_an_override_has_something_that_records_it()
    {
        var failures = new List<string>();

        foreach (var module in Modules)
        {
            var overridable = module.Messages
                .Where(static m => m.OverridePermission is not null)
                .Select(static m => m.Code.Value)
                .ToList();

            if (overridable.Count == 0)
            {
                continue;
            }

            if (TakesTheAuditor(module.GetType().Assembly))
            {
                continue;
            }

            failures.Add(
                $"{module.ModuleId} offers an override on {string.Join(", ", overridable)} and "
                + "nothing in its assembly takes an OverrideAuditor. The text those messages show "
                + "says the override has been recorded against the user's name.");
        }

        failures.ShouldBeEmpty(
            "every overridable message promises an audit row:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_override_a_message_offers_is_a_permission_somebody_can_hold()
    {
        // A message that offers an override nobody declares is worse than one offering none: it
        // tells the user to go and find somebody who can approve it, and that person then finds
        // they cannot either. The startup audit reports this; the suite refuses it.
        var orphaned = Modules
            .SelectMany(static m => m.Messages.Select(message => (m.ModuleId, message)))
            .Where(static x => x.message.OverridePermission is { } p && !DeclaredPermissions.Contains(p))
            .Select(static x => $"{x.ModuleId}/{x.message.Code.Value} offers {x.message.OverridePermission}")
            .ToList();

        orphaned.ShouldBeEmpty(string.Join(Environment.NewLine, orphaned));
    }

    private static readonly HashSet<string> DeclaredPermissions = Modules
        .SelectMany(static m => m.Permissions)
        .Select(static p => p.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void The_check_is_capable_of_failing()
    {
        // Proving the shape rather than trusting it: an assembly with nothing that takes the
        // auditor has to come back false, or the test above passes for free.
        TakesTheAuditor(typeof(string).Assembly).ShouldBeFalse();
    }

    /// <summary>Whether anything in the assembly is built with an override auditor.</summary>
    private static bool TakesTheAuditor(Assembly assembly)
        => assembly
            .GetTypes()
            .SelectMany(static t => t.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .SelectMany(static c => c.GetParameters())
            .Any(static p => p.ParameterType == typeof(OverrideAuditor));
}
