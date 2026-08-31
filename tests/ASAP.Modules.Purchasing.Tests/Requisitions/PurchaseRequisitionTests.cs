using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Purchasing.Approvals;
using ASAP.Modules.Purchasing.Orders;
using ASAP.Modules.Purchasing.Requisitions;
using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Messaging;
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

namespace ASAP.Modules.Purchasing.Tests.Requisitions;

/// <summary>
/// Asking for something to be bought, and what stops the asking becoming the buying.
/// </summary>
/// <remarks>
/// Three rules carry this. Nobody signs for their own request. One requisition can become several
/// orders and no line may be ordered twice. And an approval measured against an estimate is not
/// authority to buy at any price -- the order goes through its own approval on real figures.
/// </remarks>
public sealed class PurchaseRequisitionTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000f1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000fa");
    private static readonly Guid Asker = Guid.Parse("dddddddd-0000-0000-0000-00000000000a");
    private static readonly Guid Signer = Guid.Parse("dddddddd-0000-0000-0000-00000000000b");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc));
    private readonly StubSetup _setup = new();
    private readonly StubNumbers _requisitionNumbers = new("REQ");
    private readonly StubNumbers _orderNumbers = new("PO");
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up a company with one vendor, one item and one location.</summary>
    public PurchaseRequisitionTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-requisitions-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext(Asker);

        context.Set<Location>().Add(new Location
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "HO",
            Name = "Head office",
        });

        context.Set<Item>().Add(new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "PAPER",
            Description = "Printer paper",
            BaseUnitOfMeasure = "BOX",
            UnitCost = 20m,
        });

        context.Set<Vendor>().AddRange(
            new Vendor { TenantId = Tenant, CompanyId = Company, No = "V-1", Name = "Office supplies" },
            new Vendor { TenantId = Tenant, CompanyId = Company, No = "V-2", Name = "Hardware" });

        context.SaveChanges();
    }

    /// <summary>A requisition below the threshold needs no signature at all.</summary>
    [Fact]
    public async Task A_small_requisition_goes_straight_through()
    {
        using var context = NewContext(Asker);

        var raised = await Requisitions(context, Asker).CreateAsync(
            [new PurchaseRequisitionLineRequest(PurchaseLineType.Item, "PAPER", 2m, 20m)],
            locationCode: "HO");

        raised.Succeeded.ShouldBeTrue();

        var submitted = await Requisitions(context, Asker).SubmitAsync(raised.Value.No);

        submitted.Value.Status.ShouldBe(
            PurchaseRequisitionStatus.Approved,
            "forty is under the threshold, and the threshold is a number somebody chose");
    }

    /// <summary>
    /// Nobody signs for their own request.
    /// </summary>
    /// <remarks>
    /// The rule the whole exercise turns on. An approval you can give yourself is a checkbox.
    /// </remarks>
    [Fact]
    public async Task Nobody_approves_their_own_requisition()
    {
        using var context = NewContext(Asker);

        var raised = await Big(context);

        (await Requisitions(context, Asker).SubmitAsync(raised.No)).Value.Status
            .ShouldBe(PurchaseRequisitionStatus.Submitted);

        var approved = await Requisitions(context, Asker).ApproveAsync(raised.No);

        approved.Failed.ShouldBeTrue();
        approved.Messages.ShouldContain(m => m.Code == PurchasingMessages.CannotApproveYourOwnRequisition);
    }

    /// <summary>Somebody else may sign, and the amount is frozen when they do.</summary>
    [Fact]
    public async Task Somebody_else_signs_and_the_amount_is_frozen()
    {
        using var context = NewContext(Asker);

        var raised = await Big(context);
        await Requisitions(context, Asker).SubmitAsync(raised.No);

        var approved = await Requisitions(context, Signer).ApproveAsync(raised.No);

        approved.Succeeded.ShouldBeTrue();
        approved.Value.Status.ShouldBe(PurchaseRequisitionStatus.Approved);
        approved.Value.ApprovedByUserId.ShouldBe(Signer);
        approved.Value.ApprovedAmount.ShouldBe(4000m);
    }

    /// <summary>Orders cannot be raised from a requisition nobody has signed.</summary>
    [Fact]
    public async Task An_unapproved_requisition_raises_no_orders()
    {
        using var context = NewContext(Asker);

        var raised = await Big(context);
        await Requisitions(context, Asker).SubmitAsync(raised.No);

        var order = await Requisitions(context, Signer)
            .OrderAsync(raised.No, "V-1", [new RequisitionOrderLineRequest(10, 1m, 20m)]);

        order.Failed.ShouldBeTrue();
        order.Messages.ShouldContain(m => m.Code == PurchasingMessages.RequisitionNotApproved);
    }

    /// <summary>
    /// One requisition becomes several orders, one per vendor, and each line counts what has gone.
    /// </summary>
    [Fact]
    public async Task One_requisition_becomes_several_orders()
    {
        using var context = NewContext(Asker);

        var raised = await Approved(context);

        var first = await Requisitions(context, Signer)
            .OrderAsync(raised.No, "V-1", [new RequisitionOrderLineRequest(10, 40m, 22m)]);

        first.Succeeded.ShouldBeTrue();

        var second = await Requisitions(context, Signer)
            .OrderAsync(raised.No, "V-2", [new RequisitionOrderLineRequest(10, 60m, 19m)]);

        second.Succeeded.ShouldBeTrue();
        second.Value.No.ShouldNotBe(first.Value.No);

        var stored = await Requisitions(context, Signer).LoadAsync(raised.No);

        stored.ShouldNotBeNull();
        stored.Lines.Single().QuantityOrdered.ShouldBe(100m);
        stored.Status.ShouldBe(PurchaseRequisitionStatus.Ordered);
    }

    /// <summary>
    /// A line cannot be ordered twice. The counter is checked rather than trusted.
    /// </summary>
    [Fact]
    public async Task A_line_cannot_be_ordered_beyond_what_was_asked_for()
    {
        using var context = NewContext(Asker);

        var raised = await Approved(context);

        await Requisitions(context, Signer)
            .OrderAsync(raised.No, "V-1", [new RequisitionOrderLineRequest(10, 80m, 20m)]);

        var again = await Requisitions(context, Signer)
            .OrderAsync(raised.No, "V-2", [new RequisitionOrderLineRequest(10, 40m, 20m)]);

        again.Failed.ShouldBeTrue();
        again.Messages.ShouldContain(m => m.Code == PurchasingMessages.OrderExceedsRequisition);
    }

    /// <summary>
    /// The order carries the real price, not the estimate the requisition was approved at.
    /// </summary>
    /// <remarks>
    /// An approved requisition is authority to buy the thing, not authority to buy it at any
    /// price. The estimate is the one figure on the document nobody has checked.
    /// </remarks>
    [Fact]
    public async Task The_order_carries_the_real_price_rather_than_the_estimate()
    {
        using var context = NewContext(Asker);

        var raised = await Approved(context);

        raised.Lines.Single().EstimatedUnitCost.ShouldBe(40m);

        var order = await Requisitions(context, Signer)
            .OrderAsync(raised.No, "V-1", [new RequisitionOrderLineRequest(10, 100m, 55m)]);

        order.Succeeded.ShouldBeTrue();
        order.Value.Lines.Single().DirectUnitCost.ShouldBe(
            55m,
            "the vendor charges what the vendor charges");
    }

    /// <summary>A requisition with orders behind it cannot be abandoned.</summary>
    [Fact]
    public async Task A_requisition_that_became_an_order_cannot_be_cancelled()
    {
        using var context = NewContext(Asker);

        var raised = await Approved(context);

        await Requisitions(context, Signer)
            .OrderAsync(raised.No, "V-1", [new RequisitionOrderLineRequest(10, 10m, 20m)]);

        var cancelled = await Requisitions(context, Signer).CancelAsync(raised.No, "Changed our minds");

        cancelled.Failed.ShouldBeTrue();
        cancelled.Messages.ShouldContain(m => m.Code == PurchasingMessages.RequisitionAlreadyOrdered);
    }

    /// <summary>A requisition asking for nothing is refused.</summary>
    [Fact]
    public async Task A_requisition_with_no_lines_is_refused()
    {
        using var context = NewContext(Asker);

        var raised = await Requisitions(context, Asker).CreateAsync([]);

        raised.Failed.ShouldBeTrue();
        raised.Messages.ShouldContain(m => m.Code == PurchasingMessages.RequisitionHasNoLines);
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    /// <summary>A hundred boxes at forty, which is four thousand and needs signing for.</summary>
    private async Task<PurchaseRequisition> Big(AsapDbContext context)
    {
        var raised = await Requisitions(context, Asker).CreateAsync(
            [new PurchaseRequisitionLineRequest(PurchaseLineType.Item, "PAPER", 100m, 40m)],
            locationCode: "HO",
            justification: "The Jeddah shop has run out");

        raised.Succeeded.ShouldBeTrue();

        return raised.Value;
    }

    private async Task<PurchaseRequisition> Approved(AsapDbContext context)
    {
        var raised = await Big(context);

        await Requisitions(context, Asker).SubmitAsync(raised.No);
        (await Requisitions(context, Signer).ApproveAsync(raised.No)).Succeeded.ShouldBeTrue();

        return raised;
    }

    private AsapDbContext NewContext(Guid userId)
    {
        var context = new AsapDbContext(
            _options,
            _tenancy,
            new StubUser(userId),
            _clock,
            [new InventorySchema(), new PurchasingSchema(), new Finance.FinanceSchema()]);

        _opened.Add(context);

        return context;
    }

    private PurchaseRequisitionService Requisitions(AsapDbContext context, Guid userId)
    {
        var catalog = new MessageCatalog(
            [.. PlatformMessages.All, .. InventoryMessages.All, .. PurchasingMessages.All]);

        var who = new StubUser(userId);

        var orders = new PurchaseOrderService(
            context,
            catalog,
            new OverrideAuditor(context, _tenancy, who, _clock),
            new StubNumbersAdapter(_orderNumbers),
            _setup,
            _tenancy,
            who,
            _clock,
            new PurchaseApprovalService(context, catalog, _setup, who, _tenancy, _clock,
                NullLogger<PurchaseApprovalService>.Instance),
            NullLogger<PurchaseOrderService>.Instance);

        return new PurchaseRequisitionService(
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
    }

    private sealed class StubNumbersAdapter(StubNumbers inner) : INumberSeriesService
    {
        public Task<Result<string>> NextAsync(string s, DateOnly d, CancellationToken c = default)
            => inner.NextAsync(s, d, c);

        public Task<Result<string>> PeekAsync(string s, DateOnly d, CancellationToken c = default)
            => inner.PeekAsync(s, d, c);

        public Task<Result> ValidateManualAsync(string s, string n, DateOnly d, CancellationToken c = default)
            => inner.ValidateManualAsync(s, n, d, c);
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

    private sealed class StubUser(Guid userId) : IUserContext
    {
        public Guid? UserId => userId;

        public string? UserName => userId == Asker ? "salim" : "mona";

        public string? DisplayName => userId == Asker ? "Salim" : "Mona";

        public string? Culture => "en";

        public bool IsSuperUser => true;

        public IReadOnlySet<string> Permissions => new HashSet<string>();

        public bool Has(string permissionKey) => true;

        public Guid RequireUserId() => userId;
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
