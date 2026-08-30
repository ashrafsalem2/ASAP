using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
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

namespace ASAP.Modules.Inventory.Tests.Costing;

/// <summary>
/// Covers writing stock down, and the one thing that makes it worth having: it has to survive
/// being sold.
/// </summary>
/// <remarks>
/// A revaluation posted as a lump sum against nothing in particular leaves the cost layers
/// carrying their old figures. The write-down lands in the inventory account, and then the next
/// sale is costed at the price the goods were originally bought at, and the account drifts back
/// towards where it started. Nothing errors. The only way to notice is to sell the stock and check
/// what it cost, which is what these do.
/// </remarks>
public sealed class RevaluationLifecycleTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000f1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000f1");
    private static readonly DateOnly Received = new(2026, 8, 1);
    private static readonly DateOnly Written = new(2026, 8, 20);
    private static readonly DateOnly Sold = new(2026, 8, 25);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    public RevaluationLifecycleTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-reval-{Guid.CreateVersion7()}")
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
            Code = "SHOP",
            Name = "Shop floor",
        });

        context.Set<Item>().Add(new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "ITEM-1001",
            Description = "Widget",
            BaseUnitOfMeasure = "PCS",
            CostingMethod = CostingMethod.Fifo,
            UnitCost = 10.00m,
            LastDirectCost = 10.00m,
            AllowNegativeInventory = true,
        });

        context.SaveChanges();
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
        _opened.Add(context);
        return context;
    }

    private static MessageCatalog Catalog()
        => new([.. PlatformMessages.All, .. InventoryMessages.All]);

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

    private RevaluationService Revaluation(AsapDbContext context)
        => new(
            context,
            Catalog(),
            new NullPublisher(),
            new LocationBranchLookup(context),
            _allocator,
            NullLogger<RevaluationService>.Instance);

    private async Task ReceiveAsync(decimal quantity, decimal unitCost, DateOnly date)
    {
        await using var context = NewContext();

        var result = await Posting(context).PostAsync(
            [new StockMovementRequest("ITEM-1001", "SHOP", quantity, unitCost, ItemLedgerEntryType.Purchase)],
            date,
            "TEST",
            documentNo: null,
            companyAllowsNegative: true);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_write_down_survives_the_sale_that_follows_it()
    {
        // Received a hundred at ten. Written down to six because the line is not moving.
        await ReceiveAsync(100m, 10.00m, Received);

        await using (var context = NewContext())
        {
            var written = await Revaluation(context)
                .RevalueAsync("ITEM-1001", "SHOP", 6.00m, Written, "Slow moving");

            written.Succeeded.ShouldBeTrue();
            written.Value.OldUnitCost.ShouldBe(10.00m);
            written.Value.ValueChange.ShouldBe(-400.00m);
        }

        // The whole point. Sell ten: they must cost six each, not ten. Costing them at ten would
        // put 40.00 of the write-down straight back into the inventory account.
        await using (var context = NewContext())
        {
            var sale = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -10m, 0m, ItemLedgerEntryType.Sale)],
                Sold,
                "TEST",
                documentNo: null,
                companyAllowsNegative: true);

            sale.Succeeded.ShouldBeTrue();
            sale.Value.CostAmount.ShouldBe(-60.00m);
        }
    }

    [Fact]
    public async Task What_is_left_is_worth_what_it_was_written_down_to()
    {
        await ReceiveAsync(100m, 10.00m, Received);

        await using (var context = NewContext())
        {
            await Revaluation(context).RevalueAsync("ITEM-1001", "SHOP", 6.00m, Written);
        }

        await using (var context = NewContext())
        {
            await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -10m, 0m, ItemLedgerEntryType.Sale)],
                Sold,
                "TEST",
                documentNo: null,
                companyAllowsNegative: true);
        }

        await using (var check = NewContext())
        {
            var valuation = await Revaluation(check).ValuationAsync("ITEM-1001", "SHOP");

            valuation.Succeeded.ShouldBeTrue();
            valuation.Value.Quantity.ShouldBe(90m);
            valuation.Value.UnitCost.ShouldBe(6.00m);
            valuation.Value.Value.ShouldBe(540.00m);
        }
    }

    [Fact]
    public async Task A_write_down_spans_receipts_bought_at_different_prices()
    {
        // Two layers at different costs. Both have to end up at the new figure, or the next sale
        // costs whichever one FIFO happens to reach first.
        await ReceiveAsync(40m, 10.00m, Received);
        await ReceiveAsync(60m, 15.00m, Received.AddDays(3));

        await using (var context = NewContext())
        {
            var written = await Revaluation(context).RevalueAsync("ITEM-1001", "SHOP", 8.00m, Written);

            written.Succeeded.ShouldBeTrue();

            // 40 x 10 + 60 x 15 = 1300, against 100 x 8 = 800.
            written.Value.OldUnitCost.ShouldBe(13.00m);
            written.Value.ValueChange.ShouldBe(-500.00m);
            written.Value.LayerCount.ShouldBe(2);
        }

        await using (var context = NewContext())
        {
            // Fifty crosses from the first layer into the second. Both are eight now, so it costs
            // four hundred either way -- which is the point.
            var sale = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -50m, 0m, ItemLedgerEntryType.Sale)],
                Sold,
                "TEST",
                documentNo: null,
                companyAllowsNegative: true);

            sale.Value.CostAmount.ShouldBe(-400.00m);
        }
    }

    [Fact]
    public async Task Writing_up_works_the_same_way_round()
    {
        await ReceiveAsync(50m, 4.00m, Received);

        await using (var context = NewContext())
        {
            var written = await Revaluation(context).RevalueAsync("ITEM-1001", "SHOP", 5.50m, Written);

            written.Value.ValueChange.ShouldBe(75.00m);
        }

        await using (var context = NewContext())
        {
            var sale = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -20m, 0m, ItemLedgerEntryType.Sale)],
                Sold,
                "TEST",
                documentNo: null,
                companyAllowsNegative: true);

            sale.Value.CostAmount.ShouldBe(-110.00m);
        }
    }

    [Fact]
    public async Task Revaluing_twice_lands_where_the_second_one_said()
    {
        await ReceiveAsync(100m, 10.00m, Received);

        await using (var context = NewContext())
        {
            await Revaluation(context).RevalueAsync("ITEM-1001", "SHOP", 6.00m, Written);
        }

        await using (var context = NewContext())
        {
            var again = await Revaluation(context).RevalueAsync("ITEM-1001", "SHOP", 7.50m, Written);

            again.Succeeded.ShouldBeTrue();
            again.Value.OldUnitCost.ShouldBe(6.00m);
            again.Value.ValueChange.ShouldBe(150.00m);
        }

        await using (var check = NewContext())
        {
            var valuation = await Revaluation(check).ValuationAsync("ITEM-1001", "SHOP");

            valuation.Value.UnitCost.ShouldBe(7.50m);
        }
    }

    [Fact]
    public async Task Revaluing_a_part_consumed_receipt_only_moves_what_is_left()
    {
        await ReceiveAsync(100m, 10.00m, Received);

        await using (var context = NewContext())
        {
            await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -60m, 0m, ItemLedgerEntryType.Sale)],
                Received.AddDays(5),
                "TEST",
                documentNo: null,
                companyAllowsNegative: true);
        }

        await using (var context = NewContext())
        {
            // Forty left, written from ten to six. The sixty already sold stay at ten: their cost
            // of sales is booked against the revenue they earned, and reaching back to restate it
            // would move a closed month.
            var written = await Revaluation(context).RevalueAsync("ITEM-1001", "SHOP", 6.00m, Written);

            written.Value.Quantity.ShouldBe(40m);
            written.Value.ValueChange.ShouldBe(-160.00m);
        }
    }

    [Fact]
    public async Task Revaluing_what_is_not_there_is_refused()
    {
        await using var context = NewContext();

        var written = await Revaluation(context).RevalueAsync("ITEM-1001", "SHOP", 6.00m, Written);

        written.Failed.ShouldBeTrue();
        written.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.REVAL.NOTHING_ON_HAND");
    }

    [Fact]
    public async Task Stock_cannot_be_written_below_nothing()
    {
        await ReceiveAsync(10m, 10.00m, Received);

        await using var context = NewContext();

        var written = await Revaluation(context).RevalueAsync("ITEM-1001", "SHOP", -1.00m, Written);

        written.Failed.ShouldBeTrue();
        written.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.REVAL.COST_NEGATIVE");
    }

    [Fact]
    public async Task Writing_stock_to_what_it_already_costs_posts_nothing_and_says_so()
    {
        await ReceiveAsync(10m, 10.00m, Received);

        await using var context = NewContext();

        var written = await Revaluation(context).RevalueAsync("ITEM-1001", "SHOP", 10.00m, Written);

        // Success, not failure: nothing was wrong, and nothing needed doing.
        written.Succeeded.ShouldBeTrue();
        written.Value.ValueChange.ShouldBe(0m);
        written.Value.TransactionNo.ShouldBe(0);
        written.Messages.Single().Code.Value.ShouldBe("INV.REVAL.NO_CHANGE");
    }

    [Fact]
    public async Task Writing_stock_down_to_nothing_is_allowed()
    {
        await ReceiveAsync(20m, 10.00m, Received);

        await using (var context = NewContext())
        {
            var written = await Revaluation(context).RevalueAsync("ITEM-1001", "SHOP", 0m, Written, "Written off");

            written.Succeeded.ShouldBeTrue();
            written.Value.ValueChange.ShouldBe(-200.00m);
        }

        await using (var context = NewContext())
        {
            // Worthless goods still exist and can still leave; they simply cost nothing to sell.
            var sale = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -5m, 0m, ItemLedgerEntryType.Sale)],
                Sold,
                "TEST",
                documentNo: null,
                companyAllowsNegative: true);

            sale.Value.CostAmount.ShouldBe(0m);
        }
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
