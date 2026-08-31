using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Reservations;
using ASAP.Modules.Purchasing.Approvals;
using ASAP.Modules.Purchasing.Orders;
using ASAP.Modules.Purchasing.Replenishment;
using ASAP.Modules.Purchasing.Requisitions;
using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Purchasing.Tests.Replenishment;

/// <summary>
/// What the worksheet says needs buying, and what it correctly says does not.
/// </summary>
/// <remarks>
/// The arithmetic is covered where it lives, in the inventory tests. What is covered here is
/// whether the worksheet feeds it the right figures — above all whether it sees the goods that
/// are already on order. That is where a replenishment run goes wrong in a way nobody notices
/// until the stockroom is full.
/// </remarks>
public sealed class ReplenishmentTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000d1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000d1");
    private static readonly Guid Buyer = Guid.Parse("dddddddd-0000-0000-0000-0000000000d1");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc));
    private readonly StubSetup _setup = new();
    private readonly StubNumbers _requisitionNumbers = new("REQ");
    private readonly StubNumbers _orderNumbers = new("PO");
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up a shop with one item, one vendor and nothing on the shelf.</summary>
    public ReplenishmentTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-replenishment-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        context.Set<Location>().Add(new Location
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "SHOP",
            Name = "The shop",
        });

        context.Set<Item>().Add(new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "WATER",
            Description = "Bottled water, case",
            BaseUnitOfMeasure = "CASE",
            UnitCost = 10m,
            LastDirectCost = 10m,
        });

        context.Set<Vendor>().Add(new Vendor
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "V-1",
            Name = "Drinks wholesaler",
        });

        context.SaveChanges();
    }

    /// <summary>An item below its point, with nothing coming, is suggested.</summary>
    [Fact]
    public async Task An_item_below_its_point_is_suggested()
    {
        using var context = NewContext();

        await SavePolicyAsync(context, point: 10m, quantity: 40m);
        Receive(context, 2m);

        var lines = await Replenishment(context).SuggestAsync();

        lines.Count.ShouldBe(1);
        lines[0].ItemNo.ShouldBe("WATER");
        lines[0].QuantityOnHand.ShouldBe(2m);
        lines[0].Projected.ShouldBe(2m);
        lines[0].SuggestedQuantity.ShouldBe(40m);
    }

    /// <summary>
    /// Goods already on order are counted, so the same order is not placed twice.
    /// </summary>
    /// <remarks>
    /// The case the worksheet exists to get right. A shop with two on the shelf and forty on a
    /// lorry does not need forty more, and a worksheet that thinks it does asks again every
    /// morning until they arrive.
    /// </remarks>
    [Fact]
    public async Task Goods_already_on_order_are_not_ordered_again()
    {
        using var context = NewContext();

        await SavePolicyAsync(context, point: 10m, quantity: 40m);
        Receive(context, 2m);

        (await Replenishment(context).SuggestAsync())[0].SuggestedQuantity.ShouldBe(40m);

        await OrderAsync(context, 40m);

        var after = await Replenishment(context).SuggestAsync();

        after.ShouldBeEmpty("the forty are already bought and on their way");
    }

    /// <summary>A part-received order counts only what is still to come.</summary>
    [Fact]
    public async Task A_part_received_order_counts_only_what_is_still_coming()
    {
        using var context = NewContext();

        await SavePolicyAsync(context, point: 10m, quantity: 40m);
        Receive(context, 2m);

        var order = await OrderAsync(context, 40m);

        // Thirty arrive; ten are still on the vendor's van.
        var line = await context.Set<PurchaseOrderLine>()
            .FirstAsync(l => l.PurchaseOrderId == order.Id);

        line.QuantityReceived = 30m;
        Receive(context, 30m);
        await context.SaveChangesAsync();

        var lines = await Replenishment(context).SuggestAsync(includeSatisfied: true);

        lines[0].QuantityOnHand.ShouldBe(32m);
        lines[0].QuantityOnOrder.ShouldBe(10m, "only the ten still to come");
        lines[0].Projected.ShouldBe(42m);
        lines[0].SuggestedQuantity.ShouldBe(0m);
    }

    /// <summary>A cancelled order brings nothing, so it is ordered again.</summary>
    [Fact]
    public async Task A_cancelled_order_brings_nothing()
    {
        using var context = NewContext();

        await SavePolicyAsync(context, point: 10m, quantity: 40m);
        Receive(context, 2m);

        var order = await OrderAsync(context, 40m);

        (await Replenishment(context).SuggestAsync()).ShouldBeEmpty();

        order.Status = PurchaseOrderStatus.Cancelled;
        await context.SaveChangesAsync();

        var lines = await Replenishment(context).SuggestAsync();

        lines.Count.ShouldBe(1, "nothing is coming after all");
        lines[0].SuggestedQuantity.ShouldBe(40m);
    }

    /// <summary>Stock promised to somebody else does not count as stock the shop has.</summary>
    [Fact]
    public async Task Stock_promised_to_somebody_else_does_not_count()
    {
        using var context = NewContext();

        await SavePolicyAsync(context, point: 10m, quantity: 40m);
        Receive(context, 50m);

        (await Replenishment(context).SuggestAsync()).ShouldBeEmpty("fifty is well above ten");

        context.Set<StockReservation>().Add(new StockReservation
        {
            TenantId = Tenant,
            CompanyId = Company,
            ItemNo = "WATER",
            LocationCode = "SHOP",
            DocumentNo = "SO-0001",
            Quantity = 45m,
            QuantityOutstanding = 45m,
        });

        await context.SaveChangesAsync();

        var lines = await Replenishment(context).SuggestAsync();

        lines.Count.ShouldBe(1);
        lines[0].QuantityReserved.ShouldBe(45m);
        lines[0].Projected.ShouldBe(5m, "five are actually free");
    }

    /// <summary>
    /// The worksheet becomes a requisition, which goes through approval like any other.
    /// </summary>
    /// <remarks>
    /// Deliberately not an order. A run that placed its own orders would be a rule nobody wrote
    /// spending money nobody approved.
    /// </remarks>
    [Fact]
    public async Task The_worksheet_becomes_a_requisition_not_an_order()
    {
        using var context = NewContext();

        await SavePolicyAsync(context, point: 10m, quantity: 40m, vendorNo: "V-1");
        Receive(context, 2m);

        var lines = await Replenishment(context).SuggestAsync();

        var raised = await Replenishment(context).RequisitionAsync(lines, "SHOP");

        raised.Succeeded.ShouldBeTrue();
        raised.Value.Status.ShouldBe(PurchaseRequisitionStatus.Draft);
        raised.Value.Lines.Count.ShouldBe(1);
        raised.Value.Lines.Single().Quantity.ShouldBe(40m);
        raised.Value.Lines.Single().SuggestedVendorNo.ShouldBe("V-1");

        raised.Value.Justification.ShouldNotBeNull().ShouldContain(
            "reorder point",
            Case.Insensitive,
            "somebody approving this did not run the worksheet");
    }

    /// <summary>A policy that is switched off is not looked at.</summary>
    [Fact]
    public async Task A_policy_switched_off_is_not_looked_at()
    {
        using var context = NewContext();

        await SavePolicyAsync(context, point: 10m, quantity: 40m, isActive: false);
        Receive(context, 2m);

        (await Replenishment(context).SuggestAsync(includeSatisfied: true)).ShouldBeEmpty();
    }

    /// <summary>A policy whose maximum is below its point is refused, not saved and ignored.</summary>
    [Fact]
    public async Task A_maximum_below_the_point_is_refused()
    {
        using var context = NewContext();

        var result = await Policies(context).SaveAsync(new ReorderPolicyRequest(
            "WATER",
            "SHOP",
            ReorderKind.UpToMaximum,
            ReorderPoint: 100m,
            MaximumInventory: 50m));

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == InventoryMessages.ReorderMaximumBelowPoint);
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private async Task SavePolicyAsync(
        AsapDbContext context,
        decimal point,
        decimal quantity,
        string? vendorNo = null,
        bool isActive = true)
    {
        var saved = await Policies(context).SaveAsync(new ReorderPolicyRequest(
            "WATER",
            "SHOP",
            ReorderKind.FixedQuantity,
            ReorderPoint: point,
            ReorderQuantity: quantity,
            VendorNo: vendorNo,
            IsActive: isActive));

        saved.Succeeded.ShouldBeTrue();
    }

    /// <summary>Puts goods on the shelf without going through a posting run.</summary>
    private void Receive(AsapDbContext context, decimal quantity)
    {
        context.Set<ItemLedgerEntry>().Add(new ItemLedgerEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            ItemNo = "WATER",
            LocationCode = "SHOP",
            PostingDate = _clock.Today,
            EntryType = ItemLedgerEntryType.Purchase,
            Quantity = quantity,
            DocumentNo = "GRN-0001",
            SourceCode = "PURCHASE",
        });

        context.SaveChanges();
    }

    /// <summary>Buys some, so the worksheet has something on order to see.</summary>
    private async Task<PurchaseOrder> OrderAsync(AsapDbContext context, decimal quantity)
    {
        var order = new PurchaseOrder
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = $"PO-{Guid.CreateVersion7():N}"[..12],
            VendorNo = "V-1",
            VendorName = "Drinks wholesaler",
            OrderDate = _clock.Today,
            LocationCode = "SHOP",
            Status = PurchaseOrderStatus.Released,
        };

        context.Set<PurchaseOrder>().Add(order);
        await context.SaveChangesAsync();

        context.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine
        {
            TenantId = Tenant,
            CompanyId = Company,
            PurchaseOrderId = order.Id,
            LineNo = 10,
            Type = PurchaseLineType.Item,
            ItemNo = "WATER",
            Description = "Bottled water, case",
            LocationCode = "SHOP",
            Quantity = quantity,
            DirectUnitCost = 10m,
        });

        await context.SaveChangesAsync();

        return order;
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(
            _options,
            _tenancy,
            new StubUser(),
            _clock,
            [new InventorySchema(), new PurchasingSchema(), new Finance.FinanceSchema()]);

        _opened.Add(context);

        return context;
    }

    private static MessageCatalog Catalogue() => new(
        [.. PlatformMessages.All, .. InventoryMessages.All, .. PurchasingMessages.All]);

    private ReorderPolicyService Policies(AsapDbContext context)
        => new(context, Catalogue(), _tenancy);

    private ReplenishmentService Replenishment(AsapDbContext context)
    {
        var catalog = Catalogue();
        var who = new StubUser();

        var orders = new PurchaseOrderService(
            context,
            catalog,
            new OverrideAuditor(context, _tenancy, who, _clock),
            _orderNumbers,
            _setup,
            _tenancy,
            who,
            _clock,
            new PurchaseApprovalService(context, catalog, _setup, who, _tenancy, _clock,
                NullLogger<PurchaseApprovalService>.Instance),
            NullLogger<PurchaseOrderService>.Instance);

        var requisitions = new PurchaseRequisitionService(
            context,
            orders,
            new PurchaseApprovalService(context, catalog, _setup, who, _tenancy, _clock,
                NullLogger<PurchaseApprovalService>.Instance),
            catalog,
            _requisitionNumbers,
            _setup,
            _tenancy,
            who,
            _clock,
            NullLogger<PurchaseRequisitionService>.Instance);

        return new ReplenishmentService(context, requisitions, _clock);
    }

    private sealed class StubNumbers(string prefix) : INumberSeriesService
    {
        private int _next;

        public Task<Result<string>> NextAsync(string s, DateOnly d, CancellationToken c = default)
            => Task.FromResult(Result<string>.Success($"{prefix}-{++_next:0000}"));

        public Task<Result<string>> PeekAsync(string s, DateOnly d, CancellationToken c = default)
            => Task.FromResult(Result<string>.Success($"{prefix}-{_next + 1:0000}"));

        public Task<Result> ValidateManualAsync(string s, string n, DateOnly d, CancellationToken c = default)
            => Task.FromResult(Result.Success());
    }

    private sealed class StubTenant : ITenantContext
    {
        public Guid? TenantId { get; set; }

        public Guid? CompanyId { get; set; }

        public Guid? BranchId { get; set; }

        public bool IsCrossTenantOperation { get; set; }

        public Guid RequireTenantId() => TenantId ?? Guid.Empty;

        public Guid RequireCompanyId() => CompanyId ?? Guid.Empty;
    }

    private sealed class StubUser : IUserContext
    {
        public Guid? UserId => Buyer;

        public string? UserName => "buyer";

        public string? DisplayName => "Buyer";

        public string? Culture => "en";

        public bool IsSuperUser => true;

        public IReadOnlySet<string> Permissions => new HashSet<string>();

        public bool Has(string permissionKey) => true;

        public Guid RequireUserId() => Buyer;
    }

    private sealed class StubClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    /// <summary>Anything over a thousand needs signing for.</summary>
    private sealed class StubSetup : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(string key, CancellationToken cancellationToken = default)
        {
            object value = key switch
            {
                "Purchasing.Approval.Threshold" => 1000m,
                "Purchasing.Requisitions.NumberSeries" => "PURCH-REQ",
                _ => "PURCH-ORD",
            };

            return ValueTask.FromResult((TValue)value);
        }

        public ValueTask<TValue?> GetAtScopeAsync<TValue>(
            string key,
            SetupScope scope,
            Guid? scopeId = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TValue?>(default);

        public Task<Result> SetAsync(
            string key,
            string? value,
            SetupScope scope = SetupScope.Company,
            Guid? scopeId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }
}
