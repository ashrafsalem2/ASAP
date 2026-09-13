using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
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

namespace ASAP.Modules.Inventory.Tests.Costing;

/// <summary>
/// Each unit carrying its own cost.
/// </summary>
/// <remarks>
/// The case this exists for: two cars bought at different prices, and the newer one sold first.
/// FIFO would cost the sale at the older car's price and leave the dearer one on the books at the
/// wrong value, and both margins would be wrong by the difference. Choosing "specific" used to do
/// exactly that, because nothing implemented it.
/// </remarks>
public sealed class SpecificCostingTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000f3");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000f3");
    private static readonly DateOnly Day = new(2026, 9, 1);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up a showroom, a serially tracked car, a lot-tracked tyre and an ordinary item.</summary>
    public SpecificCostingTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-specific-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        context.Set<Location>().AddRange(Place("SHOW", "Showroom"), Place("YARD", "Yard"));

        context.Set<Item>().AddRange(
            Thing("CAR", CostingMethod.Specific, ItemTracking.Serial),
            Thing("TYRE", CostingMethod.Specific, ItemTracking.Lot),
            Thing("OIL", CostingMethod.Fifo, ItemTracking.None));

        context.SaveChanges();

        static Location Place(string code, string name) => new()
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = code,
            Name = name,
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
            AllowNegativeInventory = true,
        };
    }

    /// <summary>
    /// The newer car sold first costs what the newer car cost.
    /// </summary>
    [Fact]
    public async Task The_car_sold_costs_what_that_car_cost()
    {
        (await PostAsync(Receive("CAR", 1m, 80_000m, "VIN-OLD"))).Succeeded.ShouldBeTrue();
        (await PostAsync(Receive("CAR", 1m, 95_000m, "VIN-NEW"))).Succeeded.ShouldBeTrue();

        var sale = await PostAsync(Issue("CAR", 1m, "VIN-NEW"));

        sale.Succeeded.ShouldBeTrue();
        sale.Value.CostAmount.ShouldBe(-95_000m, "FIFO would have said eighty thousand");

        await using var context = NewContext();

        var left = await context.Set<ItemLedgerEntry>()
            .Where(e => e.ItemNo == "CAR" && e.RemainingQuantity > 0m)
            .ToListAsync();

        left.Single().SerialNo.ShouldBe("VIN-OLD", "the older car is the one still on the books");
    }

    /// <summary>A serial number is one unit.</summary>
    [Fact]
    public async Task A_serial_moves_one_unit()
    {
        var two = await PostAsync(Receive("CAR", 2m, 80_000m, "VIN-1"));

        two.Failed.ShouldBeTrue();
        two.Messages.ShouldContain(m => m.Code == InventoryMessages.SerialMovesOneUnit);
    }

    /// <summary>Two cars cannot share a chassis number, even inside one posting.</summary>
    [Fact]
    public async Task One_serial_cannot_be_received_twice()
    {
        (await PostAsync(Receive("CAR", 1m, 80_000m, "VIN-1"))).Succeeded.ShouldBeTrue();

        var again = await PostAsync(Receive("CAR", 1m, 80_000m, "vin-1"));

        again.Failed.ShouldBeTrue("the number is compared regardless of case");
        again.Messages.ShouldContain(m => m.Code == InventoryMessages.SerialAlreadyOnHand);

        await using var context = NewContext();

        var batch = await Posting(context).PostAsync(
            [Receive("CAR", 1m, 70_000m, "VIN-2"), Receive("CAR", 1m, 70_000m, "VIN-2")],
            Day,
            "TEST",
            null,
            companyAllowsNegative: true);

        batch.Failed.ShouldBeTrue("two lines of one receipt cannot both bring VIN-2 in");
    }

    /// <summary>
    /// A specific unit is never sold ahead of its receipt, whatever the company allows.
    /// </summary>
    /// <remarks>
    /// There is no estimate of what one particular car cost, so the ordinary negative-stock route
    /// has nothing honest to value it at.
    /// </remarks>
    [Fact]
    public async Task A_unit_not_here_cannot_be_sold_even_where_negative_stock_is_allowed()
    {
        var sale = await PostAsync(Issue("CAR", 1m, "VIN-GHOST"));

        sale.Failed.ShouldBeTrue();
        sale.Messages.ShouldContain(m => m.Code == InventoryMessages.TrackedUnitNotOnHand);
    }

    /// <summary>A serial at the yard is not here in the showroom.</summary>
    [Fact]
    public async Task A_unit_elsewhere_is_not_here()
    {
        await PostAsync(Receive("CAR", 1m, 80_000m, "VIN-1", "YARD"));

        var sale = await PostAsync(Issue("CAR", 1m, "VIN-1", "SHOW"));

        sale.Failed.ShouldBeTrue();
        sale.Messages.ShouldContain(m => m.Code == InventoryMessages.TrackedUnitNotOnHand);
    }

    /// <summary>A tracked item without a number is refused.</summary>
    [Fact]
    public async Task A_tracked_item_needs_its_number()
    {
        var unnamed = await PostAsync(Receive("CAR", 1m, 80_000m, null));

        unnamed.Failed.ShouldBeTrue();
        unnamed.Messages.ShouldContain(m => m.Code == InventoryMessages.TrackingNumberRequired);
    }

    /// <summary>A number on an untracked item is refused rather than recorded and ignored.</summary>
    [Fact]
    public async Task A_number_on_an_untracked_item_is_refused()
    {
        var oil = await PostAsync(Receive("OIL", 10m, 5m, "LOT-7"));

        oil.Failed.ShouldBeTrue();
        oil.Messages.ShouldContain(m => m.Code == InventoryMessages.TrackingOnUntrackedItem);
    }

    /// <summary>A lot costs what that lot cost, and cannot give more than it holds.</summary>
    [Fact]
    public async Task A_lot_costs_what_that_lot_cost()
    {
        await PostAsync(Receive("TYRE", 10m, 200m, "LOT-A"));
        await PostAsync(Receive("TYRE", 10m, 260m, "LOT-B"));

        var sale = await PostAsync(Issue("TYRE", 4m, "LOT-B"));

        sale.Succeeded.ShouldBeTrue();
        sale.Value.CostAmount.ShouldBe(-1_040m, "four at two hundred and sixty, not at two hundred");

        var tooMany = await PostAsync(Issue("TYRE", 7m, "LOT-B"));

        tooMany.Failed.ShouldBeTrue("six are left in lot B");
        tooMany.Messages.ShouldContain(m => m.Code == InventoryMessages.TrackedUnitNotOnHand);
    }

    /// <summary>A car sold and brought back can be sold again.</summary>
    [Fact]
    public async Task A_car_returned_can_be_sold_again()
    {
        await PostAsync(Receive("CAR", 1m, 80_000m, "VIN-1"));
        await PostAsync(Issue("CAR", 1m, "VIN-1"), "SALE-1");

        var back = await PostAsync(
            new StockMovementRequest("CAR", "SHOW", 1m, 0m, ItemLedgerEntryType.SalesReturn, AppliesToDocumentNo: "SALE-1", TrackingNo: "VIN-1"));

        back.Succeeded.ShouldBeTrue();
        back.Value.CostAmount.ShouldBe(80_000m, "it comes back at what it left at");

        (await PostAsync(Issue("CAR", 1m, "VIN-1"))).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// One of two cars sold together comes back at what that car left at, not at their average.
    /// </summary>
    [Fact]
    public async Task A_returned_car_comes_back_at_its_own_cost()
    {
        await PostAsync(Receive("CAR", 1m, 80_000m, "VIN-A"));
        await PostAsync(Receive("CAR", 1m, 95_000m, "VIN-B"));

        await using (var context = NewContext())
        {
            (await Posting(context).PostAsync(
                    [Issue("CAR", 1m, "VIN-A"), Issue("CAR", 1m, "VIN-B")],
                    Day,
                    "TEST",
                    "SALE-2",
                    companyAllowsNegative: true))
                .Succeeded.ShouldBeTrue();
        }

        var back = await PostAsync(Returned("VIN-B", "SALE-2"));

        back.Succeeded.ShouldBeTrue();
        back.Value.CostAmount.ShouldBe(95_000m, "the average of the two would say eighty-seven and a half");
    }

    /// <summary>A car that did not leave on a sale cannot come back against it.</summary>
    [Fact]
    public async Task A_car_cannot_come_back_against_a_sale_it_was_not_on()
    {
        await PostAsync(Receive("CAR", 1m, 80_000m, "VIN-A"));
        await PostAsync(Receive("CAR", 1m, 95_000m, "VIN-B"));
        await PostAsync(Issue("CAR", 1m, "VIN-A"), "SALE-1");
        await PostAsync(Issue("CAR", 1m, "VIN-B"), "SALE-2");

        var back = await PostAsync(Returned("VIN-B", "SALE-1"));

        back.Failed.ShouldBeTrue();
        back.Messages.ShouldContain(m => m.Code == InventoryMessages.TrackedUnitNotOnDocument);
    }

    /// <summary>A car goes back to the vendor it came from only if it came in on that order.</summary>
    [Fact]
    public async Task A_car_goes_back_only_against_the_order_it_arrived_on()
    {
        await PostAsync(Receive("CAR", 1m, 80_000m, "VIN-A"), "PO-1");
        await PostAsync(Receive("CAR", 1m, 95_000m, "VIN-B"), "PO-2");

        var wrongOrder = await PostAsync(
            new StockMovementRequest("CAR", "SHOW", -1m, 0m, ItemLedgerEntryType.PurchaseReturn, AppliesToDocumentNo: "PO-1", TrackingNo: "VIN-B"));

        wrongOrder.Failed.ShouldBeTrue();
        wrongOrder.Messages.ShouldContain(m => m.Code == InventoryMessages.TrackedUnitNotOnDocument);

        var rightOrder = await PostAsync(
            new StockMovementRequest("CAR", "SHOW", -1m, 0m, ItemLedgerEntryType.PurchaseReturn, AppliesToDocumentNo: "PO-2", TrackingNo: "VIN-B"));

        rightOrder.Succeeded.ShouldBeTrue();
        rightOrder.Value.CostAmount.ShouldBe(-95_000m);
    }

    /// <summary>
    /// A car goes back against the line it arrived on, not another line of the same order.
    /// </summary>
    /// <remarks>
    /// Two lines of one order at different prices. Sending the dearer car back against the cheaper
    /// line relieves ninety-five thousand of stock against eighty thousand of accrual.
    /// </remarks>
    [Fact]
    public async Task A_car_goes_back_against_the_line_it_arrived_on()
    {
        await PostAsync(Receive("CAR", 1m, 80_000m, "VIN-A") with { LineNo = 10 }, "PO-1");
        await PostAsync(Receive("CAR", 1m, 95_000m, "VIN-B") with { LineNo = 20 }, "PO-1");

        var wrongLine = await PostAsync(
            new StockMovementRequest("CAR", "SHOW", -1m, 0m, ItemLedgerEntryType.PurchaseReturn, AppliesToDocumentNo: "PO-1", TrackingNo: "VIN-B", LineNo: 10));

        wrongLine.Failed.ShouldBeTrue();
        wrongLine.Messages.ShouldContain(m => m.Code == InventoryMessages.TrackedUnitNotOnDocument);

        var rightLine = await PostAsync(
            new StockMovementRequest("CAR", "SHOW", -1m, 0m, ItemLedgerEntryType.PurchaseReturn, AppliesToDocumentNo: "PO-1", TrackingNo: "VIN-B", LineNo: 20));

        rightLine.Succeeded.ShouldBeTrue();
    }

    /// <summary>A refusal names the document's own line where the caller said which it was.</summary>
    [Fact]
    public async Task A_refusal_names_the_document_line()
    {
        var unnamed = await PostAsync(Receive("CAR", 1m, 80_000m, null) with { LineNo = 30 });

        unnamed.Messages.Single().Arguments["LineNo"].ShouldBe(30);
    }

    private static StockMovementRequest Returned(string serial, string saleNo)
        => new("CAR", "SHOW", 1m, 0m, ItemLedgerEntryType.SalesReturn, AppliesToDocumentNo: saleNo, TrackingNo: serial);

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private static StockMovementRequest Receive(string itemNo, decimal quantity, decimal unitCost, string? tracking, string location = "SHOW")
        => new(itemNo, location, quantity, unitCost, ItemLedgerEntryType.Purchase, TrackingNo: tracking);

    private static StockMovementRequest Issue(string itemNo, decimal quantity, string? tracking, string location = "SHOW")
        => new(itemNo, location, -quantity, 0m, ItemLedgerEntryType.Sale, TrackingNo: tracking);

    private async Task<Result<StockPostingReceipt>> PostAsync(StockMovementRequest movement, string? documentNo = null)
    {
        await using var context = NewContext();

        return await Posting(context).PostAsync([movement], Day, "TEST", documentNo, companyAllowsNegative: true);
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
            new Inventory.Costing.StockAvailability(Catalog()),
            new LocationBranchLookup(context),
            new NullPublisher(),
            Catalog(),
            _tenancy,
            new OverrideAuditor(context, _tenancy, new StubUser(), _clock),
            _clock,
            _allocator,
            NullLogger<StockPostingService>.Instance);

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

        public Task<long> NextAsync(CancellationToken cancellationToken = default) => Task.FromResult(++_next);
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

/// <summary>How a document line and its serials become movements.</summary>
public sealed class TrackedMovementsTests
{
    /// <summary>Three serials on a line of three become three units, and the sale divides without losing a halala.</summary>
    [Fact]
    public void Three_serials_become_three_units()
    {
        var line = new StockMovementRequest("CAR", "SHOW", -3m, 0m, ItemLedgerEntryType.Sale, SalesAmount: 100m);

        var split = TrackedMovements.Split(line, ["vin-1", "VIN-2", " VIN-3 "]);

        split.ShouldNotBeNull();
        split.Count.ShouldBe(3);
        split.ShouldAllBe(m => m.Quantity == -1m);
        split.Select(static m => m.TrackingNo).ShouldBe(["VIN-1", "VIN-2", "VIN-3"]);
        split.Sum(static m => m.SalesAmount).ShouldBe(100m, "33.33 twice and 33.34 once");
    }

    /// <summary>One number is a lot, and the whole line goes under it.</summary>
    [Fact]
    public void One_number_carries_the_whole_line()
    {
        var split = TrackedMovements.Split(
            new StockMovementRequest("TYRE", "SHOW", 40m, 200m, ItemLedgerEntryType.Purchase),
            ["lot-a"]);

        split.ShouldNotBeNull().Single().Quantity.ShouldBe(40m);
        split.Single().TrackingNo.ShouldBe("LOT-A");
    }

    /// <summary>Several numbers that do not match the quantity are refused.</summary>
    [Fact]
    public void Numbers_that_do_not_match_the_quantity_are_refused()
        => TrackedMovements.Split(
                new StockMovementRequest("CAR", "SHOW", 3m, 80_000m, ItemLedgerEntryType.Purchase),
                ["VIN-1", "VIN-2"])
            .ShouldBeNull();

    /// <summary>No numbers leaves the line as it was, for posting to judge.</summary>
    [Fact]
    public void No_numbers_leaves_the_line_alone()
        => TrackedMovements.Split(
                new StockMovementRequest("OIL", "SHOW", 3m, 5m, ItemLedgerEntryType.Purchase),
                null)
            .ShouldNotBeNull().Single().TrackingNo.ShouldBeNull();
}

/// <summary>Setting how an item is costed.</summary>
public sealed class ItemCostingTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000f4");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000f4");

    private readonly DbContextOptions<AsapDbContext> _options = new DbContextOptionsBuilder<AsapDbContext>()
        .UseInMemoryDatabase($"asap-item-costing-{Guid.CreateVersion7()}")
        .Options;

    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Specific costing with nothing to tell units apart is refused.</summary>
    [Fact]
    public async Task Specific_costing_needs_tracking()
    {
        using var context = Seeded();

        var result = await Costing(context).SetAsync("CAR", CostingMethod.Specific, ItemTracking.None);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == InventoryMessages.SpecificCostingNeedsTracking);
    }

    /// <summary>Tracking on an item that is not costed specifically is refused.</summary>
    [Fact]
    public async Task Tracking_needs_specific_costing()
    {
        using var context = Seeded();

        var result = await Costing(context).SetAsync("CAR", CostingMethod.Fifo, ItemTracking.Serial);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == InventoryMessages.TrackingNeedsSpecificCosting);
    }

    /// <summary>Once anything has posted, the method cannot change.</summary>
    [Fact]
    public async Task The_method_is_locked_once_anything_has_posted()
    {
        using var context = Seeded(posted: true);

        var result = await Costing(context).SetAsync("CAR", CostingMethod.Specific, ItemTracking.Serial);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == InventoryMessages.CostingMethodLocked);
    }

    /// <summary>Before anything has posted, it can.</summary>
    [Fact]
    public async Task Before_anything_posts_it_can_be_set()
    {
        using var context = Seeded();

        var result = await Costing(context).SetAsync("CAR", CostingMethod.Specific, ItemTracking.Serial);

        result.Succeeded.ShouldBeTrue();
        result.Value.Tracking.ShouldBe(ItemTracking.Serial);
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private AsapDbContext Seeded(bool posted = false)
    {
        var tenancy = new Tenancy();
        var context = new AsapDbContext(_options, tenancy, new User(), new Clock(), [new InventorySchema()]);

        _opened.Add(context);

        context.Set<Item>().Add(new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "CAR",
            Description = "Car",
            BaseUnitOfMeasure = "EA",
            HasLedgerEntries = posted,
        });

        context.SaveChanges();

        return context;
    }

    private static ItemCostingService Costing(AsapDbContext context)
        => new(context, new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All]));

    private sealed class Tenancy : ITenantContext
    {
        public Guid? TenantId { get; set; } = Tenant;

        public Guid? CompanyId { get; set; } = Company;

        public Guid? BranchId { get; set; }

        public bool IsCrossTenantOperation { get; set; }

        public Guid RequireTenantId() => Tenant;

        public Guid RequireCompanyId() => Company;
    }

    private sealed class User : IUserContext
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

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => new(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc);

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }
}
