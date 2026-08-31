using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Modules.Inventory.Reporting;
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

namespace ASAP.Modules.Inventory.Tests.Reporting;

/// <summary>
/// What the stock is worth, how old it is, and how fast it moves.
/// </summary>
/// <remarks>
/// The claim worth testing is the first one. A valuation is only useful if it ties to the
/// inventory account, and it ties because it is built from the same rows rather than because
/// somebody checked. The others are about what a report says when it has no answer, which is the
/// part reports usually get wrong.
/// </remarks>
public sealed class InventoryReportTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000d1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000da");
    private static readonly DateOnly Arrived = new(2026, 3, 1);
    private static readonly DateOnly Later = new(2026, 7, 1);
    private static readonly DateOnly Today = new(2026, 8, 20);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up one shop and two items, one of which never moves.</summary>
    public InventoryReportTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-inventory-reports-{Guid.CreateVersion7()}")
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

        context.Set<Item>().AddRange(
            Item("MOVER", "Sells steadily"),
            Item("STUCK", "Has not moved since it arrived"));

        context.SaveChanges();

        static Item Item(string no, string description) => new()
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = no,
            Description = description,
            BaseUnitOfMeasure = "PCS",
            CostingMethod = CostingMethod.Fifo,
            Kind = ItemKind.Inventory,
            UnitCost = 10m,
            LastDirectCost = 10m,
        };
    }

    /// <summary>
    /// The valuation is the same arithmetic that posts to the inventory account.
    /// </summary>
    /// <remarks>
    /// Not a coincidence to be checked but the reason the report is built this way. A valuation
    /// worked out any other way is a second opinion, and a second opinion about the inventory
    /// account is what nobody wants at a period end.
    /// </remarks>
    [Fact]
    public async Task The_valuation_ties_to_the_sum_of_the_value_entries()
    {
        await Receive("MOVER", 10m, 10m, Arrived);
        await Receive("MOVER", 10m, 12.50m, Later);
        await Sell("MOVER", 6m, Today);

        await using var context = NewContext();

        var rows = await Reports(context).ValuationAsync(Today);
        var mover = rows.Single(r => r.ItemNo == "MOVER");

        var ledger = await context.Set<ValueEntry>()
            .Where(v => v.ItemNo == "MOVER" && v.PostingDate <= Today)
            .SumAsync(v => v.CostAmount);

        mover.Value.ShouldBe(ledger, "the report and the account are built from the same rows");
        mover.Quantity.ShouldBe(14m);
        mover.UnitCost.ShouldBe(Math.Round(ledger / 14m, 5, MidpointRounding.AwayFromZero));
    }

    /// <summary>A valuation as at a past date does not see what came later.</summary>
    [Fact]
    public async Task A_valuation_as_at_a_date_ignores_what_came_after_it()
    {
        await Receive("MOVER", 10m, 10m, Arrived);
        await Receive("MOVER", 10m, 12.50m, Later);

        await using var context = NewContext();

        var atMarch = await Reports(context).ValuationAsync(new DateOnly(2026, 3, 31));

        atMarch.Single(r => r.ItemNo == "MOVER").Quantity.ShouldBe(10m);
        atMarch.Single(r => r.ItemNo == "MOVER").Value.ShouldBe(100m);
    }

    /// <summary>
    /// A sale made from stock that had not arrived leaves value that is still a guess, and the
    /// valuation says how much of it.
    /// </summary>
    [Fact]
    public async Task The_valuation_says_how_much_of_itself_is_a_guess()
    {
        await Sell("STUCK", 4m, Today, allowNegative: true);

        await using var context = NewContext();

        var row = (await Reports(context).ValuationAsync(Today)).Single(r => r.ItemNo == "STUCK");

        row.Quantity.ShouldBe(-4m);
        row.EstimatedValue.ShouldBe(40m, "nothing on hand backed any of it");
    }

    /// <summary>Stock is as old as the layer it is still sitting in.</summary>
    [Fact]
    public async Task Ageing_reads_the_age_off_the_cost_layers()
    {
        await Receive("MOVER", 10m, 10m, Arrived);
        await Receive("MOVER", 10m, 12.50m, Later);

        await using var context = NewContext();

        var row = (await Reports(context).AgeingAsync(Today)).Single(r => r.ItemNo == "MOVER");

        row.Quantity.ShouldBe(20m);
        row.Value.ShouldBe(225m);

        // 1 March to 20 August is 172 days; 1 July to 20 August is 50.
        row.OldestDays.ShouldBe(172);

        row.Buckets.Single(b => b.Label == "31-60").Quantity.ShouldBe(10m);
        row.Buckets.Single(b => b.Label == "181+").Quantity.ShouldBe(0m);
        row.Buckets.Single(b => b.Label == "91-180").Quantity.ShouldBe(10m);
    }

    /// <summary>
    /// Selling the oldest layer moves the stock out of the oldest band, because the layer it
    /// came from is the thing that emptied.
    /// </summary>
    [Fact]
    public async Task Selling_the_oldest_stock_empties_the_oldest_band()
    {
        await Receive("MOVER", 10m, 10m, Arrived);
        await Receive("MOVER", 10m, 12.50m, Later);
        await Sell("MOVER", 10m, Today);

        await using var context = NewContext();

        var row = (await Reports(context).AgeingAsync(Today)).Single(r => r.ItemNo == "MOVER");

        row.Quantity.ShouldBe(10m);
        row.OldestDays.ShouldBe(50, "the March layer is gone");
        row.Buckets.Single(b => b.Label == "91-180").Quantity.ShouldBe(0m);
    }

    /// <summary>
    /// An item that never moved appears, because it is the reason the report is run.
    /// </summary>
    [Fact]
    public async Task Velocity_lists_what_did_not_move()
    {
        await Receive("MOVER", 10m, 10m, Arrived);
        await Receive("STUCK", 10m, 10m, Arrived);
        await Sell("MOVER", 6m, Today);

        await using var context = NewContext();

        var rows = await Reports(context).VelocityAsync(Arrived, Today);

        rows.Count.ShouldBe(2);
        rows[0].ItemNo.ShouldBe("STUCK", "slowest first, because that is what somebody is looking for");
        rows[0].QuantitySold.ShouldBe(0m);
        rows.Single(r => r.ItemNo == "MOVER").QuantitySold.ShouldBe(6m);
    }

    /// <summary>
    /// Turns and days of cover are left empty where they have no answer, rather than filled
    /// with a nought that means something else.
    /// </summary>
    [Fact]
    public async Task A_figure_with_no_answer_is_left_empty()
    {
        await Receive("STUCK", 10m, 10m, Arrived);

        await using var context = NewContext();

        var rows = await Reports(context).VelocityAsync(Arrived, Today);

        var stuck = rows.Single(r => r.ItemNo == "STUCK");
        var never = rows.Single(r => r.ItemNo == "MOVER");

        stuck.Turns.ShouldBe(0m, "there is stock to divide by, and none of it turned");
        stuck.DaysOfCover.ShouldBeNull("stock that never moves would last for ever");

        never.Turns.ShouldBeNull("there is no stock to divide by at all");
        never.DaysOfCover.ShouldBeNull();
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private async Task Receive(string itemNo, decimal quantity, decimal unitCost, DateOnly on)
    {
        await using var context = NewContext();

        (await Posting(context).PostAsync(
            [new StockMovementRequest(itemNo, "SHOP", quantity, unitCost, ItemLedgerEntryType.Purchase)],
            on,
            "PURCH",
            null,
            companyAllowsNegative: false)).Succeeded.ShouldBeTrue();
    }

    private async Task Sell(string itemNo, decimal quantity, DateOnly on, bool allowNegative = false)
    {
        await using var context = NewContext();

        (await Posting(context).PostAsync(
            [new StockMovementRequest(itemNo, "SHOP", -quantity, EntryType: ItemLedgerEntryType.Sale)],
            on,
            "SALES",
            null,
            allowNegative)).Succeeded.ShouldBeTrue();
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
        _opened.Add(context);
        return context;
    }

    private static InventoryReportService Reports(AsapDbContext context) => new(context);

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
            // Nothing to deliver; these tests are about what the reports say.
        }
    }
}
