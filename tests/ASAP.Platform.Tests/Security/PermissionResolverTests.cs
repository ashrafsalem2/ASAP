using ASAP.Platform.Core.Security;
using ASAP.Platform.Kernel.Security;
using Shouldly;

namespace ASAP.Platform.Tests.Security;

public sealed class PermissionResolverTests
{
    private static readonly Guid TradingCompany = Guid.CreateVersion7();
    private static readonly Guid PropertyCompany = Guid.CreateVersion7();
    private static readonly Guid RiyadhBranch = Guid.CreateVersion7();
    private static readonly Guid JeddahBranch = Guid.CreateVersion7();
    private static readonly DateOnly Today = new(2026, 8, 26);

    private static readonly PermissionDescriptor[] Declared =
    [
        PermissionDescriptor.Define("Finance", "Journal", PermissionAction.Read, "View journals"),
        PermissionDescriptor.Define(
            "Finance", "Journal", PermissionAction.Post, "Post journals",
            implies: ["Finance.Journal.Read"]),
        PermissionDescriptor.Define(
            "Finance", "Journal", PermissionAction.Reverse, "Reverse postings",
            // Reversing implies posting, which in turn implies reading: a two-step chain the
            // resolver has to follow all the way down.
            implies: ["Finance.Journal.Post"]),
        PermissionDescriptor.Define("Sales", "Order", PermissionAction.Read, "View sales orders"),
    ];

    private static PermissionSet Set(string code, params string[] keys)
    {
        var set = new PermissionSet { Code = code, Name = code };
        foreach (var key in keys)
        {
            set.Entries.Add(new PermissionSetEntry { PermissionSetId = set.Id, PermissionKey = key });
        }

        return set;
    }

    private static UserPermissionAssignment Assign(
        PermissionSet set,
        Guid? companyId = null,
        Guid? branchId = null,
        DateOnly? from = null,
        DateOnly? to = null)
        => new()
        {
            PermissionSetId = set.Id,
            CompanyId = companyId,
            BranchId = branchId,
            EffectiveFrom = from,
            EffectiveTo = to,
        };

    private static IReadOnlyDictionary<Guid, PermissionSet> Index(params PermissionSet[] sets)
        => sets.ToDictionary(static s => s.Id);

    private static PermissionResolver Resolver() => new(Declared);

    [Fact]
    public void Grants_the_keys_in_an_assigned_set()
    {
        var cashier = Set("CASHIER", "Sales.Order.Read");

        var granted = Resolver().Resolve(
            [Assign(cashier, TradingCompany)], Index(cashier), TradingCompany, null, Today);

        granted.ShouldContain("Sales.Order.Read");
    }

    [Fact]
    public void Grants_implied_permissions_down_the_whole_chain()
    {
        // Reverse implies Post implies Read. Granting one key must yield all three, or an
        // administrator has to know the chain by heart to configure it correctly.
        var auditor = Set("AUDITOR", "Finance.Journal.Reverse");

        var granted = Resolver().Resolve(
            [Assign(auditor, TradingCompany)], Index(auditor), TradingCompany, null, Today);

        granted.ShouldBe(
            ["Finance.Journal.Reverse", "Finance.Journal.Post", "Finance.Journal.Read"],
            ignoreOrder: true);
    }

    [Fact]
    public void Grants_everything_an_included_set_grants()
    {
        var cashier = Set("CASHIER", "Sales.Order.Read");
        var manager = Set("MANAGER", "Finance.Journal.Post");
        manager.Includes.Add(new PermissionSetInclusion
        {
            PermissionSetId = manager.Id,
            IncludedPermissionSetId = cashier.Id,
        });

        var granted = Resolver().Resolve(
            [Assign(manager, TradingCompany)], Index(manager, cashier), TradingCompany, null, Today);

        granted.ShouldContain("Sales.Order.Read");
        granted.ShouldContain("Finance.Journal.Post");
        granted.ShouldContain("Finance.Journal.Read");
    }

    [Fact]
    public void Does_not_leak_a_grant_from_one_company_into_another()
    {
        var accountant = Set("ACCOUNTANT", "Finance.Journal.Post");

        var granted = Resolver().Resolve(
            [Assign(accountant, TradingCompany)], Index(accountant), PropertyCompany, null, Today);

        granted.ShouldBeEmpty();
    }

    [Fact]
    public void Treats_an_assignment_with_no_company_as_covering_every_company()
    {
        // How a group-wide accountant is granted access once instead of once per company.
        var groupAccountant = Set("GROUP", "Finance.Journal.Post");

        var granted = Resolver().Resolve(
            [Assign(groupAccountant, companyId: null)], Index(groupAccountant), PropertyCompany, null, Today);

        granted.ShouldContain("Finance.Journal.Post");
    }

    [Fact]
    public void Does_not_leak_a_branch_grant_to_another_branch()
    {
        var cashier = Set("CASHIER", "Sales.Order.Read");

        var granted = Resolver().Resolve(
            [Assign(cashier, TradingCompany, RiyadhBranch)],
            Index(cashier), TradingCompany, JeddahBranch, Today);

        granted.ShouldBeEmpty();
    }

    [Fact]
    public void Does_not_give_a_branch_limited_grant_any_reach_at_head_office()
    {
        // Being allowed to sell at one shop is not being allowed to act across the company.
        var cashier = Set("CASHIER", "Sales.Order.Read");

        var granted = Resolver().Resolve(
            [Assign(cashier, TradingCompany, RiyadhBranch)],
            Index(cashier), TradingCompany, branchId: null, Today);

        granted.ShouldBeEmpty();
    }

    [Fact]
    public void Applies_a_company_wide_grant_at_every_branch()
    {
        var accountant = Set("ACCOUNTANT", "Finance.Journal.Post");

        var granted = Resolver().Resolve(
            [Assign(accountant, TradingCompany, branchId: null)],
            Index(accountant), TradingCompany, JeddahBranch, Today);

        granted.ShouldContain("Finance.Journal.Post");
    }

    [Fact]
    public void Ignores_an_assignment_that_has_not_started_yet()
    {
        var future = Set("FUTURE", "Finance.Journal.Post");

        var granted = Resolver().Resolve(
            [Assign(future, TradingCompany, from: new DateOnly(2026, 12, 1))],
            Index(future), TradingCompany, null, Today);

        granted.ShouldBeEmpty();
    }

    [Fact]
    public void Ignores_an_assignment_that_has_expired()
    {
        // Temporary cover has to lapse on its own; relying on someone to remember is how
        // a stand-in keeps posting authority for two years.
        var cover = Set("COVER", "Finance.Journal.Post");

        var granted = Resolver().Resolve(
            [Assign(cover, TradingCompany, to: new DateOnly(2026, 7, 31))],
            Index(cover), TradingCompany, null, Today);

        granted.ShouldBeEmpty();
    }

    [Fact]
    public void Combines_several_assignments()
    {
        var finance = Set("FIN", "Finance.Journal.Read");
        var sales = Set("SLS", "Sales.Order.Read");

        var granted = Resolver().Resolve(
            [Assign(finance, TradingCompany), Assign(sales, TradingCompany)],
            Index(finance, sales), TradingCompany, null, Today);

        granted.ShouldBe(["Finance.Journal.Read", "Sales.Order.Read"], ignoreOrder: true);
    }

    [Fact]
    public void Survives_a_cycle_between_included_sets()
    {
        // An administrator can build one through the UI. It must resolve to everything
        // reachable, not hang the request.
        var first = Set("FIRST", "Finance.Journal.Read");
        var second = Set("SECOND", "Sales.Order.Read");
        first.Includes.Add(new PermissionSetInclusion
        {
            PermissionSetId = first.Id,
            IncludedPermissionSetId = second.Id,
        });
        second.Includes.Add(new PermissionSetInclusion
        {
            PermissionSetId = second.Id,
            IncludedPermissionSetId = first.Id,
        });

        var granted = Resolver().Resolve(
            [Assign(first, TradingCompany)], Index(first, second), TradingCompany, null, Today);

        granted.ShouldBe(["Finance.Journal.Read", "Sales.Order.Read"], ignoreOrder: true);
    }

    [Fact]
    public void Keeps_a_key_whose_module_is_not_loaded()
    {
        // An extension can be uninstalled while assignments still name its permissions. The key
        // is kept so the grant starts working again when the extension returns, rather than
        // being silently dropped and having to be reconfigured.
        var set = Set("EXT", "ThirdParty.Widget.Read");

        var granted = Resolver().Resolve(
            [Assign(set, TradingCompany)], Index(set), TradingCompany, null, Today);

        granted.ShouldContain("ThirdParty.Widget.Read");
    }

    [Fact]
    public void Grants_nothing_when_an_assigned_set_has_been_deleted()
    {
        var deleted = Set("GONE", "Finance.Journal.Post");

        var granted = Resolver().Resolve(
            [Assign(deleted, TradingCompany)],
            Index(), // the set is no longer there
            TradingCompany,
            null,
            Today);

        granted.ShouldBeEmpty();
    }
}
