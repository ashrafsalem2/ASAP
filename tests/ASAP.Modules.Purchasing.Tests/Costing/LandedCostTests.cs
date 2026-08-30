using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Modules.Purchasing.Costing;
using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Purchasing.Tests.Costing;

/// <summary>
/// Covers adding freight to the cost of the goods it carried.
/// </summary>
/// <remarks>
/// The easy version of this feature puts the whole charge into inventory, and it is wrong: freight
/// that arrives after some of the goods are sold belongs on all of them, so the inventory account
/// would carry freight for stock that is not there and the margin on the sales that already
/// happened would stay overstated for ever. The split between inventory and cost of sales is what
/// these tests are for.
/// </remarks>
public sealed class LandedCostTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000d1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000d1");
    private static readonly DateOnly Day = new(2026, 8, 1);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    public LandedCostTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-landed-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _tenancy.TenantId = Tenant;
        _tenancy.CompanyId = Company;

        using var context = NewContext();

        context.Set<Location>().Add(new Location
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "HO",
            Name = "Head office",
        });

        context.Set<Item>().AddRange(
            Item("CHEAP", 10.00m),
            Item("DEAR", 90.00m));

        context.SaveChanges();

        static Item Item(string no, decimal cost)
            => new()
            {
                TenantId = Tenant,
                CompanyId = Company,
                No = no,
                Description = no,
                BaseUnitOfMeasure = "PCS",
                CostingMethod = CostingMethod.Fifo,
                UnitCost = cost,
                LastDirectCost = cost,
                AllowNegativeInventory = true,
            };
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
        _opened.Add(context);
        return context;
    }

    private static MessageCatalog Catalog()
        => new([.. PlatformMessages.All, .. InventoryMessages.All, .. PurchasingMessages.All]);

    private StockPostingService Posting(AsapDbContext context)
        => new(
            context,
            new StockAvailability(Catalog()),
            new LocationBranchLookup(context),
            new NullPublisher(),
            Catalog(),
            _tenancy,
            new OverrideAuditor(context, _tenancy, new StubUser(), _clock),
            _clock,
            _allocator,
            NullLogger<StockPostingService>.Instance);

    private LandedCostService Landed(AsapDbContext context)
        => new(
            context,
            Catalog(),
            new NullPublisher(),
            _clock,
            _allocator,
            NullLogger<LandedCostService>.Instance);

    private async Task ReceiveAsync(string itemNo, decimal quantity, decimal cost, string orderNo)
    {
        await using var context = NewContext();

        var result = await Posting(context).PostAsync(
            [new StockMovementRequest(itemNo, "HO", quantity, cost, ItemLedgerEntryType.Purchase)],
            Day,
            "PURCH",
            orderNo,
            companyAllowsNegative: true);

        result.Succeeded.ShouldBeTrue();
    }

    private async Task SellAsync(string itemNo, decimal quantity)
    {
        await using var context = NewContext();

        var result = await Posting(context).PostAsync(
            [new StockMovementRequest(itemNo, "HO", -quantity, 0m, ItemLedgerEntryType.Sale)],
            Day.AddDays(2),
            "TEST",
            documentNo: null,
            companyAllowsNegative: true);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Freight_lands_on_the_goods_and_the_next_sale_costs_more()
    {
        await ReceiveAsync("CHEAP", 100m, 10.00m, "PO-1");

        await using (var context = NewContext())
        {
            var result = await Landed(context).ApplyAsync("PO-1", 500m, LandedCostBasis.ByValue, "2100");

            result.Succeeded.ShouldBeTrue();
            result.Value.ToInventory.ShouldBe(500m);
            result.Value.ToCostOfSales.ShouldBe(0m);
        }

        // Five per unit on top of ten. Ten sold must cost 150, not 100.
        await using (var context = NewContext())
        {
            var sale = await Posting(context).PostAsync(
                [new StockMovementRequest("CHEAP", "HO", -10m, 0m, ItemLedgerEntryType.Sale)],
                Day.AddDays(3),
                "TEST",
                documentNo: null,
                companyAllowsNegative: true);

            sale.Value.CostAmount.ShouldBe(-150.00m);
        }
    }

    [Fact]
    public async Task A_charge_arriving_after_a_sale_corrects_what_that_sale_cost()
    {
        // The case the easy implementation gets wrong. A hundred received, sixty already sold, and
        // then the freight invoice turns up. The five hundred belongs on all hundred.
        await ReceiveAsync("CHEAP", 100m, 10.00m, "PO-1");
        await SellAsync("CHEAP", 60m);

        await using var context = NewContext();

        var result = await Landed(context).ApplyAsync("PO-1", 500m, LandedCostBasis.ByValue, "2100");

        result.Succeeded.ShouldBeTrue();

        // Forty still here at five each; sixty gone at five each.
        result.Value.ToInventory.ShouldBe(200.00m);
        result.Value.ToCostOfSales.ShouldBe(300.00m);

        // And it says so, because a charge quietly landing in cost of sales is a surprise.
        result.Messages.ShouldContain(m => m.Code.Value == "PUR.LANDED.CORRECTED_SALES");
    }

    [Fact]
    public async Task The_correction_lands_on_the_sale_that_consumed_the_receipt()
    {
        await ReceiveAsync("CHEAP", 100m, 10.00m, "PO-1");
        await SellAsync("CHEAP", 60m);

        await using (var context = NewContext())
        {
            await Landed(context).ApplyAsync("PO-1", 500m, LandedCostBasis.ByValue, "2100");
        }

        await using var check = NewContext();

        // The outbound entry's total cost is the original 600 plus the 300 of freight that
        // belonged to it. Correcting the stock instead would have left this at 600 for ever.
        var outbound = await check.Set<ItemLedgerEntry>()
            .FirstAsync(e => e.ItemNo == "CHEAP" && e.Quantity < 0);

        var cost = await check.Set<ValueEntry>()
            .Where(v => v.ItemLedgerEntryId == outbound.Id)
            .SumAsync(v => v.CostAmount);

        cost.ShouldBe(-900.00m);
    }

    [Fact]
    public async Task By_value_the_expensive_thing_carries_more_of_it()
    {
        // 100 at 10 is 1,000; 100 at 90 is 9,000. A thousand of duty splits 100 / 900.
        await ReceiveAsync("CHEAP", 100m, 10.00m, "PO-2");
        await ReceiveAsync("DEAR", 100m, 90.00m, "PO-2");

        await using var context = NewContext();

        var result = await Landed(context).ApplyAsync("PO-2", 1_000m, LandedCostBasis.ByValue, "2100");

        result.Succeeded.ShouldBeTrue();

        var cheap = result.Value.Shares.Single(s => s.ItemNo == "CHEAP");
        var dear = result.Value.Shares.Single(s => s.ItemNo == "DEAR");

        cheap.Share.ShouldBe(100.00m);
        dear.Share.ShouldBe(900.00m);
    }

    [Fact]
    public async Task By_quantity_a_pallet_fee_does_not_care_what_is_on_the_pallet()
    {
        await ReceiveAsync("CHEAP", 100m, 10.00m, "PO-2");
        await ReceiveAsync("DEAR", 100m, 90.00m, "PO-2");

        await using var context = NewContext();

        var result = await Landed(context).ApplyAsync("PO-2", 1_000m, LandedCostBasis.ByQuantity, "2100");

        result.Value.Shares.Single(s => s.ItemNo == "CHEAP").Share.ShouldBe(500.00m);
        result.Value.Shares.Single(s => s.ItemNo == "DEAR").Share.ShouldBe(500.00m);
    }

    [Fact]
    public async Task The_shares_add_up_to_the_charge_exactly()
    {
        // Three receipts and an amount that does not divide. The last share carries the rounding,
        // so the entries add up to the invoice rather than to within a halala of it.
        await ReceiveAsync("CHEAP", 3m, 10.00m, "PO-3");
        await ReceiveAsync("CHEAP", 3m, 10.00m, "PO-3");
        await ReceiveAsync("CHEAP", 3m, 10.00m, "PO-3");

        await using var context = NewContext();

        var result = await Landed(context).ApplyAsync("PO-3", 100m, LandedCostBasis.ByQuantity, "2100");

        result.Value.Shares.Sum(static s => s.Share).ShouldBe(100.00m);
        (result.Value.ToInventory + result.Value.ToCostOfSales).ShouldBe(100.00m);
    }

    [Fact]
    public async Task A_charge_of_nothing_is_refused()
    {
        await ReceiveAsync("CHEAP", 10m, 10.00m, "PO-1");

        await using var context = NewContext();

        var result = await Landed(context).ApplyAsync("PO-1", 0m, LandedCostBasis.ByValue, "2100");

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("PUR.LANDED.NOT_POSITIVE");
    }

    [Fact]
    public async Task A_charge_with_nowhere_to_post_against_is_refused()
    {
        await ReceiveAsync("CHEAP", 10m, 10.00m, "PO-1");

        await using var context = NewContext();

        var result = await Landed(context).ApplyAsync("PO-1", 100m, LandedCostBasis.ByValue, "  ");

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("PUR.LANDED.NO_ACCOUNT");
    }

    [Fact]
    public async Task An_order_with_nothing_received_is_refused()
    {
        await using var context = NewContext();

        var result = await Landed(context).ApplyAsync("PO-NOTHING", 100m, LandedCostBasis.ByValue, "2100");

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("PUR.LANDED.NOTHING_RECEIVED");
    }

    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }

        public Guid? CompanyId { get; set; }

        public Guid? BranchId { get; set; }

        public bool IsCrossTenantOperation { get; set; }

        public Guid RequireTenantId() => TenantId!.Value;

        public Guid RequireCompanyId() => CompanyId!.Value;
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

    private sealed class CountingAllocator : ITransactionNumberAllocator
    {
        private long _next;

        public Task<long> NextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(++_next);
    }

    private sealed class NullPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent asapEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;

        public Task<Result> PublishVetoableAsync<TEvent>(
            TEvent asapEvent,
            CancellationToken cancellationToken = default)
            where TEvent : VetoableEvent
            => Task.FromResult(Result.Success());

        public void Enqueue<TEvent>(TEvent asapEvent)
            where TEvent : IIntegrationEvent
        {
            // Nothing to deliver; these tests are about what reaches the cost layers.
        }
    }
}
