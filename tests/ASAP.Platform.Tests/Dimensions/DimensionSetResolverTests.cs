using ASAP.Platform.Core.Dimensions;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Platform.Tests.Dimensions;

/// <summary>
/// Covers turning the dimension codes a document names into a stored set.
/// </summary>
/// <remarks>
/// This is the one place where what a person types meets what the posting engine works in, so it
/// is the place every mistyped code arrives at. What matters is that each of them is refused for
/// its own reason: a dimension that does not exist and a value that does not exist send somebody
/// to two different screens, and one message covering both sends them to neither.
/// </remarks>
public sealed class DimensionSetResolverTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000d1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000d1");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    public DimensionSetResolverTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-dimensions-{Guid.CreateVersion7()}")
            .Options;

        using var context = NewContext();

        context.Set<Dimension>().Add(new Dimension
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "DEPARTMENT",
            Name = "Department",
            ShortcutIndex = 1,
            Values =
            [
                Value("SALES", "Sales"),
                Value("ADMIN", "Administration"),
                Value("ALL", "All departments", DimensionValueKind.Total),
            ],
        });

        context.Set<Dimension>().Add(new Dimension
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "PROJECT",
            Name = "Project",
            Values = [Value("Q1", "First quarter push")],
        });

        context.Set<Dimension>().Add(new Dimension
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "REGION",
            Name = "Region",
            IsBlocked = true,
            Values = [Value("NORTH", "North")],
        });

        context.SaveChanges();

        static DimensionValue Value(string code, string name, DimensionValueKind kind = DimensionValueKind.Standard)
            => new()
            {
                TenantId = Tenant,
                CompanyId = Company,
                Code = code,
                Name = name,
                Kind = kind,
            };
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, []);
        _opened.Add(context);
        return context;
    }

    private DimensionSetResolver Resolver(AsapDbContext context)
        => new(context, new MessageCatalog(PlatformMessages.All), _tenancy, _clock);

    [Fact]
    public async Task Codes_resolve_to_a_set_that_is_created_once_and_then_shared()
    {
        Guid? first;

        await using (var context = NewContext())
        {
            var resolved = await Resolver(context).ResolveAsync(new Dictionary<string, string>
            {
                ["DEPARTMENT"] = "SALES",
                ["PROJECT"] = "Q1",
            });

            resolved.Failed.ShouldBeFalse();
            resolved.Combination.Count.ShouldBe(2);

            first = await Resolver(context).SetForAsync(resolved.Combination);
            first.ShouldNotBeNull();

            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            // The same combination again. A company running four dimensions should accumulate a
            // few thousand sets over its life, not several rows per ledger entry.
            var again = await Resolver(context).ResolveAsync(new Dictionary<string, string>
            {
                ["PROJECT"] = "Q1",
                ["DEPARTMENT"] = "SALES",
            });

            var setId = await Resolver(context).SetForAsync(again.Combination);

            setId.ShouldBe(first, "the order they were named in does not make a new combination");

            (await context.Set<DimensionSet>().CountAsync()).ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_dimension_the_company_does_not_have_is_refused_by_its_own_name()
    {
        await using var context = NewContext();

        var resolved = await Resolver(context)
            .ResolveAsync(new Dictionary<string, string> { ["COSTCENTRE"] = "X" });

        resolved.Failed.ShouldBeTrue();

        var refusal = resolved.Found.Single(m => m.IsFailure);
        refusal.Code.Value.ShouldBe("PLAT.DIMENSION.NOT_FOUND");
        refusal.Detail.ShouldNotBeNull().ShouldContain("COSTCENTRE");
    }

    [Fact]
    public async Task A_value_the_dimension_does_not_have_is_a_different_refusal()
    {
        await using var context = NewContext();

        // Deliberately not the same message as the one above. A missing dimension and a missing
        // value send somebody to two different screens, and one message covering both sends them
        // to neither.
        var resolved = await Resolver(context)
            .ResolveAsync(new Dictionary<string, string> { ["DEPARTMENT"] = "WAREHOUSE" });

        resolved.Failed.ShouldBeTrue();
        resolved.Found.Single(m => m.IsFailure).Code.Value.ShouldBe("PLAT.DIMENSION.VALUE_NOT_FOUND");
    }

    [Fact]
    public async Task A_total_is_a_thing_to_report_under_not_a_thing_to_post_to()
    {
        await using var context = NewContext();

        // Posting beneath a subtotal that also sums the things below it would count the entry
        // twice on any report that showed both.
        var resolved = await Resolver(context)
            .ResolveAsync(new Dictionary<string, string> { ["DEPARTMENT"] = "ALL" });

        resolved.Failed.ShouldBeTrue();
        resolved.Found.Single(m => m.IsFailure).Code.Value.ShouldBe("PLAT.DIMENSION.VALUE_BLOCKED");
    }

    [Fact]
    public async Task A_retired_dimension_is_refused_separately_from_a_missing_one()
    {
        await using var context = NewContext();

        // It exists and its value exists. The refusal is about the axis, and saying "no such
        // dimension" would send somebody to create one that is already there.
        var resolved = await Resolver(context)
            .ResolveAsync(new Dictionary<string, string> { ["REGION"] = "NORTH" });

        resolved.Failed.ShouldBeTrue();
        resolved.Found.Single(m => m.IsFailure).Code.Value.ShouldBe("PLAT.DIMENSION.BLOCKED");
    }

    [Fact]
    public async Task Every_bad_code_is_reported_at_once()
    {
        await using var context = NewContext();

        var resolved = await Resolver(context).ResolveAsync(new Dictionary<string, string>
        {
            ["DEPARTMENT"] = "SALES",
            ["COSTCENTRE"] = "X",
            ["PROJECT"] = "Q9",
        });

        // Somebody who mistyped two should be told twice, not told about one, fix it, and be told
        // about the other.
        resolved.Found.Count(m => m.IsFailure).ShouldBe(2);
    }

    [Fact]
    public async Task An_empty_value_means_the_axis_is_simply_not_set()
    {
        await using var context = NewContext();

        // Somebody clearing the field, not an error.
        var resolved = await Resolver(context).ResolveAsync(new Dictionary<string, string>
        {
            ["DEPARTMENT"] = "SALES",
            ["PROJECT"] = "",
        });

        resolved.Failed.ShouldBeFalse();
        resolved.Combination.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Naming_nothing_resolves_to_nothing_rather_than_to_an_empty_set()
    {
        await using var context = NewContext();

        var resolved = await Resolver(context).ResolveAsync(null);

        resolved.Failed.ShouldBeFalse();

        (await Resolver(context).SetForAsync(resolved.Combination))
            .ShouldBeNull("a set with no values in it is a row that would puzzle somebody");

        (await context.Set<DimensionSet>().CountAsync()).ShouldBe(0);
    }

    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private sealed class StubTenant : ITenantContext
    {
        public Guid? TenantId => Tenant;

        public Guid? CompanyId => Company;

        public Guid? BranchId => null;

        public bool IsCrossTenantOperation => false;

        public Guid RequireTenantId() => Tenant;

        public Guid RequireCompanyId() => Company;
    }

    private sealed class StubUser : IUserContext
    {
        public Guid? UserId => Guid.Empty;

        public string? UserName => "tests";

        public string? DisplayName => "Tests";

        public string? Culture => "en";

        public bool IsSuperUser => true;

        public IReadOnlySet<string> Permissions => new HashSet<string>();

        public bool Has(string permissionKey) => true;

        public Guid RequireUserId() => Guid.Empty;
    }

    private sealed class StubClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }
}
