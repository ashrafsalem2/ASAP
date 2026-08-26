using ASAP.Platform.Core.Security;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Persistence;
using ASAP.Platform.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Platform.Tests.Security;

/// <summary>
/// Covers the routine that keeps the shipped permission sets in step with the loaded modules.
/// </summary>
/// <remarks>
/// The gap this exists to close: a customer buys Inventory, the module is installed, and nothing
/// grants its permissions to the Administrator set -- because that set was written when Inventory
/// did not exist. Nobody sees an error; the screens are simply absent.
/// </remarks>
public sealed class SystemPermissionSetSynchroniserTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000001");

    private readonly TestContextHarness _harness = new();

    public SystemPermissionSetSynchroniserTests()
    {
        _harness.AsSystem(context =>
        {
            context.Tenants.Add(new Tenant
            {
                Code = "DEMO",
                Name = "Demo",
            });
            context.SaveChanges();
        });

        // The harness seeds through a fresh context, so read the tenant back for its key.
        _harness.AsSystem(context =>
        {
            var tenant = context.Tenants.Single();
            _tenantId = tenant.Id;
        });
    }

    private Guid _tenantId;

    private static PermissionDescriptor[] PlatformOnly() =>
    [
        PermissionDescriptor.Define("Platform", "User", PermissionAction.Read, "View users"),
        PermissionDescriptor.Define("Platform", "User", PermissionAction.Update, "Change users"),
        PermissionDescriptor.Define("Platform", "Setup", PermissionAction.Read, "View setup"),
        PermissionDescriptor.Define("Platform", "Dimension", PermissionAction.Read, "View dimensions"),
    ];

    private static PermissionDescriptor[] PlatformAndFinance() =>
    [
        .. PlatformOnly(),
        PermissionDescriptor.Define("Finance", "Journal", PermissionAction.Read, "View journals"),
        PermissionDescriptor.Define("Finance", "Journal", PermissionAction.Post, "Post journals"),
        PermissionDescriptor.Define("Finance", "Account", PermissionAction.Read, "View accounts"),
    ];

    private int Synchronise(PermissionDescriptor[] declared)
    {
        var changed = 0;

        _harness.AsSystem(context =>
        {
            var synchroniser = new SystemPermissionSetSynchroniser(
                context,
                declared,
                NullLogger<SystemPermissionSetSynchroniser>.Instance);

            changed = synchroniser.SynchroniseAsync().GetAwaiter().GetResult();
        });

        return changed;
    }

    private List<string> KeysOf(string setCode)
    {
        List<string> keys = [];

        _harness.AsSystem(context =>
        {
            keys = context.PermissionSets
                .Include(s => s.Entries)
                .Where(s => s.Code == setCode)
                .SelectMany(s => s.Entries.Select(e => e.PermissionKey))
                .OrderBy(k => k)
                .ToList();
        });

        return keys;
    }

    [Fact]
    public void Creates_the_shipped_sets_on_a_tenant_that_has_none()
    {
        Synchronise(PlatformOnly()).ShouldBeGreaterThan(0);

        KeysOf("ADMIN").ShouldNotBeEmpty();
        KeysOf("VIEWER").ShouldNotBeEmpty();
    }

    [Fact]
    public void Grants_a_newly_installed_modules_permissions_to_the_administrator_set()
    {
        // The whole point. Install Finance, and Administrator gains its permissions without
        // anyone editing a set by hand.
        Synchronise(PlatformOnly());
        KeysOf("ADMIN").ShouldNotContain("Finance.Journal.Post");

        Synchronise(PlatformAndFinance());
        KeysOf("ADMIN").ShouldContain("Finance.Journal.Post");
    }

    [Fact]
    public void Adds_new_entries_rather_than_updating_rows_that_do_not_exist()
    {
        // Guards a defect that reached startup once: adding through the parent's navigation
        // collection let EF classify the new children as modified, producing UPDATE against rows
        // never inserted. It surfaced as a concurrency exception blaming another user.
        Synchronise(PlatformOnly());

        Should.NotThrow(() => Synchronise(PlatformAndFinance()));

        KeysOf("ADMIN").Count.ShouldBe(PlatformAndFinance().Length);
    }

    [Fact]
    public void Removes_permissions_whose_module_is_no_longer_installed()
    {
        // Leaving them behind would show an administrator grants that resolve to nothing.
        Synchronise(PlatformAndFinance());
        KeysOf("ADMIN").ShouldContain("Finance.Journal.Post");

        Synchronise(PlatformOnly());
        KeysOf("ADMIN").ShouldNotContain("Finance.Journal.Post");
    }

    [Fact]
    public void Running_twice_with_no_change_does_nothing()
    {
        Synchronise(PlatformOnly());

        Synchronise(PlatformOnly()).ShouldBe(0);
    }

    [Fact]
    public void The_read_only_set_holds_only_read_permissions()
    {
        Synchronise(PlatformAndFinance());

        KeysOf("VIEWER").ShouldAllBe(k => k.EndsWith("Read", StringComparison.Ordinal));
    }

    [Fact]
    public void Leaves_a_set_an_administrator_created_alone()
    {
        // Copying a shipped set is the supported way to customise one, so the copy must never be
        // reconciled away.
        Synchronise(PlatformAndFinance());

        _harness.AsSystem(context =>
        {
            var custom = new PermissionSet
            {
                TenantId = _tenantId,
                Code = "OUR-CASHIER",
                Name = "Our cashier",
                IsSystemDefined = false,
            };

            custom.Entries.Add(new PermissionSetEntry
            {
                PermissionSetId = custom.Id,
                PermissionKey = "Finance.Journal.Read",
            });

            context.PermissionSets.Add(custom);
            context.SaveChanges();
        });

        Synchronise(PlatformOnly());

        // Finance is gone from the shipped sets, but the administrator's own copy is untouched.
        KeysOf("OUR-CASHIER").ShouldBe(["Finance.Journal.Read"]);
    }

    [Fact]
    public void Does_not_create_a_set_that_would_resolve_to_nothing()
    {
        // An empty Accountant set on a tenant without Finance is a meaningless option on the
        // assignment screen.
        Synchronise(PlatformOnly());

        KeysOf("ACCOUNTANT").ShouldBeEmpty();
    }

    [Fact]
    public void Creates_the_accountant_set_once_finance_is_installed()
    {
        Synchronise(PlatformOnly());
        Synchronise(PlatformAndFinance());

        KeysOf("ACCOUNTANT").ShouldNotBeEmpty();
        KeysOf("ACCOUNTANT").ShouldAllBe(k => k.StartsWith("Finance.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_bookkeeper_can_prepare_a_journal_but_not_post_it()
    {
        // The separation the set exists for: whoever keys a journal should not usually be the
        // person who commits it to the ledger.
        Synchronise(PlatformAndFinance());

        var keys = KeysOf("BOOKKEEPER");

        keys.ShouldContain("Finance.Journal.Read");
        keys.ShouldNotContain("Finance.Journal.Post");
    }

    public void Dispose() => _harness.Dispose();
}
