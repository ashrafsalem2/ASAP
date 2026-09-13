using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Modules.Inventory.Transfers;
using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Events;
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

namespace ASAP.Modules.Inventory.Tests.Transfers;

/// <summary>
/// A branch asking for stock, and whoever holds it deciding.
/// </summary>
/// <remarks>
/// Two rules carry this, the same two a purchase requisition carries. Nobody answers their own
/// request. And an answer may give less than was asked for, never more.
/// </remarks>
public sealed class TransferRequestTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000c7");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c7");
    private static readonly Guid BranchManager = Guid.Parse("dddddddd-0000-0000-0000-0000000000c1");
    private static readonly Guid Warehouse = Guid.Parse("dddddddd-0000-0000-0000-0000000000c2");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingSeries _numbers = new();
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up a warehouse, a branch and one item.</summary>
    public TransferRequestTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-transfer-requests-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext(BranchManager);

        foreach (var (code, name, transit) in new[]
                 {
                     ("WH", "Warehouse", false),
                     ("JED", "Jeddah branch", false),
                     ("TRANSIT", "In transit", true),
                 })
        {
            context.Set<Location>().Add(new Location
            {
                TenantId = Tenant,
                CompanyId = Company,
                Code = code,
                Name = name,
                IsSellable = !transit,
                IsInTransit = transit,
            });
        }

        context.Set<Item>().Add(new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "WATER",
            Description = "Bottled water, case",
            BaseUnitOfMeasure = "CASE",
            CostingMethod = CostingMethod.Fifo,
            UnitCost = 10m,
        });

        context.SaveChanges();
    }

    /// <summary>A request is raised as a draft and commits nothing.</summary>
    [Fact]
    public async Task A_request_commits_nothing()
    {
        using var context = NewContext(BranchManager);

        var raised = await Requests(context, BranchManager).CreateAsync(
            "WH", "JED", [new TransferRequestLineRequest("WATER", 20m)], reason: "Weekend promotion");

        raised.Succeeded.ShouldBeTrue();
        raised.Value.Status.ShouldBe(TransferRequestStatus.Draft);
        raised.Value.Lines.Single().QuantityRequested.ShouldBe(20m);

        (await context.Set<TransferOrder>().CountAsync()).ShouldBe(0, "nothing is sent until it is agreed");
    }

    /// <summary>
    /// Nobody answers their own request.
    /// </summary>
    /// <remarks>
    /// The place holding the goods knows what else is promised out of them. The branch asking does
    /// not, and an approval it could give itself would be a checkbox.
    /// </remarks>
    [Fact]
    public async Task Nobody_answers_their_own_request()
    {
        using var context = NewContext(BranchManager);

        var no = await SubmittedAsync(context);

        var approved = await Requests(context, BranchManager).ApproveAsync(no);

        approved.Failed.ShouldBeTrue();
        approved.Messages.ShouldContain(m => m.Code == InventoryMessages.CannotApproveYourOwnTransferRequest);
    }

    /// <summary>Somebody else agrees, and everything asked for is agreed by default.</summary>
    [Fact]
    public async Task Somebody_else_agrees_to_it()
    {
        using var context = NewContext(BranchManager);

        var no = await SubmittedAsync(context);

        var approved = await Requests(context, Warehouse).ApproveAsync(no);

        approved.Succeeded.ShouldBeTrue();
        approved.Value.Status.ShouldBe(TransferRequestStatus.Approved);
        approved.Value.Lines.Single().QuantityApproved.ShouldBe(20m);
        approved.Value.ApprovedByUserName.ShouldBe("Warehouse");
    }

    /// <summary>Less than was asked for may be agreed.</summary>
    [Fact]
    public async Task Less_than_was_asked_may_be_agreed()
    {
        using var context = NewContext(BranchManager);

        var no = await SubmittedAsync(context);

        var approved = await Requests(context, Warehouse)
            .ApproveAsync(no, [new TransferApprovalLine(10, 12m, "Only twelve spare")]);

        approved.Succeeded.ShouldBeTrue();
        approved.Value.Lines.Single().QuantityApproved.ShouldBe(12m);
        approved.Value.Lines.Single().Note.ShouldBe("Only twelve spare");
    }

    /// <summary>
    /// More than was asked for is refused.
    /// </summary>
    /// <remarks>
    /// A warehouse clearing its shelves into a branch that cannot sell them has moved a problem
    /// rather than solved one.
    /// </remarks>
    [Fact]
    public async Task More_than_was_asked_is_refused()
    {
        using var context = NewContext(BranchManager);

        var no = await SubmittedAsync(context);

        var approved = await Requests(context, Warehouse)
            .ApproveAsync(no, [new TransferApprovalLine(10, 50m)]);

        approved.Failed.ShouldBeTrue();
        approved.Messages.ShouldContain(m => m.Code == InventoryMessages.ApprovedMoreThanRequested);
    }

    /// <summary>What was agreed becomes a transfer from where it was asked of, to where it was wanted.</summary>
    [Fact]
    public async Task What_was_agreed_becomes_a_transfer()
    {
        using var context = NewContext(BranchManager);

        var no = await SubmittedAsync(context);

        await Requests(context, Warehouse).ApproveAsync(no, [new TransferApprovalLine(10, 12m)]);

        var transfer = await Requests(context, Warehouse).TransferAsync(no);

        transfer.Succeeded.ShouldBeTrue();
        transfer.Value.FromLocationCode.ShouldBe("WH");
        transfer.Value.ToLocationCode.ShouldBe("JED");
        transfer.Value.Lines.Single().Quantity.ShouldBe(12m, "what was agreed, not what was asked");

        var request = await Requests(context, Warehouse).LoadAsync(no);

        request!.Status.ShouldBe(TransferRequestStatus.Fulfilled);
        request.Lines.Single().QuantityTransferred.ShouldBe(12m);
    }

    /// <summary>
    /// A request cannot be sent twice.
    /// </summary>
    /// <remarks>
    /// A line sent twice is stock nobody asked for arriving at a branch that cannot send it back
    /// without paperwork.
    /// </remarks>
    [Fact]
    public async Task A_request_cannot_be_sent_twice()
    {
        using var context = NewContext(BranchManager);

        var no = await SubmittedAsync(context);

        await Requests(context, Warehouse).ApproveAsync(no);
        (await Requests(context, Warehouse).TransferAsync(no)).Succeeded.ShouldBeTrue();

        var again = await Requests(context, Warehouse).TransferAsync(no);

        again.Failed.ShouldBeTrue();
        (await context.Set<TransferOrder>().CountAsync()).ShouldBe(1);
    }

    /// <summary>A request that has not been agreed cannot be sent.</summary>
    [Fact]
    public async Task An_unanswered_request_cannot_be_sent()
    {
        using var context = NewContext(BranchManager);

        var no = await SubmittedAsync(context);

        (await Requests(context, Warehouse).TransferAsync(no)).Failed.ShouldBeTrue();
    }

    /// <summary>A request turned down says why, and cannot then be sent.</summary>
    [Fact]
    public async Task A_request_turned_down_says_why()
    {
        using var context = NewContext(BranchManager);

        var no = await SubmittedAsync(context);

        var rejected = await Requests(context, Warehouse).RejectAsync(no, "Reserved for Riyadh opening");

        rejected.Succeeded.ShouldBeTrue();
        rejected.Value.Status.ShouldBe(TransferRequestStatus.Rejected);
        rejected.Value.RejectionReason.ShouldBe("Reserved for Riyadh opening");

        (await Requests(context, Warehouse).TransferAsync(no)).Failed.ShouldBeTrue();
    }

    /// <summary>A place cannot ask itself for stock.</summary>
    [Fact]
    public async Task A_place_cannot_ask_itself()
    {
        using var context = NewContext(BranchManager);

        var raised = await Requests(context, BranchManager).CreateAsync(
            "JED", "JED", [new TransferRequestLineRequest("WATER", 20m)]);

        raised.Failed.ShouldBeTrue();
        raised.Messages.ShouldContain(m => m.Code == InventoryMessages.TransferToSameLocation);
    }

    /// <summary>A line that asks for nothing is refused.</summary>
    [Fact]
    public async Task A_line_asking_for_nothing_is_refused()
    {
        using var context = NewContext(BranchManager);

        var raised = await Requests(context, BranchManager).CreateAsync(
            "WH", "JED", [new TransferRequestLineRequest("WATER", 0m)]);

        raised.Failed.ShouldBeTrue();
        raised.Messages.ShouldContain(m => m.Code == InventoryMessages.TransferRequestQuantityZero);
    }

    /// <summary>A submitted request can no longer be changed by submitting it again.</summary>
    [Fact]
    public async Task A_submitted_request_is_no_longer_editable()
    {
        using var context = NewContext(BranchManager);

        var no = await SubmittedAsync(context);

        var again = await Requests(context, BranchManager).SubmitAsync(no);

        again.Failed.ShouldBeTrue();
        again.Messages.ShouldContain(m => m.Code == InventoryMessages.TransferRequestNotEditable);
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private async Task<string> SubmittedAsync(AsapDbContext context)
    {
        var raised = await Requests(context, BranchManager).CreateAsync(
            "WH", "JED", [new TransferRequestLineRequest("WATER", 20m)], reason: "Weekend promotion");

        raised.Succeeded.ShouldBeTrue();

        (await Requests(context, BranchManager).SubmitAsync(raised.Value.No)).Succeeded.ShouldBeTrue();

        return raised.Value.No;
    }

    private AsapDbContext NewContext(Guid userId)
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(userId), _clock, [new InventorySchema()]);

        _opened.Add(context);

        return context;
    }

    private TransferRequestService Requests(AsapDbContext context, Guid userId)
    {
        var catalog = new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All]);
        var who = new StubUser(userId);

        var posting = new StockPostingService(
            context,
            new StockAvailability(catalog),
            new LocationBranchLookup(context),
            new NullPublisher(),
            catalog,
            _tenancy,
            new OverrideAuditor(context, _tenancy, who, _clock),
            _clock,
            _allocator,
            NullLogger<StockPostingService>.Instance);

        var transfers = new TransferService(
            context,
            posting,
            new BinPicker(context, catalog),
            catalog,
            _numbers,
            _tenancy,
            _clock,
            NullLogger<TransferService>.Instance);

        return new TransferRequestService(
            context,
            transfers,
            catalog,
            _numbers,
            new StubSetup(),
            _tenancy,
            who,
            _clock,
            NullLogger<TransferRequestService>.Instance);
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

    private sealed class StubUser(Guid userId) : IUserContext
    {
        public Guid? UserId => userId;

        public string? UserName => userId == Warehouse ? "warehouse" : "branch";

        public string? DisplayName => userId == Warehouse ? "Warehouse" : "Branch manager";

        public string? Culture => "en";

        public bool IsSuperUser => true;

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();

        public bool Has(string permissionKey) => true;

        public Guid RequireUserId() => userId;
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

    private sealed class CountingSeries : INumberSeriesService
    {
        private int _last;

        public Task<Result<string>> NextAsync(string s, DateOnly d, CancellationToken c = default)
            => Task.FromResult(Result<string>.Success($"{s}-{++_last:00000}"));

        public Task<Result<string>> PeekAsync(string s, DateOnly d, CancellationToken c = default)
            => Task.FromResult(Result<string>.Success($"{s}-{_last + 1:00000}"));

        public Task<Result> ValidateManualAsync(string s, string n, DateOnly d, CancellationToken c = default)
            => Task.FromResult(Result.Success());
    }

    private sealed class StubSetup : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((TValue)(object)"TRANSFER-REQ");

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
            // Nothing to deliver.
        }
    }
}
