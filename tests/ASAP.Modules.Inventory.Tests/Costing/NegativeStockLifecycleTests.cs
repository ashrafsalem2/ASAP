using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Events;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Inventory.Tests.Costing;

/// <summary>
/// Follows a sale made from stock that was not there, all the way to the correction posted when
/// the goods finally arrive.
///
/// This is the test the whole negative-stock design exists for. Permitting the sale is easy;
/// what is hard, and what is usually missing, is making sure the guess is corrected afterwards
/// rather than left in the books for ever.
/// </summary>
public sealed class NegativeStockLifecycleTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly DateOnly SaleDate = new(2026, 8, 20);
    private static readonly DateOnly ReceiptDate = new(2026, 8, 26);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    public NegativeStockLifecycleTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-negative-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _tenancy.TenantId = Tenant;
        _tenancy.CompanyId = Company;

        Seed();
    }

    private AsapDbContext NewContext()
    {
        // The Inventory schema has to be registered or none of its entities are in the model.
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
        _opened.Add(context);
        return context;
    }

    private void Seed()
    {
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

            // What the item is believed to cost. This is the figure a shortfall is valued at, and
            // in this story it turns out to be wrong.
            UnitCost = 12.00m,
            LastDirectCost = 12.00m,
            AllowNegativeInventory = true,
        });

        context.SaveChanges();
    }

    private StockPostingService Posting(AsapDbContext context)
    {
        var catalog = new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All]);

        return new StockPostingService(
            context,
            new StockAvailability(catalog),
            new NullPublisher(),
            catalog,
            _tenancy,
            new StubUser(),
            new OverrideAuditor(context, _tenancy, new StubUser(), _clock),
            _clock,
            _allocator,
            NullLogger<StockPostingService>.Instance);
    }

    private CostSettlementService Settlement(AsapDbContext context)
        => new(
            context,
            new NullPublisher(),
            new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All]),
            _allocator,
            _clock,
            NullLogger<CostSettlementService>.Instance);

    [Fact]
    public async Task A_sale_with_no_stock_is_valued_at_an_estimate_and_settled_when_goods_arrive()
    {
        // 1. Sell ten with nothing on hand. Permitted, valued at the believed cost of 12.00.
        await using (var context = NewContext())
        {
            var result = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -10, EntryType: ItemLedgerEntryType.Sale)],
                SaleDate,
                "POS",
                "POS-0001",
                companyAllowsNegative: true);

            result.Succeeded.ShouldBeTrue();
            result.Value.CostAmount.ShouldBe(-120.00m);

            // The whole cost is an estimate: nothing on hand backed any of it.
            result.Value.EstimatedCostAmount.ShouldBe(-120.00m);
            result.Messages.ShouldContain(m => m.Code.Value == "INV.STOCK.WENT_NEGATIVE");
        }

        await using (var context = NewContext())
        {
            var sale = await context.Set<ItemLedgerEntry>().SingleAsync();
            sale.WentNegative.ShouldBeTrue();
            sale.Quantity.ShouldBe(-10);

            // The application has no receipt behind it, and is flagged as still waiting. That row
            // is the work list the settlement routine comes back to.
            var application = await context.Set<ItemApplicationEntry>().SingleAsync();
            application.InboundEntryId.ShouldBeNull();
            application.IsOutstanding.ShouldBeTrue();

            var value = await context.Set<ValueEntry>().SingleAsync();
            value.IsExpected.ShouldBeTrue();
            value.CostAmount.ShouldBe(-120.00m);
        }

        // 2. The goods arrive, and turn out to have cost 13.50 rather than the 12.00 assumed.
        await using (var context = NewContext())
        {
            var result = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", 10, 13.50m, ItemLedgerEntryType.Purchase)],
                ReceiptDate,
                "PURCH",
                "PINV-0001",
                companyAllowsNegative: true);

            result.Succeeded.ShouldBeTrue();
            result.Value.CostAmount.ShouldBe(135.00m);
        }

        // 3. Settlement matches the sale to the receipt and posts only the difference.
        await using (var context = NewContext())
        {
            var result = await Settlement(context).SettleAsync("ITEM-1001");

            result.Succeeded.ShouldBeTrue();
            result.Value.ApplicationsSettled.ShouldBe(1);

            // Estimated at 120.00, actually 135.00, so 15.00 more has to come off stock.
            result.Value.TotalCorrection.ShouldBe(-15.00m);
        }

        await using (var context = NewContext())
        {
            var application = await context.Set<ItemApplicationEntry>().SingleAsync();
            application.IsOutstanding.ShouldBeFalse();
            application.InboundEntryId.ShouldNotBeNull();

            var values = await context.Set<ValueEntry>().OrderBy(v => v.EntryType).ToListAsync();

            // Nothing was rewritten. The estimate stays on the record as what was booked at the
            // time, and the correction sits beside it -- so both the guess and its settlement can
            // be read months later.
            var estimate = values.Single(v => v.CostAmount == -120.00m);
            estimate.IsExpected.ShouldBeFalse();

            var correction = values.Single(v => v.EntryType == ValueEntryType.Revaluation);
            correction.CostAmount.ShouldBe(-15.00m);
            correction.SourceCode.ShouldBe("COSTADJ");

            // And the arithmetic closes: 120.00 estimated plus 15.00 correction is the 135.00 the
            // goods actually cost.
            var soldCost = values.Where(v => v.Quantity <= 0).Sum(v => v.CostAmount);
            soldCost.ShouldBe(-135.00m);
        }
    }

    [Fact]
    public async Task Pushing_past_a_block_leaves_a_record_of_who_did_it()
    {
        // Inventory honoured override permissions long before it recorded them, so a sale could
        // go out below zero at a company that forbids it and leave nothing behind naming whoever
        // allowed it. An override nobody wrote down is indistinguishable from a rule that was
        // never there.
        await using (var context = NewContext())
        {
            // The seeded item allows negative stock outright, which would settle the question
            // before any block was raised. Clearing the override makes it follow the company,
            // and the company is about to say no.
            var item = await context.Set<Item>().SingleAsync(i => i.No == "ITEM-1001");
            item.AllowNegativeInventory = null;
            await context.SaveChangesAsync();

            var result = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -10, EntryType: ItemLedgerEntryType.Sale)],
                SaleDate,
                "POS",
                "POS-0009",

                // The company forbids it. Only the held permission gets this through.
                companyAllowsNegative: false,
                heldOverridePermissions: new HashSet<string> { "Inventory.Stock.Override" },
                overrideReason: "Customer waiting; delivery signed for this morning.");

            result.Succeeded.ShouldBeTrue();
        }

        await using (var context = NewContext())
        {
            var overrides = await context.AuditLog
                .IgnoreQueryFilters()
                .Where(a => a.Action == AuditAction.Override)
                .ToListAsync();

            var entry = overrides.ShouldHaveSingleItem();

            entry.OverriddenMessageCode.ShouldBe("INV.STOCK.NEGATIVE_BLOCKED");
            entry.EntityType.ShouldBe("Inventory.ItemLedgerEntry");
            entry.DisplayNo.ShouldBe("POS-0009");
            entry.OverrideReason.ShouldBe("Customer waiting; delivery signed for this morning.");
            entry.UserName.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task A_sale_within_stock_records_no_override()
    {
        // The audit log is only worth reading if it holds the exceptions and nothing else.
        await using (var context = NewContext())
        {
            await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", 50, 12.00m, ItemLedgerEntryType.Purchase)],
                SaleDate,
                "PURCH",
                "PINV-0009",
                companyAllowsNegative: false,
                heldOverridePermissions: new HashSet<string> { "Inventory.Stock.Override" });
        }

        await using (var context = NewContext())
        {
            var overrides = await context.AuditLog
                .IgnoreQueryFilters()
                .Where(a => a.Action == AuditAction.Override)
                .ToListAsync();

            overrides.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task An_estimate_that_was_right_settles_without_posting_anything()
    {
        // Writing a zero-value entry for every accurate guess would fill the ledger with rows that
        // say nothing, so the settlement closes the application and stops there.
        await using (var context = NewContext())
        {
            await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -10, EntryType: ItemLedgerEntryType.Sale)],
                SaleDate,
                "POS",
                "POS-0002",
                companyAllowsNegative: true);
        }

        await using (var context = NewContext())
        {
            await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", 10, 12.00m, ItemLedgerEntryType.Purchase)],
                ReceiptDate,
                "PURCH",
                "PINV-0002",
                companyAllowsNegative: true);
        }

        await using (var context = NewContext())
        {
            var result = await Settlement(context).SettleAsync("ITEM-1001");

            result.Value.ApplicationsSettled.ShouldBe(1);
            result.Value.TotalCorrection.ShouldBe(0m);
        }

        await using (var context = NewContext())
        {
            (await context.Set<ValueEntry>().AnyAsync(v => v.EntryType == ValueEntryType.Revaluation))
                .ShouldBeFalse();

            (await context.Set<ItemApplicationEntry>().SingleAsync()).IsOutstanding.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Settlement_leaves_an_application_alone_when_the_goods_have_not_arrived()
    {
        await using (var context = NewContext())
        {
            await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -10, EntryType: ItemLedgerEntryType.Sale)],
                SaleDate,
                "POS",
                "POS-0003",
                companyAllowsNegative: true);
        }

        await using (var context = NewContext())
        {
            var result = await Settlement(context).SettleAsync("ITEM-1001");

            result.Succeeded.ShouldBeTrue();
            result.Value.ApplicationsSettled.ShouldBe(0);
        }

        await using (var context = NewContext())
        {
            // Still waiting, and still an estimate. Running the routine early must not quietly
            // mark the cost as final.
            (await context.Set<ItemApplicationEntry>().SingleAsync()).IsOutstanding.ShouldBeTrue();
            (await context.Set<ValueEntry>().SingleAsync()).IsExpected.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task A_receipt_before_the_sale_is_not_used_to_settle_it()
    {
        // A receipt dated before the sale would have been consumed at the time, so it cannot be
        // what covered it. Reaching backwards would take stock some earlier sale already used.
        await using (var context = NewContext())
        {
            await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", 10, 9.00m, ItemLedgerEntryType.Purchase)],
                new DateOnly(2026, 8, 1),
                "PURCH",
                "PINV-EARLY",
                companyAllowsNegative: true);
        }

        // Sells the ten that exist plus ten that do not.
        await using (var context = NewContext())
        {
            var result = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -20, EntryType: ItemLedgerEntryType.Sale)],
                SaleDate,
                "POS",
                "POS-0004",
                companyAllowsNegative: true);

            result.Succeeded.ShouldBeTrue();

            // Ten at the real 9.00 from the receipt, and ten estimated -- also at 9.00, because
            // receiving goods updates what the item is believed to cost. The most recent price
            // paid is a far better guess than a figure left over from before the last delivery.
            result.Value.CostAmount.ShouldBe(-180.00m);
            result.Value.EstimatedCostAmount.ShouldBe(-90.00m);
        }

        await using (var context = NewContext())
        {
            var result = await Settlement(context).SettleAsync("ITEM-1001");

            // The early receipt is fully consumed and predates the sale, so nothing settles yet.
            result.Value.ApplicationsSettled.ShouldBe(0);
        }
    }

    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    // ---- Stubs ----

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
        public Guid? UserId => null;

        public string? UserName => "tests";

        public string? DisplayName => "Tests";

        public string? Culture => "en";

        public bool IsSuperUser => true;

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();

        public bool Has(string permissionKey) => true;

        public Guid RequireUserId() => Guid.Empty;
    }

    private sealed class StubClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    /// <summary>
    /// Hands out transaction numbers without a database.
    /// </summary>
    /// <remarks>
    /// The real allocator uses a single atomic SQL statement, which the in-memory provider cannot
    /// run. Stubbing the interface keeps this test about costing rather than about numbering; the
    /// allocator's own concurrency behaviour is a question for a test against real SQL Server.
    /// </remarks>
    private sealed class CountingAllocator : ITransactionNumberAllocator
    {
        private long _last;

        public Task<long> NextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(++_last);
    }

    private sealed class NullPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent asapEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;

        public Task<Platform.Kernel.Results.Result> PublishVetoableAsync<TEvent>(
            TEvent asapEvent,
            CancellationToken cancellationToken = default)
            where TEvent : VetoableEvent
            => Task.FromResult(Platform.Kernel.Results.Result.Success());

        public void Enqueue<TEvent>(TEvent asapEvent)
            where TEvent : IIntegrationEvent
        {
            // Nothing to deliver; these tests are about what reaches the ledger.
        }
    }
}
