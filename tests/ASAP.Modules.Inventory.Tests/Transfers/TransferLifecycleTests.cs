using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Modules.Inventory.Transfers;
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

namespace ASAP.Modules.Inventory.Tests.Transfers;

/// <summary>
/// Follows goods from one branch to another.
///
/// The point of the whole design is the gap in the middle. Goods leave Riyadh on Monday and reach
/// Jeddah on Wednesday, and for those two days they belong to the company, sit on the balance
/// sheet, and are at neither branch. A transfer that moved stock instantaneously would make them
/// vanish from the valuation for the length of the journey.
/// </summary>
public sealed class TransferLifecycleTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    public TransferLifecycleTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-transfer-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Seed();
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
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

        Where("RUH", "Riyadh", sellable: true);
        Where("JED", "Jeddah", sellable: true);
        Where("TRANSIT", "In transit", sellable: false, inTransit: true);

        context.Set<Item>().Add(new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "ITEM-1001",
            Description = "Desk lamp",
            BaseUnitOfMeasure = "PCS",
            CostingMethod = CostingMethod.Fifo,
            UnitCost = 12.00m,
            LastDirectCost = 12.00m,
        });

        var transfer = new TransferOrder
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "TR-0001",
            FromLocationCode = "RUH",
            ToLocationCode = "JED",
            ShipmentDate = new DateOnly(2026, 8, 24),
            Status = TransferStatus.Released,
        };

        transfer.Lines.Add(new TransferOrderLine
        {
            TenantId = Tenant,
            CompanyId = Company,
            TransferOrderId = transfer.Id,
            LineNo = 1,
            ItemNo = "ITEM-1001",
            Description = "Desk lamp",
            Quantity = 40,
        });

        context.Set<TransferOrder>().Add(transfer);
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
            _clock,
            _allocator,
            NullLogger<StockPostingService>.Instance);
    }

    private TransferService Transfers(AsapDbContext context)
        => new(
            context,
            Posting(context),
            new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All]),
            _clock,
            NullLogger<TransferService>.Instance);

    private async Task StockAtRiyadhAsync(decimal quantity)
    {
        await using var context = NewContext();

        await Posting(context).PostAsync(
            [new StockMovementRequest("ITEM-1001", "RUH", quantity, 12.00m, ItemLedgerEntryType.Purchase)],
            new DateOnly(2026, 8, 20),
            "PURCH",
            "PINV-1",
            companyAllowsNegative: false);
    }

    private async Task<Dictionary<string, decimal>> OnHandAsync()
    {
        await using var context = NewContext();

        var rows = await context.Set<ItemLedgerEntry>()
            .GroupBy(e => e.LocationCode)
            .Select(g => new { Location = g.Key, Quantity = g.Sum(e => e.Quantity) })
            .ToListAsync();

        return rows.ToDictionary(r => r.Location, r => r.Quantity);
    }

    [Fact]
    public async Task Goods_sit_in_transit_between_shipping_and_receiving()
    {
        await StockAtRiyadhAsync(100);

        await using (var context = NewContext())
        {
            var shipped = await Transfers(context).ShipAsync("TR-0001", companyAllowsNegative: false);

            shipped.Succeeded.ShouldBeTrue();
            shipped.Value.Status.ShouldBe(TransferStatus.Shipped);
        }

        // The moment that matters. The goods have left Riyadh, have not reached Jeddah, and remain
        // the property of the company -- visible, valued, and attributable to a document.
        var midJourney = await OnHandAsync();
        midJourney["RUH"].ShouldBe(60);
        midJourney.GetValueOrDefault("JED").ShouldBe(0);
        midJourney["TRANSIT"].ShouldBe(40);
        midJourney.Values.Sum().ShouldBe(100, "nothing may vanish while it travels");

        await using (var context = NewContext())
        {
            var received = await Transfers(context).ReceiveAsync("TR-0001");

            received.Succeeded.ShouldBeTrue();
            received.Value.Status.ShouldBe(TransferStatus.Received);
        }

        var arrived = await OnHandAsync();
        arrived["RUH"].ShouldBe(60);
        arrived["JED"].ShouldBe(40);
        arrived["TRANSIT"].ShouldBe(0);
        arrived.Values.Sum().ShouldBe(100);
    }

    [Fact]
    public async Task A_short_receipt_leaves_the_difference_in_transit_rather_than_writing_it_off()
    {
        // The honest position: the goods left, they did not arrive, and until somebody
        // investigates nobody knows whether they are lost, stolen, or on the next lorry.
        await StockAtRiyadhAsync(100);

        await using (var context = NewContext())
        {
            await Transfers(context).ShipAsync("TR-0001", companyAllowsNegative: false);
        }

        await using (var context = NewContext())
        {
            var received = await Transfers(context)
                .ReceiveAsync("TR-0001", new Dictionary<string, decimal> { ["ITEM-1001"] = 35 });

            received.Succeeded.ShouldBeTrue();
            received.Value.Status.ShouldBe(TransferStatus.PartiallyReceived);

            var warning = received.Messages
                .FirstOrDefault(m => m.Code.Value == "INV.TRANSFER.SHORT_RECEIPT");

            warning.ShouldNotBeNull();
            warning.Detail.ShouldNotBeNull().ShouldContain("5");
            warning.Resolution.ShouldNotBeNull().ShouldContain("write them off");
        }

        var after = await OnHandAsync();
        after["JED"].ShouldBe(35);
        after["TRANSIT"].ShouldBe(5);
        after.Values.Sum().ShouldBe(100, "the missing five are still somewhere, not gone");
    }

    [Fact]
    public async Task Shipping_twice_is_refused()
    {
        await StockAtRiyadhAsync(100);

        await using (var context = NewContext())
        {
            await Transfers(context).ShipAsync("TR-0001", companyAllowsNegative: false);
        }

        await using (var context = NewContext())
        {
            var again = await Transfers(context).ShipAsync("TR-0001", companyAllowsNegative: false);

            again.Failed.ShouldBeTrue();
            again.Failures.ShouldContain(m => m.Code.Value == "INV.TRANSFER.ALREADY_SHIPPED");
        }
    }

    [Fact]
    public async Task Receiving_before_shipping_is_refused()
    {
        await using var context = NewContext();

        var received = await Transfers(context).ReceiveAsync("TR-0001");

        received.Failed.ShouldBeTrue();
        received.Failures.ShouldContain(m => m.Code.Value == "INV.TRANSFER.NOT_SHIPPED");
    }

    [Fact]
    public async Task A_transfer_that_does_not_exist_is_reported_by_number()
    {
        await using var context = NewContext();

        var result = await Transfers(context).ShipAsync("TR-9999", companyAllowsNegative: false);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(m => m.Code.Value == "INV.TRANSFER.NOT_FOUND");
    }

    [Fact]
    public async Task Shipping_more_than_is_on_hand_is_refused_when_negative_stock_is_not_allowed()
    {
        await StockAtRiyadhAsync(10);

        await using var context = NewContext();

        var shipped = await Transfers(context).ShipAsync("TR-0001", companyAllowsNegative: false);

        shipped.Failed.ShouldBeTrue();
        shipped.Failures.ShouldContain(m => m.Code.Value == "INV.STOCK.NEGATIVE_BLOCKED");
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

        public Task<Result> PublishVetoableAsync<TEvent>(
            TEvent asapEvent,
            CancellationToken cancellationToken = default)
            where TEvent : VetoableEvent => Task.FromResult(Result.Success());

        public void Enqueue<TEvent>(TEvent asapEvent)
            where TEvent : IIntegrationEvent
        {
            // Nothing to deliver; these tests are about where the stock is.
        }
    }
}
