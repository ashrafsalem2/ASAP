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
/// Covers what goods are worth when a customer brings them back.
/// </summary>
/// <remarks>
/// A return is not a purchase. Nothing was bought and nothing new was paid, so restoring the stock
/// at what the item costs today lets a customer changing their mind move the inventory account:
/// goods sold at ten and returned when the item costs thirty come back at thirty, and twenty
/// appears out of nowhere while the original sale's cost of sales stays where it was.
/// </remarks>
public sealed class ReturnCostingTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000e9");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000e9");
    private static readonly DateOnly Day = new(2026, 8, 1);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    public ReturnCostingTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-returns-{Guid.CreateVersion7()}")
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
            No = "WIDGET",
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

    private async Task<Result<StockPostingReceipt>> PostAsync(
        StockMovementRequest movement,
        string? documentNo = null)
    {
        await using var context = NewContext();

        return await Posting(context).PostAsync(
            [movement],
            Day,
            "TEST",
            documentNo,
            companyAllowsNegative: true);
    }

    /// <summary>Twenty bought at ten, all sold on SALE-1, then twenty more bought at thirty.</summary>
    private async Task TheCostHasMovedSinceAsync()
    {
        await PostAsync(new StockMovementRequest("WIDGET", "SHOP", 20m, 10.00m, ItemLedgerEntryType.Purchase));
        await PostAsync(new StockMovementRequest("WIDGET", "SHOP", -20m, 0m, ItemLedgerEntryType.Sale), "SALE-1");
        await PostAsync(new StockMovementRequest("WIDGET", "SHOP", 20m, 30.00m, ItemLedgerEntryType.Purchase));
    }

    [Fact]
    public async Task Goods_come_back_at_what_they_cost_when_they_left()
    {
        await TheCostHasMovedSinceAsync();

        // Two units that cost the company ten each. Restoring them at thirty would invent forty of
        // inventory value out of a customer changing their mind.
        var returned = await PostAsync(
            new StockMovementRequest(
                "WIDGET",
                "SHOP",
                2m,
                0m,
                ItemLedgerEntryType.SalesReturn,
                AppliesToDocumentNo: "SALE-1"));

        returned.Succeeded.ShouldBeTrue();
        returned.Value.CostAmount.ShouldBe(20.00m);
    }

    /// <summary>
    /// Goods going back to a vendor leave at what they cost when they arrived.
    /// </summary>
    /// <remarks>
    /// The same rule as a customer return, read from the other end. A vendor return has to price
    /// itself from the entries that came <em>in</em> on the named order, and the query that finds
    /// them looked only for entries that went out -- which found nothing at all, and fell back to
    /// what the item costs today without a word. That fallback is the exact failure the whole
    /// mechanism exists to prevent: it lets a change of supplier price move the inventory account.
    /// </remarks>
    [Fact]
    public async Task Goods_go_back_to_a_vendor_at_what_they_cost_when_they_arrived()
    {
        // Twenty arrived at ten on PO-1, then the price went up and twenty more arrived at thirty.
        await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 20m, 10.00m, ItemLedgerEntryType.Purchase),
            "PO-1");

        await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 20m, 30.00m, ItemLedgerEntryType.Purchase),
            "PO-2");

        // Two of the first delivery go back. They cost ten each, whatever the second cost.
        var returned = await PostAsync(
            new StockMovementRequest(
                "WIDGET",
                "SHOP",
                -2m,
                0m,
                ItemLedgerEntryType.PurchaseReturn,
                AppliesToDocumentNo: "PO-1"));

        returned.Succeeded.ShouldBeTrue();
        returned.Value.CostAmount.ShouldBe(
            -20.00m,
            "priced from the delivery they came in on, not from the later one");
    }

    /// <summary>
    /// A vendor return relieves the delivery it names, even when an older and cheaper one is
    /// still open.
    /// </summary>
    /// <remarks>
    /// The case that costs money. Ten arrived at ten on the first order and ten at thirty on the
    /// second; two of the second go back. Relieving the oldest layer instead would take twenty off
    /// inventory while the vendor is credited sixty, leaving forty stuck in the goods-received
    /// accrual for ever -- and the expensive goods still on the shelf, quietly overstating the
    /// valuation. Nothing else in the system would ever mention it.
    /// </remarks>
    [Fact]
    public async Task A_vendor_return_relieves_the_delivery_it_names()
    {
        await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 10m, 10.00m, ItemLedgerEntryType.Purchase),
            "PO-CHEAP");

        await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 10m, 30.00m, ItemLedgerEntryType.Purchase),
            "PO-DEAR");

        var returned = await PostAsync(
            new StockMovementRequest(
                "WIDGET",
                "SHOP",
                -2m,
                0m,
                ItemLedgerEntryType.PurchaseReturn,
                AppliesToDocumentNo: "PO-DEAR"));

        returned.Succeeded.ShouldBeTrue();
        returned.Value.CostAmount.ShouldBe(
            -60.00m,
            "they came in at thirty, whatever is still open on the cheaper delivery");
    }

    /// <summary>
    /// Where the named delivery has already gone, the ordinary layers stand.
    /// </summary>
    [Fact]
    public async Task A_vendor_return_falls_back_when_its_delivery_is_used_up()
    {
        await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 10m, 10.00m, ItemLedgerEntryType.Purchase),
            "PO-CHEAP");

        await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 2m, 30.00m, ItemLedgerEntryType.Purchase),
            "PO-DEAR");

        // The dear delivery sells out.
        await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", -12m, 0m, ItemLedgerEntryType.Sale),
            "SALE-9");

        await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 5m, 10.00m, ItemLedgerEntryType.Purchase),
            "PO-LATER");

        var returned = await PostAsync(
            new StockMovementRequest(
                "WIDGET",
                "SHOP",
                -1m,
                0m,
                ItemLedgerEntryType.PurchaseReturn,
                AppliesToDocumentNo: "PO-DEAR"));

        returned.Succeeded.ShouldBeTrue();
        returned.Value.CostAmount.ShouldBe(-10.00m, "nothing of that delivery is left to relieve");
    }

    /// <summary>
    /// A vendor return that names no order falls back to what the item costs today, and does not
    /// pretend otherwise.
    /// </summary>
    [Fact]
    public async Task A_vendor_return_that_names_no_order_is_valued_at_todays_cost()
    {
        await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 20m, 10.00m, ItemLedgerEntryType.Purchase),
            "PO-1");

        var returned = await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", -2m, 0m, ItemLedgerEntryType.PurchaseReturn));

        returned.Succeeded.ShouldBeTrue();
        returned.Value.CostAmount.ShouldBe(-20.00m, "there is only one layer, so today's cost is ten");
    }

    [Fact]
    public async Task A_return_that_names_no_sale_is_valued_at_todays_cost_and_says_so()
    {
        await TheCostHasMovedSinceAsync();

        // Sometimes nobody knows which sale it came off -- a customer with no receipt. The
        // approximation is unavoidable; passing over it in silence is not.
        var returned = await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 2m, 0m, ItemLedgerEntryType.SalesReturn));

        returned.Succeeded.ShouldBeTrue();
        returned.Value.CostAmount.ShouldBe(60.00m);
        returned.Messages.ShouldContain(m => m.Code.Value == "INV.RETURN.COST_ASSUMED");
    }

    [Fact]
    public async Task An_ordinary_receipt_says_nothing_about_returns()
    {
        await TheCostHasMovedSinceAsync();

        var received = await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 5m, 12.00m, ItemLedgerEntryType.Purchase));

        received.Messages.ShouldNotContain(m => m.Code.Value == "INV.RETURN.COST_ASSUMED");
    }

    [Fact]
    public async Task A_named_cost_still_wins()
    {
        await TheCostHasMovedSinceAsync();

        // Whoever posts the movement may know better than either figure -- a return at an agreed
        // settlement, say. An explicit cost is not overridden and raises nothing.
        var returned = await PostAsync(
            new StockMovementRequest(
                "WIDGET",
                "SHOP",
                2m,
                7.50m,
                ItemLedgerEntryType.SalesReturn,
                AppliesToDocumentNo: "SALE-1"));

        returned.Value.CostAmount.ShouldBe(15.00m);
        returned.Messages.ShouldNotContain(m => m.Code.Value == "INV.RETURN.COST_ASSUMED");
    }

    [Fact]
    public async Task A_sale_that_drew_on_two_prices_returns_at_what_it_averaged()
    {
        // Ten at ten and ten at twenty, then fifteen sold on one document: that sale cost 10x10
        // plus 5x20, so 200 over 15 units. A return of three comes back at that average, because
        // nothing records which of the fifteen the customer had.
        await PostAsync(new StockMovementRequest("WIDGET", "SHOP", 10m, 10.00m, ItemLedgerEntryType.Purchase));
        await PostAsync(new StockMovementRequest("WIDGET", "SHOP", 10m, 20.00m, ItemLedgerEntryType.Purchase));
        await PostAsync(new StockMovementRequest("WIDGET", "SHOP", -15m, 0m, ItemLedgerEntryType.Sale), "SALE-2");

        var returned = await PostAsync(
            new StockMovementRequest(
                "WIDGET",
                "SHOP",
                3m,
                0m,
                ItemLedgerEntryType.SalesReturn,
                AppliesToDocumentNo: "SALE-2"));

        // 200 / 15 = 13.33333 each, three of them.
        returned.Value.CostAmount.ShouldBe(40.00m);
    }

    [Fact]
    public async Task A_document_that_never_sold_this_item_falls_back_and_says_so()
    {
        await TheCostHasMovedSinceAsync();

        var returned = await PostAsync(
            new StockMovementRequest(
                "WIDGET",
                "SHOP",
                1m,
                0m,
                ItemLedgerEntryType.SalesReturn,
                AppliesToDocumentNo: "SALE-NOTHING"));

        returned.Value.CostAmount.ShouldBe(30.00m);
        returned.Messages.ShouldContain(m => m.Code.Value == "INV.RETURN.COST_ASSUMED");
    }

    [Fact]
    public async Task A_return_does_not_change_what_the_item_costs()
    {
        await TheCostHasMovedSinceAsync();

        // Restoring goods at last year's cost must not drag the item's current cost back there
        // with them. It would value every later shortfall at a price nobody has paid in months --
        // and the second return below would come back at the first one's figure rather than at
        // today's, which is how one honest fix quietly becomes a different wrong answer.
        await PostAsync(
            new StockMovementRequest(
                "WIDGET",
                "SHOP",
                2m,
                0m,
                ItemLedgerEntryType.SalesReturn,
                AppliesToDocumentNo: "SALE-1"));

        var blind = await PostAsync(
            new StockMovementRequest("WIDGET", "SHOP", 2m, 0m, ItemLedgerEntryType.SalesReturn));

        // Still thirty each, not the ten the first return came back at.
        blind.Value.CostAmount.ShouldBe(60.00m);
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
            // Nothing to deliver; these tests are about what the return is worth.
        }
    }
}
