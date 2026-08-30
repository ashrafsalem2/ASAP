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

namespace ASAP.Modules.Inventory.Tests.Items;

/// <summary>
/// Covers the one thing that makes variants dangerous: they partition the cost layers.
/// </summary>
/// <remarks>
/// A bin says where the same goods are standing and never touches a cost. A variant is a different
/// physical thing that may have cost a different amount. So a query that forgets the variant does
/// not fail -- it costs a blue shirt against a red receipt, and the only symptom is a margin
/// quietly wrong on both. Everything here exists to make that visible if it ever happens.
/// </remarks>
public sealed class VariantCostingTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000c1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c1");
    private static readonly DateOnly Day = new(2026, 8, 1);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    public VariantCostingTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-variants-{Guid.CreateVersion7()}")
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

        var shirt = new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "SHIRT",
            Description = "Cotton shirt",
            BaseUnitOfMeasure = "PCS",
            CostingMethod = CostingMethod.Fifo,
            UnitCost = 30.00m,
            LastDirectCost = 30.00m,
            AllowNegativeInventory = true,
            HasVariants = true,
        };

        var mug = new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "MUG",
            Description = "Plain mug",
            BaseUnitOfMeasure = "PCS",
            CostingMethod = CostingMethod.Fifo,
            UnitCost = 5.00m,
            LastDirectCost = 5.00m,
            AllowNegativeInventory = true,
        };

        context.Set<Item>().AddRange(shirt, mug);
        context.SaveChanges();

        context.Set<ItemVariant>().AddRange(
            Variant(shirt.Id, "BLUE-M", "Blue, medium", 10),
            Variant(shirt.Id, "RED-L", "Red, large", 20),
            Variant(shirt.Id, "GONE", "Discontinued", 90, blocked: true));

        context.SaveChanges();

        static ItemVariant Variant(Guid itemId, string code, string description, int order, bool blocked = false)
            => new()
            {
                TenantId = Tenant,
                CompanyId = Company,
                ItemId = itemId,
                Code = code,
                Description = description,
                SortOrder = order,
                IsBlocked = blocked,
            };
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

    private async Task<Result<StockPostingReceipt>> PostAsync(params StockMovementRequest[] movements)
    {
        await using var context = NewContext();

        return await Posting(context).PostAsync(
            movements,
            Day,
            "TEST",
            documentNo: null,
            companyAllowsNegative: true);
    }

    private static StockMovementRequest Receive(string variant, decimal quantity, decimal cost)
        => new("SHIRT", "SHOP", quantity, cost, ItemLedgerEntryType.Purchase, VariantCode: variant);

    private static StockMovementRequest Sell(string variant, decimal quantity)
        => new("SHIRT", "SHOP", -quantity, 0m, ItemLedgerEntryType.Sale, VariantCode: variant);

    [Fact]
    public async Task Blue_is_never_costed_out_of_a_red_receipt()
    {
        // Red is on the shelf; blue has never been received. Selling blue must not reach into the
        // red layer -- it must go short and be valued at an estimate that settles later.
        (await PostAsync(Receive("RED-L", 10m, 50.00m))).Succeeded.ShouldBeTrue();

        var sale = await PostAsync(Sell("BLUE-M", 2m));

        sale.Succeeded.ShouldBeTrue();

        // The whole cost is an estimate, which is only true if no layer was drawn on. Had the red
        // receipt been consumed, this would have been a settled cost and nothing would have said so.
        sale.Value.EstimatedCostAmount.ShouldBe(sale.Value.CostAmount);

        await using var context = NewContext();

        // And red is untouched. This is the assertion that would fail the day a layer query
        // forgets the variant.
        var red = await context.Set<ItemLedgerEntry>()
            .FirstAsync(e => e.VariantCode == "RED-L" && e.Quantity > 0);

        red.RemainingQuantity.ShouldBe(10m);

        var blue = await context.Set<ItemLedgerEntry>()
            .FirstAsync(e => e.VariantCode == "BLUE-M");

        blue.WentNegative.ShouldBeTrue();
    }

    [Fact]
    public async Task Each_variant_keeps_its_own_cost()
    {
        await PostAsync(Receive("BLUE-M", 10m, 40.00m));
        await PostAsync(Receive("RED-L", 10m, 60.00m));

        var blue = await PostAsync(Sell("BLUE-M", 5m));
        var red = await PostAsync(Sell("RED-L", 5m));

        blue.Value.CostAmount.ShouldBe(-200.00m);
        red.Value.CostAmount.ShouldBe(-300.00m);
        blue.Value.EstimatedCostAmount.ShouldBe(0m);
        red.Value.EstimatedCostAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task Stock_on_hand_is_per_variant()
    {
        await PostAsync(Receive("BLUE-M", 10m, 40.00m));
        await PostAsync(Receive("RED-L", 4m, 60.00m));
        await PostAsync(Sell("BLUE-M", 3m));

        await using var context = NewContext();

        var rows = await new ItemVariantService(context, Catalog()).StockAsync("SHIRT");

        rows.Count.ShouldBe(2);
        rows.First(r => r.VariantCode == "BLUE-M").Quantity.ShouldBe(7m);
        rows.First(r => r.VariantCode == "RED-L").Quantity.ShouldBe(4m);
    }

    [Fact]
    public async Task A_movement_that_does_not_say_which_variant_is_refused()
    {
        // Guessing is the one thing that must not happen. A default would open a phantom stock
        // line that no shelf corresponds to.
        var result = await PostAsync(
            new StockMovementRequest("SHIRT", "SHOP", 5m, 40.00m, ItemLedgerEntryType.Purchase));

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.VARIANT.REQUIRED");
    }

    [Fact]
    public async Task A_variant_on_an_item_that_has_none_is_refused()
    {
        var result = await PostAsync(
            new StockMovementRequest("MUG", "SHOP", 5m, 5.00m, ItemLedgerEntryType.Purchase, VariantCode: "BLUE-M"));

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.VARIANT.NOT_USED");
    }

    [Fact]
    public async Task A_variant_the_item_has_not_got_is_refused()
    {
        var result = await PostAsync(Receive("GREEN-S", 5m, 40.00m));

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.VARIANT.NOT_FOUND");
    }

    [Fact]
    public async Task A_withdrawn_variant_takes_no_more_stock_in()
    {
        var result = await PostAsync(Receive("GONE", 5m, 40.00m));

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.VARIANT.BLOCKED");
    }

    [Fact]
    public async Task A_withdrawn_variant_still_lets_what_is_there_leave()
    {
        // Blocked before it was withdrawn, and still on the shelf. Refusing the sale would strand
        // goods that physically exist.
        await using (var context = NewContext())
        {
            var gone = await context.Set<ItemVariant>().FirstAsync(v => v.Code == "GONE");
            gone.IsBlocked = false;
            await context.SaveChangesAsync();
        }

        await PostAsync(Receive("GONE", 5m, 40.00m));

        await using (var context = NewContext())
        {
            var gone = await context.Set<ItemVariant>().FirstAsync(v => v.Code == "GONE");
            gone.IsBlocked = true;
            await context.SaveChangesAsync();
        }

        var sale = await PostAsync(Sell("GONE", 5m));

        sale.Succeeded.ShouldBeTrue();
        sale.Value.CostAmount.ShouldBe(-200.00m);
    }

    [Fact]
    public async Task An_item_without_variants_behaves_exactly_as_before()
    {
        // The safety property behind making variants opt-in: nothing about an ordinary item
        // changes, and every entry it writes carries no variant at all.
        var received = await PostAsync(
            new StockMovementRequest("MUG", "SHOP", 20m, 5.00m, ItemLedgerEntryType.Purchase));

        var sold = await PostAsync(
            new StockMovementRequest("MUG", "SHOP", -8m, 0m, ItemLedgerEntryType.Sale));

        received.Succeeded.ShouldBeTrue();
        sold.Value.CostAmount.ShouldBe(-40.00m);

        await using var context = NewContext();

        var entries = await context.Set<ItemLedgerEntry>().Where(e => e.ItemNo == "MUG").ToListAsync();

        entries.ShouldAllBe(e => e.VariantId == null);
    }

    [Fact]
    public async Task Variants_cannot_be_turned_off_while_stock_stands_under_them()
    {
        await PostAsync(Receive("BLUE-M", 6m, 40.00m));

        await using var context = NewContext();

        var result = await new ItemVariantService(context, Catalog()).SetHasVariantsAsync("SHIRT", false);

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.VARIANT.STILL_HOLDS_STOCK");
    }

    [Fact]
    public async Task Variants_can_be_turned_off_once_the_stock_is_gone()
    {
        await PostAsync(Receive("BLUE-M", 6m, 40.00m));
        await PostAsync(Sell("BLUE-M", 6m));

        await using var context = NewContext();

        var result = await new ItemVariantService(context, Catalog()).SetHasVariantsAsync("SHIRT", false);

        result.Succeeded.ShouldBeTrue();
        result.Value.HasVariants.ShouldBeFalse();
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
