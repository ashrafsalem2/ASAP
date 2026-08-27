using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Sales;
using ASAP.Modules.Sales.Orders;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Sales.Tests;

/// <summary>
/// Covers what an order is allowed to promise.
/// </summary>
/// <remarks>
/// Nothing here posts. The whole value of asking these questions at order time is that the person
/// keying it is the last one who can fix the answer cheaply: a location that does not exist used
/// to be discovered at the despatch bay, days later, in front of a customer who had been given a
/// date.
/// </remarks>
public sealed class SalesOrderCreationTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000031");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000003a");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    public SalesOrderCreationTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-sales-orders-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Seed();
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(
            _options,
            _tenancy,
            new StubUser(),
            _clock,
            [new InventorySchema(), new SalesSchema(), new Finance.FinanceSchema()]);

        _opened.Add(context);

        return context;
    }

    private void Seed()
    {
        using var context = NewContext();

        void Where(string code, string name, bool sellable, bool inTransit = false)
            => context.Set<Location>().Add(new Location
            {
                TenantId = Tenant,
                CompanyId = Company,
                Code = code,
                Name = name,
                IsSellable = sellable,
                IsInTransit = inTransit,
            });

        Where("JED-01", "Jeddah shop", sellable: true);
        Where("HO", "Head office stock", sellable: false);
        Where("TRANSIT", "In transit", sellable: false, inTransit: true);

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

        context.Set<Customer>().Add(new Customer
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "C-0001",
            Name = "Al Faisaliah Trading",
        });

        context.SaveChanges();
    }

    private SalesOrderService Service(AsapDbContext context)
    {
        var catalog = new MessageCatalog(
            [.. PlatformMessages.All, .. InventoryMessages.All, .. SalesMessages.All]);

        return new SalesOrderService(
            context,
            catalog,
            new OverrideAuditor(context, _tenancy, new StubUser(), _clock),
            new StubNumbers(),
            new StubSetup(),
            _tenancy,
            new StubUser(),
            _clock,
            NullLogger<SalesOrderService>.Instance);
    }

    private Task<Result<SalesOrder>> Take(
        AsapDbContext context,
        string? locationCode,
        IReadOnlySet<string>? held = null)
        => Service(context).CreateAsync(
            "C-0001",
            [new SalesOrderLineRequest(SalesLineType.Item, "ITEM-1001", 2m, 200m)],
            locationCode,
            heldOverridePermissions: held);

    [Fact]
    public async Task An_order_from_a_real_shop_is_taken()
    {
        using var context = NewContext();

        var result = await Take(context, "JED-01");

        result.Succeeded.ShouldBeTrue();
        result.Value.Lines.Single().Quantity.ShouldBe(2m);
    }

    [Fact]
    public async Task An_order_naming_a_location_that_does_not_exist_is_refused()
    {
        // Nobody can override a typo. The location list is where this is fixed.
        using var context = NewContext();

        var result = await Take(context, "MAIN");

        result.Failed.ShouldBeTrue();

        var refusal = result.Messages.Single(m => m.IsFailure);
        refusal.Code.Value.ShouldBe("INV.LOCATION.NOT_FOUND");
        refusal.Detail.ShouldNotBeNull().ShouldContain("MAIN");

        // Reported against the line that named it, so the screen can point at the right row.
        refusal.Target.Field.ShouldBe("Lines[1]");

        context.Set<SalesOrder>().ShouldBeEmpty("nothing was promised");
    }

    [Fact]
    public async Task An_order_from_a_place_that_does_not_sell_is_refused()
    {
        using var context = NewContext();

        var result = await Take(context, "HO");

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.LOCATION.NOT_SELLABLE");
    }

    [Fact]
    public async Task Goods_on_a_lorry_cannot_be_promised_either()
    {
        // In transit is not somewhere anybody picks from, however the sellable flag reads.
        using var context = NewContext();

        var result = await Take(context, "TRANSIT");

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.LOCATION.NOT_SELLABLE");
    }

    [Fact]
    public async Task Somebody_holding_the_override_may_sell_from_a_warehouse_and_it_is_recorded()
    {
        // Clearing head office stock to a customer is a real decision. What it must not be is
        // silent: the audit log is what the message promises when it says so.
        using var context = NewContext();

        var result = await Take(
            context,
            "HO",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Inventory.Stock.Override" });

        result.Succeeded.ShouldBeTrue();

        var warning = result.Messages.Single(m => m.Code.Value == "INV.LOCATION.NOT_SELLABLE");
        warning.WasOverridden.ShouldBeTrue();
        warning.IsFailure.ShouldBeFalse();

        context.AuditLog
            .Count(a => a.OverriddenMessageCode == "INV.LOCATION.NOT_SELLABLE")
            .ShouldBe(1, "the text told the user it had been recorded against their name");
    }

    [Fact]
    public async Task A_line_may_ship_from_somewhere_other_than_the_order()
    {
        // The order says Jeddah and one line says head office. The line wins, and is refused on
        // its own terms rather than inheriting the order's answer.
        using var context = NewContext();

        var result = await Service(context).CreateAsync(
            "C-0001",
            [
                new SalesOrderLineRequest(SalesLineType.Item, "ITEM-1001", 1m, 200m),
                new SalesOrderLineRequest(SalesLineType.Item, "ITEM-1001", 1m, 200m, LocationCode: "HO"),
            ],
            "JED-01");

        result.Failed.ShouldBeTrue();

        var refusal = result.Messages.Single(m => m.IsFailure);
        refusal.Code.Value.ShouldBe("INV.LOCATION.NOT_SELLABLE");
        refusal.Target.Field.ShouldBe("Lines[2]", "only the second line named the warehouse");
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
        public Guid? TenantId { get; set; }

        public Guid? CompanyId { get; set; }

        public Guid? BranchId { get; set; }

        public bool IsCrossTenantOperation { get; set; }

        public Guid RequireTenantId() => TenantId ?? Guid.Empty;

        public Guid RequireCompanyId() => CompanyId ?? Guid.Empty;
    }

    private sealed class StubUser : IUserContext
    {
        public Guid? UserId { get; } = Guid.Parse("eeeeeeee-0000-0000-0000-00000000003e");

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

    private sealed class StubNumbers : INumberSeriesService
    {
        private int _next = 1;

        public Task<Result<string>> NextAsync(
            string seriesCode,
            DateOnly documentDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success($"SO-{_next++:0000}"));

        public Task<Result<string>> PeekAsync(
            string seriesCode,
            DateOnly documentDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success($"SO-{_next:0000}"));

        public Task<Result> ValidateManualAsync(
            string seriesCode,
            string number,
            DateOnly documentDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }

    private sealed class StubSetup : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(
            string key,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult((TValue)(object)"SALES-ORD");

        public ValueTask<TValue?> GetAtScopeAsync<TValue>(
            string key,
            SetupScope scope,
            Guid? scopeId = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TValue?>(default);

        public Task<Result> SetAsync(
            string key,
            string? value,
            SetupScope scope = SetupScope.Company,
            Guid? scopeId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }
}
