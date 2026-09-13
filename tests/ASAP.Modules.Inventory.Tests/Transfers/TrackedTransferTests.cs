using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Modules.Inventory.Transfers;
using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Events;
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
/// What a transfer is worth on the way, and which unit is travelling.
/// </summary>
/// <remarks>
/// A transfer moves goods and nothing else. The value that arrives has to be the value that left:
/// the arriving half used to be priced at whatever the item cost today, so moving old stock after
/// a dearer delivery wrote value into the valuation that no ledger entry explained. And a car that
/// leaves Riyadh has to be the car that arrives in Jeddah, at that car's cost.
/// </remarks>
public sealed class TrackedTransferTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000f5");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000f5");
    private static readonly DateOnly Day = new(2026, 9, 1);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly CountingSeries _series = new();
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Two branches, somewhere in between, a car, a tyre and a lamp.</summary>
    public TrackedTransferTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-tracked-transfer-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        context.Set<Location>().AddRange(
            Place("RUH", "Riyadh"),
            Place("JED", "Jeddah"),
            Place("TRANSIT", "In transit", inTransit: true));

        context.Set<Item>().AddRange(
            Thing("CAR", CostingMethod.Specific, ItemTracking.Serial),
            Thing("TYRE", CostingMethod.Specific, ItemTracking.Lot),
            Thing("LAMP", CostingMethod.Fifo, ItemTracking.None));

        context.SaveChanges();

        static Location Place(string code, string name, bool inTransit = false) => new()
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = code,
            Name = name,
            IsInTransit = inTransit,
            IsSellable = !inTransit,
        };

        static Item Thing(string no, CostingMethod method, ItemTracking tracking) => new()
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = no,
            Description = no,
            BaseUnitOfMeasure = "EA",
            CostingMethod = method,
            Tracking = tracking,
            UnitCost = 1m,
            LastDirectCost = 1m,
        };
    }

    /// <summary>
    /// Old stock moved after a dearer delivery arrives at what it cost, not at today's price.
    /// </summary>
    [Fact]
    public async Task A_transfer_carries_the_cost_that_left()
    {
        await ReceiveAsync("LAMP", 100m, 12m);
        await ReceiveAsync("LAMP", 10m, 20m);

        var transferNo = await RaiseAsync(("LAMP", 40m));

        (await ShipAsync(transferNo)).Succeeded.ShouldBeTrue();

        (await ValueAtAsync("TRANSIT")).ShouldBe(480m, "forty of the oldest at twelve, not forty at twenty");

        (await ReceiveTransferAsync(transferNo)).Succeeded.ShouldBeTrue();

        (await ValueAtAsync("JED")).ShouldBe(480m);
        (await ValueAtAsync("TRANSIT")).ShouldBe(0m);
        (await TotalValueAsync()).ShouldBe(1_400m, "a transfer moves value; it never makes any");

        await using var context = NewContext();

        var lamp = await context.Set<Item>().SingleAsync(i => i.No == "LAMP");

        lamp.UnitCost.ShouldBe(20m, "a transfer says nothing about what the item costs to buy");
    }

    /// <summary>
    /// The car that leaves is the car that arrives, at that car's cost, without being named twice.
    /// </summary>
    [Fact]
    public async Task The_car_shipped_is_the_car_received()
    {
        await ReceiveAsync("CAR", 1m, 80_000m, "VIN-OLD");
        await ReceiveAsync("CAR", 1m, 97_000m, "VIN-NEW");

        var transferNo = await RaiseAsync(("CAR", 1m));

        var shipped = await ShipAsync(transferNo, (10, ["vin-new"]));

        shipped.Succeeded.ShouldBeTrue();
        (await ValueAtAsync("TRANSIT")).ShouldBe(97_000m);

        await using (var context = NewContext())
        {
            var line = await context.Set<TransferOrderLine>().Include(l => l.Units).SingleAsync();

            line.Units.Single().TrackingNo.ShouldBe("VIN-NEW");
        }

        (await ReceiveTransferAsync(transferNo)).Succeeded.ShouldBeTrue("nobody at Jeddah had to read the chassis number");

        (await SerialAtAsync("VIN-NEW")).ShouldBe(("JED", 97_000m));
        (await SerialAtAsync("VIN-OLD")).ShouldBe(("RUH", 80_000m));
    }

    /// <summary>A tracked line shipped without naming its units is refused, and says which line.</summary>
    [Fact]
    public async Task A_tracked_line_without_its_numbers_is_refused_by_its_own_line_number()
    {
        await ReceiveAsync("LAMP", 5m, 10m);
        await ReceiveAsync("CAR", 1m, 80_000m, "VIN-1");

        var transferNo = await RaiseAsync(("LAMP", 1m), ("CAR", 1m));

        var shipped = await ShipAsync(transferNo);

        shipped.Failed.ShouldBeTrue();

        var refusal = shipped.Messages.Single(m => m.Code == InventoryMessages.TrackingNumberRequired);

        refusal.Arguments["LineNo"].ShouldBe(20, "the transfer's line, not the fourth movement of the posting");
    }

    /// <summary>A car still in Riyadh's neighbour cannot leave Riyadh.</summary>
    [Fact]
    public async Task A_unit_that_is_not_at_the_source_cannot_ship()
    {
        await ReceiveAsync("CAR", 1m, 80_000m, "VIN-1", "JED");

        var transferNo = await RaiseAsync(("CAR", 1m));

        var shipped = await ShipAsync(transferNo, (10, ["VIN-1"]));

        shipped.Failed.ShouldBeTrue();
        shipped.Messages.ShouldContain(m => m.Code == InventoryMessages.TrackedUnitNotOnHand);
    }

    /// <summary>Two cars left and one arrived: which one has to be said.</summary>
    [Fact]
    public async Task A_short_arrival_of_several_units_must_say_which_arrived()
    {
        await ReceiveAsync("CAR", 1m, 80_000m, "VIN-1");
        await ReceiveAsync("CAR", 1m, 90_000m, "VIN-2");

        var transferNo = await RaiseAsync(("CAR", 2m));
        await ShipAsync(transferNo, (10, ["VIN-1", "VIN-2"]));

        var guessed = await ReceiveTransferAsync(transferNo, shortages: new Dictionary<string, decimal> { ["CAR"] = 1m });

        guessed.Failed.ShouldBeTrue();
        guessed.Messages.ShouldContain(m => m.Code == InventoryMessages.TransferShortNeedsTrackingNos);

        var named = await ReceiveTransferAsync(transferNo, arrived: (10, ["VIN-2"]));

        named.Succeeded.ShouldBeTrue();
        named.Value.Status.ShouldBe(TransferStatus.PartiallyReceived);

        (await SerialAtAsync("VIN-2")).ShouldBe(("JED", 90_000m));
        (await SerialAtAsync("VIN-1")).ShouldBe(("TRANSIT", 80_000m), "the missing car is the one still on its way");
    }

    /// <summary>A unit that is not travelling on the line cannot arrive on it.</summary>
    [Fact]
    public async Task A_unit_not_in_transit_cannot_arrive()
    {
        await ReceiveAsync("CAR", 1m, 80_000m, "VIN-1");

        var transferNo = await RaiseAsync(("CAR", 1m));
        await ShipAsync(transferNo, (10, ["VIN-1"]));

        var received = await ReceiveTransferAsync(transferNo, arrived: (10, ["VIN-9"]));

        received.Failed.ShouldBeTrue();
        received.Messages.ShouldContain(m => m.Code == InventoryMessages.TransferUnitNotInTransit);
    }

    /// <summary>Part of one lot can arrive, and the rest of it stays in transit.</summary>
    [Fact]
    public async Task Part_of_a_lot_can_arrive()
    {
        await ReceiveAsync("TYRE", 10m, 250m, "LOT-A");

        var transferNo = await RaiseAsync(("TYRE", 10m));
        (await ShipAsync(transferNo, (10, ["LOT-A"]))).Succeeded.ShouldBeTrue();

        var received = await ReceiveTransferAsync(transferNo, shortages: new Dictionary<string, decimal> { ["TYRE"] = 8m });

        received.Succeeded.ShouldBeTrue();
        (await ValueAtAsync("JED")).ShouldBe(2_000m);
        (await ValueAtAsync("TRANSIT")).ShouldBe(500m);
    }

    /// <summary>Closes every context the test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private async Task ReceiveAsync(string itemNo, decimal quantity, decimal unitCost, string? trackingNo = null, string location = "RUH")
    {
        await using var context = NewContext();

        var result = await Posting(context).PostAsync(
            [new StockMovementRequest(itemNo, location, quantity, unitCost, ItemLedgerEntryType.Purchase, TrackingNo: trackingNo)],
            Day,
            "PURCH",
            "PO-1",
            companyAllowsNegative: false);

        result.Succeeded.ShouldBeTrue();
    }

    private async Task<string> RaiseAsync(params (string ItemNo, decimal Quantity)[] lines)
    {
        await using var context = NewContext();

        var created = await Transfers(context).CreateAsync(
            "RUH",
            "JED",
            [.. lines.Select(static l => new TransferLineRequest(l.ItemNo, l.Quantity))]);

        created.Succeeded.ShouldBeTrue();
        return created.Value.No;
    }

    private async Task<Result<TransferReceipt>> ShipAsync(string transferNo, params (int LineNo, string[] Numbers)[] numbers)
    {
        await using var context = NewContext();

        return await Transfers(context).ShipAsync(
            transferNo,
            companyAllowsNegative: false,
            trackingNos: numbers.ToDictionary(static n => n.LineNo, static n => (IReadOnlyList<string>)n.Numbers));
    }

    private async Task<Result<TransferReceipt>> ReceiveTransferAsync(
        string transferNo,
        Dictionary<string, decimal>? shortages = null,
        (int LineNo, string[] Numbers)? arrived = null)
    {
        await using var context = NewContext();

        return await Transfers(context).ReceiveAsync(
            transferNo,
            shortages,
            companyAllowsNegative: false,
            arrivedTrackingNos: arrived is { } a
                ? new Dictionary<int, IReadOnlyList<string>> { [a.LineNo] = a.Numbers }
                : null);
    }

    private async Task<decimal> ValueAtAsync(string location)
    {
        await using var context = NewContext();

        return await (
                from value in context.Set<ValueEntry>()
                join entry in context.Set<ItemLedgerEntry>() on value.ItemLedgerEntryId equals entry.Id
                where entry.LocationCode == location
                select value.CostAmount)
            .SumAsync();
    }

    private async Task<decimal> TotalValueAsync()
    {
        await using var context = NewContext();

        return await context.Set<ValueEntry>().SumAsync(static v => v.CostAmount);
    }

    private async Task<(string Location, decimal Value)> SerialAtAsync(string serial)
    {
        await using var context = NewContext();

        var layer = await context.Set<ItemLedgerEntry>()
            .SingleAsync(e => e.SerialNo == serial && e.RemainingQuantity > 0m);

        var value = await context.Set<ValueEntry>()
            .Where(v => v.ItemLedgerEntryId == layer.Id)
            .SumAsync(static v => v.CostAmount);

        return (layer.LocationCode, value);
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
        _opened.Add(context);
        return context;
    }

    private static MessageCatalog Catalog() => new([.. PlatformMessages.All, .. InventoryMessages.All]);

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

    private TransferService Transfers(AsapDbContext context)
        => new(
            context,
            Posting(context),
            new BinPicker(context, Catalog()),
            Catalog(),
            _series,
            _tenancy,
            _clock,
            NullLogger<TransferService>.Instance);

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

        public Task<long> NextAsync(CancellationToken cancellationToken = default) => Task.FromResult(++_last);
    }

    private sealed class CountingSeries : INumberSeriesService
    {
        private int _last;

        public Task<Result<string>> NextAsync(string seriesCode, DateOnly documentDate, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success($"{seriesCode}-{++_last:00000}"));

        public Task<Result<string>> PeekAsync(string seriesCode, DateOnly documentDate, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success($"{seriesCode}-{_last + 1:00000}"));

        public Task<Result> ValidateManualAsync(string seriesCode, string number, DateOnly documentDate, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }

    private sealed class NullPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent asapEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;

        public Task<Result> PublishVetoableAsync<TEvent>(TEvent asapEvent, CancellationToken cancellationToken = default)
            where TEvent : VetoableEvent => Task.FromResult(Result.Success());

        public void Enqueue<TEvent>(TEvent asapEvent)
            where TEvent : IIntegrationEvent
        {
            // Nothing to deliver.
        }
    }
}
