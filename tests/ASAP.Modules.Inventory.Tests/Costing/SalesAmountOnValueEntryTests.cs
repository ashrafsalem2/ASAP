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
/// What a sale is recorded as having sold for, when it does not come off one receipt.
/// </summary>
/// <remarks>
/// <para>
/// The sales amount belongs to the movement, and the movement is split across however many cost
/// layers it has to draw on. Every other test in this suite posts a clean sequence where a sale
/// comes off one layer, and in that case there is nothing to get wrong -- which is exactly why the
/// bug this covers survived: a sale of ten that took eight from one receipt and two from another
/// wrote the full sales amount twice, and the margin report read the company's revenue as double.
/// </para>
/// <para>
/// The second half is the other side of it. A return has to carry the sale coming off, or an order
/// returned in full keeps the margin it had the day it shipped and the report says the company
/// made money it gave back.
/// </para>
/// </remarks>
public sealed class SalesAmountOnValueEntryTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000b1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000ba");
    private static readonly DateOnly Day = new(2026, 8, 20);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up a shop with two receipts at different costs.</summary>
    public SalesAmountOnValueEntryTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-sales-amount-{Guid.CreateVersion7()}")
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
            UnitCost = 10m,
            LastDirectCost = 10m,
        });

        context.SaveChanges();
    }

    /// <summary>
    /// A sale spanning two receipts records what the customer paid once, not once per receipt.
    /// </summary>
    [Fact]
    public async Task A_sale_across_two_layers_records_its_revenue_once()
    {
        await Receive(8m, 10m);
        await Receive(8m, 12.50m);

        await using (var context = NewContext())
        {
            var result = await Posting(context).PostAsync(
                [new StockMovementRequest(
                    "ITEM-1001",
                    "SHOP",
                    -10m,
                    EntryType: ItemLedgerEntryType.Sale,
                    SalesAmount: 450m)],
                Day,
                "SALES",
                "SO-0001",
                companyAllowsNegative: false);

            result.Succeeded.ShouldBeTrue();
        }

        await using (var context = NewContext())
        {
            var sold = await context.Set<ValueEntry>()
                .Where(v => v.ItemLedgerEntryType == ItemLedgerEntryType.Sale)
                .ToListAsync();

            sold.Count.ShouldBe(2, "the sale came off two receipts");

            sold.Sum(v => v.SalesAmount).ShouldBe(
                450m,
                "the customer was charged 450 once, however many layers the goods came off");
        }
    }

    /// <summary>
    /// The apportionment adds back to the whole, rather than losing a penny to rounding on
    /// every layer it touches.
    /// </summary>
    [Fact]
    public async Task An_amount_that_does_not_divide_evenly_still_adds_back_to_the_whole()
    {
        await Receive(1m, 10m);
        await Receive(1m, 11m);
        await Receive(1m, 12m);

        await using (var context = NewContext())
        {
            await Posting(context).PostAsync(
                [new StockMovementRequest(
                    "ITEM-1001",
                    "SHOP",
                    -3m,
                    EntryType: ItemLedgerEntryType.Sale,

                    // A third of this is 33.333..., which is the case that loses money quietly.
                    SalesAmount: 100m)],
                Day,
                "SALES",
                "SO-0002",
                companyAllowsNegative: false);
        }

        await using (var context = NewContext())
        {
            var sold = await context.Set<ValueEntry>()
                .Where(v => v.ItemLedgerEntryType == ItemLedgerEntryType.Sale)
                .ToListAsync();

            sold.Count.ShouldBe(3);
            sold.Sum(v => v.SalesAmount).ShouldBe(100m);
        }
    }

    /// <summary>
    /// A return carries the revenue off again, so a sale that came back in full nets to nothing.
    /// </summary>
    [Fact]
    public async Task A_return_takes_the_revenue_back_off()
    {
        await Receive(10m, 10m);

        await using (var context = NewContext())
        {
            await Posting(context).PostAsync(
                [new StockMovementRequest(
                    "ITEM-1001",
                    "SHOP",
                    -10m,
                    EntryType: ItemLedgerEntryType.Sale,
                    SalesAmount: 450m)],
                Day,
                "SALES",
                "SO-0003",
                companyAllowsNegative: false);
        }

        await using (var context = NewContext())
        {
            await Posting(context).PostAsync(
                [new StockMovementRequest(
                    "ITEM-1001",
                    "SHOP",
                    10m,
                    EntryType: ItemLedgerEntryType.SalesReturn,
                    SalesAmount: -450m,
                    AppliesToDocumentNo: "SO-0003")],
                Day,
                "SALES",
                "SO-0003",
                companyAllowsNegative: false);
        }

        await using (var context = NewContext())
        {
            var sold = await context.Set<ValueEntry>()
                .Where(v => v.ItemLedgerEntryType == ItemLedgerEntryType.Sale
                    || v.ItemLedgerEntryType == ItemLedgerEntryType.SalesReturn)
                .ToListAsync();

            sold.Sum(v => v.SalesAmount).ShouldBe(
                0m,
                "everything that was sold came back, so there is no revenue left to report");
        }
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private async Task Receive(decimal quantity, decimal unitCost)
    {
        await using var context = NewContext();

        var result = await Posting(context).PostAsync(
            [new StockMovementRequest("ITEM-1001", "SHOP", quantity, unitCost, ItemLedgerEntryType.Purchase)],
            Day.AddDays(-1),
            "PURCH",
            null,
            companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
        _opened.Add(context);
        return context;
    }

    private StockPostingService Posting(AsapDbContext context)
    {
        var catalog = new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All]);

        return new StockPostingService(
            context,
            new StockAvailability(catalog),
            new LocationBranchLookup(context),
            new NullPublisher(),
            catalog,
            _tenancy,
            new OverrideAuditor(context, _tenancy, new StubUser(), _clock),
            _clock,
            _allocator,
            NullLogger<StockPostingService>.Instance);
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
            // Nothing to deliver; these tests are about what the return is worth.
        }
    }

}
