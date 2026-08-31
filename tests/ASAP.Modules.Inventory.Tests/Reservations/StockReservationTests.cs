using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Modules.Inventory.Reservations;
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

namespace ASAP.Modules.Inventory.Tests.Reservations;

/// <summary>
/// What it means to promise stock to one document and not another.
/// </summary>
/// <remarks>
/// A reservation posts nothing, so none of this is about money. It is about the difference between
/// what is on the shelf and what is left to promise, and about the two places that difference has
/// to be visible: when somebody tries to reserve more than is free, and when somebody tries to
/// ship stock that was promised to somebody else.
/// </remarks>
public sealed class StockReservationTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000c1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000ca");
    private static readonly DateOnly Day = new(2026, 8, 20);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up a shop with ten on the shelf.</summary>
    public StockReservationTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-reservations-{Guid.CreateVersion7()}")
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
            IsSellable = true,
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

    /// <summary>Holding stock does not move it. On hand is a fact about the shelf.</summary>
    [Fact]
    public async Task Reserving_moves_nothing()
    {
        await Receive(10m);

        await using (var context = NewContext())
        {
            (await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 4m, "SO-0001"))
                .Succeeded.ShouldBeTrue();
        }

        await using (var context = NewContext())
        {
            context.Set<ItemLedgerEntry>().Count().ShouldBe(1, "nothing new was posted");

            var rows = await Reservations(context).AvailabilityAsync();
            var row = rows.Single();

            row.QuantityOnHand.ShouldBe(10m);
            row.QuantityReserved.ShouldBe(4m);
            row.QuantityAvailable.ShouldBe(6m);
        }
    }

    /// <summary>
    /// Reserving more than is free is refused, not warned about.
    /// </summary>
    /// <remarks>
    /// The difference from selling into negative stock is who is standing there. A sale below zero
    /// is a decision made with a customer in front of you and the goods visible; a reservation is
    /// planning at a desk with nobody waiting, and a promise against stock that is not there is
    /// not a promise.
    /// </remarks>
    [Fact]
    public async Task Reserving_more_than_is_free_is_refused()
    {
        await Receive(10m);

        await using var context = NewContext();

        (await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 7m, "SO-0001"))
            .Succeeded.ShouldBeTrue();

        var second = await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 5m, "SO-0002");

        second.Failed.ShouldBeTrue();
        second.Messages.ShouldContain(m => m.Code == InventoryMessages.NotEnoughToReserve);
    }

    /// <summary>
    /// A document adding to its own reservation is not refused by what it already holds.
    /// </summary>
    [Fact]
    public async Task A_document_is_not_blocked_by_its_own_reservation()
    {
        await Receive(10m);

        await using var context = NewContext();
        var reservations = Reservations(context);

        (await reservations.ReserveAsync("ITEM-1001", "SHOP", 6m, "SO-0001")).Succeeded.ShouldBeTrue();
        (await reservations.ReserveAsync("ITEM-1001", "SHOP", 4m, "SO-0001")).Succeeded.ShouldBeTrue();

        var held = await reservations.ListAsync("SO-0001");

        held.Single().QuantityOutstanding.ShouldBe(10m);
    }

    /// <summary>
    /// Shipping against the document that reserved the stock takes its own reservation and is
    /// not refused for it.
    /// </summary>
    [Fact]
    public async Task A_document_may_ship_what_it_reserved()
    {
        await Receive(10m);

        await using (var context = NewContext())
        {
            await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 10m, "SO-0001");
        }

        await using (var context = NewContext())
        {
            var result = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -10m, EntryType: ItemLedgerEntryType.Sale)],
                Day,
                "SALES",
                "SO-0001",
                companyAllowsNegative: false);

            result.Succeeded.ShouldBeTrue("the order is taking the stock it was promised");
        }

        await using (var context = NewContext())
        {
            var held = await Reservations(context).ListAsync("SO-0001", outstandingOnly: false);

            held.Single().QuantityOutstanding.ShouldBe(0m, "shipping consumed it");
            held.Single().QuantityFulfilled.ShouldBe(10m);
        }
    }

    /// <summary>
    /// Shipping stock promised to another document is blocked, and the message says who has it.
    /// </summary>
    [Fact]
    public async Task Shipping_stock_promised_elsewhere_is_blocked()
    {
        await Receive(10m);

        await using (var context = NewContext())
        {
            await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 8m, "SO-0001");
        }

        await using (var context = NewContext())
        {
            var result = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -5m, EntryType: ItemLedgerEntryType.Sale)],
                Day,
                "SALES",
                "SO-0002",
                companyAllowsNegative: false);

            result.Failed.ShouldBeTrue("only two of the ten are free");
            result.Messages.ShouldContain(m => m.Code == InventoryMessages.TakingReservedStock);
        }
    }

    /// <summary>Taking only what is free is not blocked.</summary>
    [Fact]
    public async Task Taking_what_is_free_goes_through()
    {
        await Receive(10m);

        await using (var context = NewContext())
        {
            await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 8m, "SO-0001");
        }

        await using (var context = NewContext())
        {
            var result = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -2m, EntryType: ItemLedgerEntryType.Sale)],
                Day,
                "SALES",
                "SO-0002",
                companyAllowsNegative: false);

            result.Succeeded.ShouldBeTrue();
            result.Messages.ShouldNotContain(m => m.Code == InventoryMessages.TakingReservedStock);
        }
    }

    /// <summary>
    /// Somebody holding the stock override may take reserved goods anyway, and it is recorded.
    /// </summary>
    /// <remarks>
    /// The goods are on the shelf and a shop must be able to sell what it can see. What must not
    /// happen is that it is silent, and the order that was promised them finds out at the loading
    /// bay.
    /// </remarks>
    [Fact]
    public async Task The_override_lets_reserved_stock_go_and_records_it()
    {
        await Receive(10m);

        await using (var context = NewContext())
        {
            await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 8m, "SO-0001");
        }

        await using (var context = NewContext())
        {
            var result = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -5m, EntryType: ItemLedgerEntryType.Sale)],
                Day,
                "SALES",
                "SO-0002",
                companyAllowsNegative: false,
                heldOverridePermissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Inventory.Stock.Override",
                },
                overrideReason: "Customer waiting at the counter");

            result.Succeeded.ShouldBeTrue();

            var warning = result.Messages.Single(m => m.Code == InventoryMessages.TakingReservedStock);
            warning.WasOverridden.ShouldBeTrue();
            warning.IsFailure.ShouldBeFalse();
        }

        await using (var context = NewContext())
        {
            context.AuditLog
                .Count(a => a.OverriddenMessageCode == "INV.RESERVE.TAKING_RESERVED")
                .ShouldBe(1, "the message said it would be recorded");
        }
    }

    /// <summary>Releasing puts the stock back on the market and keeps the record.</summary>
    [Fact]
    public async Task Releasing_frees_the_stock_and_keeps_the_row()
    {
        await Receive(10m);

        await using (var context = NewContext())
        {
            await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 8m, "SO-0001");
        }

        await using (var context = NewContext())
        {
            (await Reservations(context).ReleaseAsync("SO-0001", reason: "Order abandoned"))
                .ShouldBe(8m);
        }

        await using (var context = NewContext())
        {
            var rows = await Reservations(context).AvailabilityAsync();

            rows.Single().QuantityAvailable.ShouldBe(10m);

            var held = await Reservations(context).ListAsync("SO-0001", outstandingOnly: false);

            held.Single().ReleaseReason.ShouldBe(
                "Order abandoned",
                "a reservation that vanished could not say why the order went unfilled");
        }
    }

    /// <summary>Receiving goods cannot take anybody's reservation.</summary>
    [Fact]
    public async Task Receiving_is_never_blocked_by_a_reservation()
    {
        await Receive(10m);

        await using (var context = NewContext())
        {
            await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 10m, "SO-0001");
        }

        await using (var context = NewContext())
        {
            var result = await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", 5m, 10m, ItemLedgerEntryType.Purchase)],
                Day,
                "PURCH",
                "PO-0001",
                companyAllowsNegative: false);

            result.Succeeded.ShouldBeTrue();
        }
    }

    /// <summary>Shipping more than was reserved takes the rest off free stock.</summary>
    [Fact]
    public async Task Shipping_more_than_was_reserved_empties_the_reservation()
    {
        await Receive(10m);

        await using (var context = NewContext())
        {
            await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 4m, "SO-0001");
        }

        await using (var context = NewContext())
        {
            (await Posting(context).PostAsync(
                [new StockMovementRequest("ITEM-1001", "SHOP", -9m, EntryType: ItemLedgerEntryType.Sale)],
                Day,
                "SALES",
                "SO-0001",
                companyAllowsNegative: false)).Succeeded.ShouldBeTrue();
        }

        await using (var context = NewContext())
        {
            var held = await Reservations(context).ListAsync("SO-0001", outstandingOnly: false);

            held.Single().QuantityOutstanding.ShouldBe(0m);
        }
    }

    /// <summary>A reservation with no document is refused.</summary>
    [Fact]
    public async Task A_reservation_must_say_what_it_is_for()
    {
        await Receive(10m);

        await using var context = NewContext();

        var result = await Reservations(context).ReserveAsync("ITEM-1001", "SHOP", 1m, string.Empty);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == InventoryMessages.ReservationNeedsADocument);
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private async Task Receive(decimal quantity)
    {
        await using var context = NewContext();

        (await Posting(context).PostAsync(
            [new StockMovementRequest("ITEM-1001", "SHOP", quantity, 10m, ItemLedgerEntryType.Purchase)],
            Day.AddDays(-1),
            "PURCH",
            null,
            companyAllowsNegative: false)).Succeeded.ShouldBeTrue();
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
        _opened.Add(context);
        return context;
    }

    private StockReservationService Reservations(AsapDbContext context)
        => new(
            context,
            new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All]),
            _tenancy,
            NullLogger<StockReservationService>.Instance);

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
            // Nothing to deliver; these tests are about what is free to promise.
        }
    }
}
