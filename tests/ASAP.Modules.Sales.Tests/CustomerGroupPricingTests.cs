using ASAP.Modules.Finance;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Sales.Pricing;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Sales.Tests;

/// <summary>
/// What a whole class of customer pays, and what happens to the one who negotiated separately.
/// </summary>
/// <remarks>
/// The rule under test is that a customer's own list beats their group's. It is the same
/// most-specific-wins rule the lines inside a list already follow, and it is what makes a group
/// price safe to set: putting a hundred wholesalers on the trade list must not quietly overwrite
/// the arrangement somebody agreed with one of them.
/// </remarks>
public sealed class CustomerGroupPricingTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000c1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c1");
    private static readonly DateOnly Today = new(2026, 6, 1);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up one item and two wholesalers.</summary>
    public CustomerGroupPricingTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-group-pricing-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        context.Set<Item>().Add(new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "ITEM-1001",
            Description = "Desk lamp",
            BaseUnitOfMeasure = "EA",
            UnitPrice = 100m,
            UnitCost = 40m,
        });

        context.Set<CustomerGroup>().Add(new CustomerGroup
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "WHOLESALE",
            Name = "Wholesale",
        });

        context.Set<Customer>().AddRange(Customer("C-0001"), Customer("C-0002"));

        context.SaveChanges();

        static Customer Customer(string no) => new()
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = no,
            Name = no,
        };
    }

    /// <summary>A customer in a group on a list pays the group's price, having none of their own.</summary>
    [Fact]
    public async Task TheGroupPriceReachesEverybodyInIt()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        await pricing.SaveAsync(Sheet("TRADE", 80m));
        (await pricing.AssignGroupAsync("WHOLESALE", "TRADE")).Succeeded.ShouldBeTrue();
        (await Groups(context).AssignAsync("C-0001", "WHOLESALE")).Succeeded.ShouldBeTrue();

        var price = await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today);

        price.Value.UnitPrice.ShouldBe(80m);
        price.Value.PriceListCode.ShouldBe("TRADE");
    }

    /// <summary>
    /// The customer who negotiated separately keeps what they negotiated, even after their whole
    /// group is put on a list.
    /// </summary>
    [Fact]
    public async Task TheirOwnListBeatsTheirGroups()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        await pricing.SaveAsync(Sheet("TRADE", 80m));
        await pricing.SaveAsync(Sheet("SPECIAL", 70m));

        await Groups(context).AssignAsync("C-0001", "WHOLESALE");
        await Groups(context).AssignAsync("C-0002", "WHOLESALE");

        await pricing.AssignAsync("C-0002", "SPECIAL");
        await pricing.AssignGroupAsync("WHOLESALE", "TRADE");

        (await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today)).Value.UnitPrice.ShouldBe(80m);

        var negotiated = await pricing.PriceForAsync("C-0002", "ITEM-1001", 1m, Today);

        negotiated.Value.UnitPrice.ShouldBe(70m, "the group price must not overwrite an agreement");
        negotiated.Value.PriceListCode.ShouldBe("SPECIAL");
    }

    /// <summary>A customer in no group falls through to the item price, as before.</summary>
    [Fact]
    public async Task SomebodyInNoGroupIsUnaffected()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        await pricing.SaveAsync(Sheet("TRADE", 80m));
        await pricing.AssignGroupAsync("WHOLESALE", "TRADE");

        var price = await pricing.PriceForAsync("C-0002", "ITEM-1001", 1m, Today);

        price.Value.UnitPrice.ShouldBe(100m);
        price.Value.PriceListCode.ShouldBeEmpty();
    }

    /// <summary>Taking the group off the list puts everybody in it back on the counter price.</summary>
    [Fact]
    public async Task TakingTheGroupOffTheListTakesThePriceWithIt()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        await pricing.SaveAsync(Sheet("TRADE", 80m));
        await Groups(context).AssignAsync("C-0001", "WHOLESALE");
        await pricing.AssignGroupAsync("WHOLESALE", "TRADE");

        (await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today)).Value.UnitPrice.ShouldBe(80m);

        (await pricing.AssignGroupAsync("WHOLESALE", null)).Succeeded.ShouldBeTrue();

        (await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today)).Value.UnitPrice.ShouldBe(100m);
    }

    /// <summary>A group cannot be put on a list that does not exist.</summary>
    [Fact]
    public async Task AGroupCannotBePutOnAListThatIsNotThere()
    {
        using var context = NewContext();

        var result = await Pricing(context).AssignGroupAsync("WHOLESALE", "NOSUCH");

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == SalesMessages.PriceListNotFound);
    }

    /// <summary>
    /// A withdrawn group takes nobody new, but keeps whoever is already in it.
    /// </summary>
    /// <remarks>
    /// The second half is the one that matters. A group that emptied itself when switched off
    /// would leave every price list and offer naming it matching nobody, and the first sign of it
    /// would be a customer being charged the counter price with nothing explaining why.
    /// </remarks>
    [Fact]
    public async Task AWithdrawnGroupKeepsWhoIsInItAndTakesNobodyNew()
    {
        using var context = NewContext();
        var groups = Groups(context);

        await groups.AssignAsync("C-0001", "WHOLESALE");

        await groups.SaveAsync(new CustomerGroupRequest("WHOLESALE", "Wholesale", IsActive: false));

        var joining = await groups.AssignAsync("C-0002", "WHOLESALE");

        joining.Failed.ShouldBeTrue();
        joining.Messages.ShouldContain(m => m.Code == FinanceMessages.CustomerGroupWithdrawn);

        (await groups.GroupOfAsync("C-0001")).ShouldBe("WHOLESALE");
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private static PriceListRequest Sheet(string code, decimal unitPrice)
        => new(code, code, Lines: [new PriceListLineRequest("ITEM-1001", unitPrice)]);

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(
            _options,
            _tenancy,
            new StubUser(),
            _clock,
            [new InventorySchema(), new FinanceSchema(), new SalesSchema()]);

        _opened.Add(context);

        return context;
    }

    private PricingService Pricing(AsapDbContext context)
        => new(
            context,
            Catalogue(),
            _tenancy);

    private CustomerGroupService Groups(AsapDbContext context)
        => new(context, Catalogue(), _tenancy, NullLogger<CustomerGroupService>.Instance);

    private static MessageCatalog Catalogue() => new(
    [
        .. PlatformMessages.All,
        .. InventoryMessages.All,
        .. FinanceMessages.All,
        .. SalesMessages.All,
    ]);

    private sealed class StubTenant : ITenantContext
    {
        public Guid? TenantId { get; set; }

        public Guid? CompanyId { get; set; }

        public Guid? BranchId { get; set; }

        public bool IsCrossTenantOperation { get; set; }

        public Guid RequireTenantId() => TenantId ?? Guid.Empty;

        public Guid RequireCompanyId() => CompanyId ?? Guid.Empty;
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
