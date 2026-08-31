using ASAP.Modules.Finance;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Sales.Pricing;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Sales.Tests;

/// <summary>
/// What a customer pays, and what happens when two rows disagree about it.
/// </summary>
/// <remarks>
/// The interesting case is the last one. Everything else here is arithmetic; refusing to choose
/// between two contradictory prices is a decision, and it is the decision that stops a customer
/// being charged whatever the database happened to read first.
/// </remarks>
public sealed class PriceListTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000041");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000004a");
    private static readonly DateOnly Today = new(2026, 6, 1);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up an empty company with one item priced at a hundred.</summary>
    public PriceListTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-price-lists-{Guid.CreateVersion7()}")
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

        context.SaveChanges();
    }

    /// <summary>A customer on no list pays what is on the item, which is the counter price.</summary>
    [Fact]
    public async Task WithoutAnArrangementTheItemPriceStands()
    {
        using var context = NewContext();

        var price = await Pricing(context).PriceForAsync("C-0001", "ITEM-1001", 1m, Today);

        price.Succeeded.ShouldBeTrue();
        price.Value.UnitPrice.ShouldBe(100m);
        price.Value.PriceListCode.ShouldBeEmpty();
    }

    /// <summary>A trade customer pays what the trade list says.</summary>
    [Fact]
    public async Task TheAgreedPriceWins()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        (await pricing.SaveAsync(Sheet("TRADE", new PriceListLineRequest("ITEM-1001", 80m))))
            .Succeeded.ShouldBeTrue();

        (await pricing.AssignAsync("C-0001", "TRADE")).Succeeded.ShouldBeTrue();

        var price = await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today);

        price.Value.UnitPrice.ShouldBe(80m);
        price.Value.PriceListCode.ShouldBe("TRADE");
    }

    /// <summary>
    /// A volume break applies from its quantity up, and the general line still covers less than
    /// that. Neither line has to know the other exists.
    /// </summary>
    [Fact]
    public async Task TheVolumeBreakOnlyAppliesAtVolume()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        await pricing.SaveAsync(Sheet(
            "TRADE",
            new PriceListLineRequest("ITEM-1001", 80m),
            new PriceListLineRequest("ITEM-1001", 70m, MinimumQuantity: 100m)));

        await pricing.AssignAsync("C-0001", "TRADE");

        (await pricing.PriceForAsync("C-0001", "ITEM-1001", 99m, Today)).Value.UnitPrice.ShouldBe(80m);
        (await pricing.PriceForAsync("C-0001", "ITEM-1001", 100m, Today)).Value.UnitPrice.ShouldBe(70m);
    }

    /// <summary>A price for one colour beats a price for the item in any colour.</summary>
    [Fact]
    public async Task TheMoreSpecificLineWins()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        await pricing.SaveAsync(Sheet(
            "TRADE",
            new PriceListLineRequest("ITEM-1001", 80m),
            new PriceListLineRequest("ITEM-1001", 95m, VariantCode: "BLUE")));

        await pricing.AssignAsync("C-0001", "TRADE");

        (await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today, "BLUE")).Value.UnitPrice
            .ShouldBe(95m);

        (await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today, "RED")).Value.UnitPrice
            .ShouldBe(80m);
    }

    /// <summary>
    /// Two equally specific lines are refused rather than resolved.
    /// </summary>
    /// <remarks>
    /// This is the one that matters. Picking either would work, would look right in a demonstration,
    /// and would make what a customer is charged depend on row order for as long as nobody queried
    /// an invoice.
    /// </remarks>
    [Fact]
    public async Task TwoEqualLinesAreRefusedRatherThanResolved()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        await pricing.SaveAsync(Sheet(
            "TRADE",
            new PriceListLineRequest("ITEM-1001", 80m),
            new PriceListLineRequest("ITEM-1001", 75m)));

        await pricing.AssignAsync("C-0001", "TRADE");

        var price = await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today);

        price.Failed.ShouldBeTrue();
        price.Messages.ShouldContain(m => m.Code == SalesMessages.PriceIsAmbiguous);
    }

    /// <summary>
    /// A line that has run out stops applying without anybody switching it off, which is the whole
    /// point of writing an end date on a quarter's arrangement.
    /// </summary>
    [Fact]
    public async Task AnExpiredLineStopsApplying()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        await pricing.SaveAsync(Sheet(
            "TRADE",
            new PriceListLineRequest("ITEM-1001", 60m, ValidTo: Today.AddDays(-1))));

        await pricing.AssignAsync("C-0001", "TRADE");

        var price = await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today);

        price.Succeeded.ShouldBeTrue();
        price.Value.UnitPrice.ShouldBe(100m);
    }

    /// <summary>A whole list that has expired takes every line on it with it.</summary>
    [Fact]
    public async Task AnExpiredListTakesItsLinesWithIt()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        var sheet = Sheet("PROMO", new PriceListLineRequest("ITEM-1001", 50m)) with
        {
            ValidTo = Today.AddDays(-1),
        };

        await pricing.SaveAsync(sheet);
        await pricing.AssignAsync("C-0001", "PROMO");

        (await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today)).Value.UnitPrice.ShouldBe(100m);
    }

    /// <summary>Taking a customer off a list puts them back on the counter price.</summary>
    [Fact]
    public async Task TakingThemOffRestoresTheCounterPrice()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        await pricing.SaveAsync(Sheet("TRADE", new PriceListLineRequest("ITEM-1001", 80m)));
        await pricing.AssignAsync("C-0001", "TRADE");

        (await pricing.AssignAsync("C-0001", null)).Succeeded.ShouldBeTrue();

        (await pricing.PriceForAsync("C-0001", "ITEM-1001", 1m, Today)).Value.UnitPrice.ShouldBe(100m);
    }

    /// <summary>A list nobody has entered cannot be assigned to anybody.</summary>
    [Fact]
    public async Task AssigningAListThatDoesNotExistIsRefused()
    {
        using var context = NewContext();

        var result = await Pricing(context).AssignAsync("C-0001", "NOSUCH");

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == SalesMessages.PriceListNotFound);
    }

    /// <summary>Saving a list replaces its prices rather than adding to them.</summary>
    [Fact]
    public async Task SavingAgainReplacesTheSheet()
    {
        using var context = NewContext();
        var pricing = Pricing(context);

        await pricing.SaveAsync(Sheet("TRADE", new PriceListLineRequest("ITEM-1001", 80m)));
        await pricing.SaveAsync(Sheet("TRADE", new PriceListLineRequest("ITEM-1001", 70m)));

        var list = await pricing.FindAsync("TRADE");

        list.ShouldNotBeNull();
        list.Lines.Count.ShouldBe(1);
        list.Lines.Single().UnitPrice.ShouldBe(70m);
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private static PriceListRequest Sheet(string code, params PriceListLineRequest[] lines)
        => new(code, code, Lines: lines);

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
            new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All, .. SalesMessages.All]),
            _tenancy);

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
        public Guid? UserId { get; } = Guid.Parse("eeeeeeee-0000-0000-0000-00000000004e");

        public string? UserName => "salim";

        public string? DisplayName => "Salim";

        public string? Culture => "en";

        public bool IsSuperUser => false;

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();

        public bool Has(string permissionKey) => Permissions.Contains(permissionKey);

        public Guid RequireUserId() => UserId ?? Guid.Empty;
    }

    private sealed class StubClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }
}
