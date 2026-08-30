using ASAP.Modules.Purchasing;
using ASAP.Modules.Purchasing.Approvals;
using ASAP.Modules.Purchasing.Orders;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Purchasing.Tests.Approvals;

/// <summary>
/// Covers who may sign for a purchase order, and the rule that makes signing mean anything.
/// </summary>
/// <remarks>
/// An approval you can give yourself is not a control, it is a checkbox. The whole point of the
/// step is that a second person looked, so the test that matters most here is the one where a
/// buyer with a generous limit tries to approve their own order.
/// </remarks>
public sealed class ApprovalRulesTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000a1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid Buyer = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid Manager = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Director = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    public ApprovalRulesTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-approvals-{Guid.CreateVersion7()}")
            .Options;

        _tenancy.TenantId = Tenant;
        _tenancy.CompanyId = Company;

        using var context = NewContext();

        context.Set<PurchaseApprovalLimit>().AddRange(
            Limit(Buyer, "buyer", "Buyer", 2_000m),
            Limit(Manager, "manager", "Manager", 50_000m),
            Limit(Director, "director", "Director", 500_000m));

        context.SaveChanges();

        static PurchaseApprovalLimit Limit(Guid id, string userName, string display, decimal maximum)
            => new()
            {
                TenantId = Tenant,
                CompanyId = Company,
                UserId = id,
                UserName = userName,
                DisplayName = display,
                MaximumAmount = maximum,
            };
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(
            _options,
            _tenancy,
            new StubUser(Buyer, "buyer"),
            _clock,
            [new PurchasingSchema()]);

        _opened.Add(context);
        return context;
    }

    private PurchaseApprovalService Service(AsapDbContext context, Guid actingAs, string userName, decimal threshold = 10_000m)
        => new(
            context,
            new MessageCatalog([.. PlatformMessages.All, .. PurchasingMessages.All]),
            new StubSetup(threshold),
            new StubUser(actingAs, userName),
            _tenancy,
            _clock,
            NullLogger<PurchaseApprovalService>.Instance);

    private static PurchaseOrder Order(decimal amount, Guid raisedBy)
        => new()
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "PO-0001",
            VendorNo = "V-001",
            VendorName = "A vendor",
            Status = PurchaseOrderStatus.PendingApproval,
            RaisedByUserId = raisedBy,
            Lines =
            [
                new PurchaseOrderLine
                {
                    TenantId = Tenant,
                    CompanyId = Company,
                    LineNo = 1,
                    Description = "Something",
                    Quantity = 1m,
                    DirectUnitCost = amount,
                },
            ],
        };

    [Fact]
    public async Task Nobody_may_approve_an_order_they_raised()
    {
        // The rule the whole feature turns on. The buyer's limit is 2,000 and this order is 1,500,
        // so the amount is not what stops them -- being the person who raised it is.
        await using var context = NewContext();

        var order = Order(1_500m, raisedBy: Buyer);
        context.Set<PurchaseOrder>().Add(order);
        await context.SaveChangesAsync();

        var result = await Service(context, Buyer, "buyer").ApproveAsync(order);

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("PUR.APPROVAL.OWN_ORDER");
        order.Status.ShouldBe(PurchaseOrderStatus.PendingApproval);
    }

    [Fact]
    public async Task Somebody_else_within_their_limit_may_approve_it()
    {
        await using var context = NewContext();

        var order = Order(1_500m, raisedBy: Buyer);
        context.Set<PurchaseOrder>().Add(order);
        await context.SaveChangesAsync();

        var result = await Service(context, Manager, "manager").ApproveAsync(order);

        result.Succeeded.ShouldBeTrue();
        order.Status.ShouldBe(PurchaseOrderStatus.Released);
        order.ApprovedByUserId.ShouldBe(Manager);
        order.ApprovedAmount.ShouldBe(1_500m);
        order.ApprovedAtUtc.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task An_amount_above_the_approver_limit_is_refused_and_says_who_can()
    {
        await using var context = NewContext();

        var order = Order(80_000m, raisedBy: Buyer);
        context.Set<PurchaseOrder>().Add(order);
        await context.SaveChangesAsync();

        var result = await Service(context, Manager, "manager").ApproveAsync(order);

        result.Failed.ShouldBeTrue();

        var refusal = result.Messages.Single(m => m.IsFailure);
        refusal.Code.Value.ShouldBe("PUR.APPROVAL.LIMIT_TOO_LOW");

        // A refusal that does not name a next step is a dead end for whoever holds the order.
        refusal.Detail.ShouldNotBeNull().ShouldContain("Director");
    }

    [Fact]
    public async Task Somebody_with_no_limit_at_all_approves_nothing()
    {
        // Unknown must not mean unlimited, or the answer to "who can approve this" becomes
        // "whoever has not been set up yet".
        await using var context = NewContext();

        var stranger = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000f");

        var order = Order(100m, raisedBy: Buyer);
        context.Set<PurchaseOrder>().Add(order);
        await context.SaveChangesAsync();

        var result = await Service(context, stranger, "stranger").ApproveAsync(order);

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("PUR.APPROVAL.LIMIT_TOO_LOW");
    }

    [Fact]
    public async Task An_order_that_is_not_waiting_cannot_be_approved()
    {
        await using var context = NewContext();

        var order = Order(500m, raisedBy: Buyer);
        order.Status = PurchaseOrderStatus.Released;
        context.Set<PurchaseOrder>().Add(order);
        await context.SaveChangesAsync();

        var result = await Service(context, Manager, "manager").ApproveAsync(order);

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("PUR.APPROVAL.NOT_PENDING");
    }

    [Fact]
    public async Task A_rejection_needs_a_reason()
    {
        await using var context = NewContext();

        var order = Order(20_000m, raisedBy: Buyer);
        context.Set<PurchaseOrder>().Add(order);
        await context.SaveChangesAsync();

        var blank = await Service(context, Manager, "manager").RejectAsync(order, "   ");

        blank.Failed.ShouldBeTrue();
        blank.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("PUR.APPROVAL.REASON_REQUIRED");
        order.Status.ShouldBe(PurchaseOrderStatus.PendingApproval);
    }

    [Fact]
    public async Task A_rejection_keeps_its_reason_on_the_record()
    {
        await using var context = NewContext();

        var order = Order(20_000m, raisedBy: Buyer);
        context.Set<PurchaseOrder>().Add(order);
        await context.SaveChangesAsync();

        var result = await Service(context, Manager, "manager")
            .RejectAsync(order, "Three quotes not obtained");

        result.Succeeded.ShouldBeTrue();
        order.Status.ShouldBe(PurchaseOrderStatus.Rejected);
        order.RejectionReason.ShouldBe("Three quotes not obtained");
        order.ApprovedByUserId.ShouldBeNull();
    }

    [Theory]
    [InlineData(9_999, false)]
    [InlineData(10_000, false)]
    [InlineData(10_001, true)]
    public async Task The_threshold_decides_what_goes_out_unsigned(decimal amount, bool needsApproval)
    {
        // At the threshold, not above it: an order for exactly the limit is one somebody chose to
        // let through, and an off-by-one here is the difference between a rule and a surprise.
        await using var context = NewContext();

        var result = await Service(context, Manager, "manager").NeedsApprovalAsync(amount);

        result.ShouldBe(needsApproval);
    }

    [Fact]
    public async Task A_limit_below_nothing_is_refused()
    {
        await using var context = NewContext();

        var result = await Service(context, Director, "director")
            .SetLimitAsync(new ApprovalLimitRequest(Buyer, "buyer", "Buyer", -1m));

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("PUR.APPROVAL.LIMIT_NEGATIVE");
    }

    [Fact]
    public async Task Setting_a_limit_twice_changes_it_rather_than_adding_a_second()
    {
        await using var context = NewContext();

        var service = Service(context, Director, "director");

        await service.SetLimitAsync(new ApprovalLimitRequest(Buyer, "buyer", "Buyer", 3_000m));
        await service.SetLimitAsync(new ApprovalLimitRequest(Buyer, "buyer", "Buyer", 4_000m));

        var rows = await context.Set<PurchaseApprovalLimit>().Where(l => l.UserId == Buyer).ToListAsync();

        rows.Count.ShouldBe(1);
        rows[0].MaximumAmount.ShouldBe(4_000m);
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

    private sealed class StubUser(Guid id, string name) : IUserContext
    {
        public Guid? UserId => id;

        public string? UserName => name;

        public string? DisplayName => name;

        public string? Culture => "en";

        public bool IsSuperUser => true;

        public IReadOnlySet<string> Permissions => new HashSet<string>();

        public bool Has(string permissionKey) => true;

        public Guid RequireUserId() => id;
    }

    private sealed class StubClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private sealed class StubSetup(decimal threshold) : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((TValue)(object)threshold);

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
