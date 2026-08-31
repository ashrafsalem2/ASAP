using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Purchasing.Approvals;
using ASAP.Modules.Purchasing.Orders;
using ASAP.Modules.Purchasing.Quotations;
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

namespace ASAP.Modules.Purchasing.Tests.Quotations;

/// <summary>
/// Asking several vendors the same question, and choosing between the answers.
/// </summary>
/// <remarks>
/// The rule the whole thing exists for is the last one. Awarding to a dearer quote is legitimate
/// and often right; awarding to one silently is the thing nobody can explain a year later.
/// </remarks>
public sealed class PurchaseQuotationTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000e1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000ea");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc));
    private readonly StubSetup _setup = new();
    private readonly StubNumbers _requestNumbers = new("RFQ");
    private readonly StubNumbers _orderNumbers = new("PO");
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up a company with three vendors and one item.</summary>
    public PurchaseQuotationTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-quotations-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

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
            new Vendor { TenantId = Tenant, CompanyId = Company, No = "CHEAP", Name = "Slow and cheap" },
            new Vendor { TenantId = Tenant, CompanyId = Company, No = "QUICK", Name = "Fast and dear" },
            new Vendor { TenantId = Tenant, CompanyId = Company, No = "SILENT", Name = "Never answers" });

        context.SaveChanges();
    }

    /// <summary>A request nobody was asked cannot go out.</summary>
    [Fact]
    public async Task A_request_sent_to_nobody_is_refused()
    {
        using var context = NewContext();

        var raised = await Quotations(context).CreateAsync(
            [new QuotationLineRequest(PurchaseLineType.Item, "PAPER", 100m)]);

        var sent = await Quotations(context).SendAsync(raised.Value.No);

        sent.Failed.ShouldBeTrue();
        sent.Messages.ShouldContain(m => m.Code == PurchasingMessages.QuotationHasNoVendors);
    }

    /// <summary>A quote from somebody nobody asked is refused.</summary>
    [Fact]
    public async Task A_vendor_who_was_not_asked_cannot_quote()
    {
        using var context = NewContext();

        var request = await Asked(context);

        var response = await Quotations(context).RespondAsync(
            request.No,
            "SILENT-OUTSIDER",
            [new QuotationResponseLine(10, 5m)]);

        response.Failed.ShouldBeTrue();
        response.Messages.ShouldContain(m => m.Code == PurchasingMessages.VendorWasNotAsked);
    }

    /// <summary>
    /// The comparison puts the answers side by side, and flags cheapest and fastest separately.
    /// </summary>
    /// <remarks>
    /// They are often different vendors. A comparison showing only money would make the choice
    /// look obvious when it is not.
    /// </remarks>
    [Fact]
    public async Task The_comparison_flags_cheapest_and_fastest_separately()
    {
        using var context = NewContext();

        var request = await Quoted(context);

        var rows = await Quotations(context).CompareAsync(request.No);
        var line = rows.Single();

        line.Quotes.Count.ShouldBe(2, "the silent vendor said nothing");

        var cheap = line.Quotes.Single(q => q.VendorNo == "CHEAP");
        var quick = line.Quotes.Single(q => q.VendorNo == "QUICK");

        cheap.IsCheapest.ShouldBeTrue();
        cheap.IsFastest.ShouldBeFalse();

        quick.IsCheapest.ShouldBeFalse();
        quick.IsFastest.ShouldBeTrue();
    }

    /// <summary>Awarding the cheapest quote needs no explanation.</summary>
    [Fact]
    public async Task The_cheapest_quote_needs_no_reason()
    {
        using var context = NewContext();

        var request = await Quoted(context);

        var awarded = await Quotations(context).AwardAsync(
            request.No,
            [new QuotationAward(10, "CHEAP")]);

        awarded.Succeeded.ShouldBeTrue();
        awarded.Value.Lines.Single().AwardedVendorNo.ShouldBe("CHEAP");
        awarded.Value.Lines.Single().AwardedUnitCost.ShouldBe(9m);
        awarded.Value.Status.ShouldBe(QuotationRequestStatus.Awarded);
    }

    /// <summary>
    /// Awarding a dearer quote with no reason is refused, and the refusal names the price gap.
    /// </summary>
    /// <remarks>
    /// The rule the module exists for. It is not a judgment about which vendor is right -- it is
    /// a requirement that somebody say what they were thinking.
    /// </remarks>
    [Fact]
    public async Task A_dearer_quote_without_a_reason_is_refused()
    {
        using var context = NewContext();

        var request = await Quoted(context);

        var awarded = await Quotations(context).AwardAsync(
            request.No,
            [new QuotationAward(10, "QUICK")]);

        awarded.Failed.ShouldBeTrue();
        awarded.Messages.ShouldContain(m => m.Code == PurchasingMessages.DearerQuoteNeedsAReason);
    }

    /// <summary>With a reason, the dearer quote wins and the reason is kept.</summary>
    [Fact]
    public async Task A_dearer_quote_with_a_reason_wins_and_the_reason_is_kept()
    {
        using var context = NewContext();

        var request = await Quoted(context);

        var awarded = await Quotations(context).AwardAsync(
            request.No,
            [new QuotationAward(10, "QUICK", "Two days rather than six weeks; the shelf is empty")]);

        awarded.Succeeded.ShouldBeTrue();

        var line = awarded.Value.Lines.Single();

        line.AwardedVendorNo.ShouldBe("QUICK");
        line.AwardReason.ShouldBe("Two days rather than six weeks; the shelf is empty");
    }

    /// <summary>A line cannot be awarded to a vendor who never quoted for it.</summary>
    [Fact]
    public async Task A_line_cannot_be_awarded_to_somebody_who_never_quoted()
    {
        using var context = NewContext();

        var request = await Quoted(context);

        var awarded = await Quotations(context).AwardAsync(
            request.No,
            [new QuotationAward(10, "SILENT", "They are cheaper in principle")]);

        awarded.Failed.ShouldBeTrue();
        awarded.Messages.ShouldContain(m => m.Code == PurchasingMessages.VendorDidNotQuote);
    }

    /// <summary>The order carries the quoted price rather than anything typed again.</summary>
    [Fact]
    public async Task The_order_carries_the_quoted_price()
    {
        using var context = NewContext();

        var request = await Quoted(context);

        await Quotations(context).AwardAsync(request.No, [new QuotationAward(10, "CHEAP")]);

        var order = await Quotations(context).OrderAsync(request.No, "CHEAP");

        order.Succeeded.ShouldBeTrue();
        order.Value.Lines.Single().DirectUnitCost.ShouldBe(
            9m,
            "the quote is the real price, and retyping it would only let the two disagree");
    }

    /// <summary>An award that became an order cannot be moved to another vendor.</summary>
    [Fact]
    public async Task An_award_that_became_an_order_cannot_be_moved()
    {
        using var context = NewContext();

        var request = await Quoted(context);

        await Quotations(context).AwardAsync(request.No, [new QuotationAward(10, "CHEAP")]);
        await Quotations(context).OrderAsync(request.No, "CHEAP");

        var moved = await Quotations(context).AwardAsync(
            request.No,
            [new QuotationAward(10, "QUICK", "Changed our minds")]);

        moved.Failed.ShouldBeTrue();
        moved.Messages.ShouldContain(m => m.Code == PurchasingMessages.QuotationAlreadyOrdered);
    }

    /// <summary>Ordering before awarding gives nothing to order.</summary>
    [Fact]
    public async Task Nothing_can_be_ordered_before_it_is_awarded()
    {
        using var context = NewContext();

        var request = await Quoted(context);

        var order = await Quotations(context).OrderAsync(request.No, "CHEAP");

        order.Failed.ShouldBeTrue();
        order.Messages.ShouldContain(m => m.Code == PurchasingMessages.NothingAwardedToOrder);
    }

    /// <summary>A vendor who declined is told apart from one who simply never answered.</summary>
    [Fact]
    public async Task A_decline_is_different_from_silence()
    {
        using var context = NewContext();

        var request = await Asked(context);

        await Quotations(context).DeclineAsync(request.No, "SILENT", "Cannot supply that quantity");

        var stored = await Quotations(context).LoadAsync(request.No);

        var declined = stored!.Invitations.Single(i => i.VendorNo == "SILENT");
        var quiet = stored.Invitations.Single(i => i.VendorNo == "QUICK");

        declined.HasAnswered.ShouldBeTrue();
        declined.DeclinedReason.ShouldBe("Cannot supply that quantity");

        quiet.HasAnswered.ShouldBeFalse("nobody has heard from them at all");
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    /// <summary>A request out with all three vendors.</summary>
    private async Task<PurchaseQuotationRequest> Asked(AsapDbContext context)
    {
        var raised = await Quotations(context).CreateAsync(
            [new QuotationLineRequest(PurchaseLineType.Item, "PAPER", 100m, "Printer paper")],
            locationCode: "HO");

        raised.Succeeded.ShouldBeTrue();

        await Quotations(context).InviteAsync(raised.Value.No, ["CHEAP", "QUICK", "SILENT"]);
        await Quotations(context).SendAsync(raised.Value.No);

        return raised.Value;
    }

    /// <summary>The same, with two of them having answered. Cheap is slow; quick is dear.</summary>
    private async Task<PurchaseQuotationRequest> Quoted(AsapDbContext context)
    {
        var request = await Asked(context);

        await Quotations(context).RespondAsync(
            request.No, "CHEAP", [new QuotationResponseLine(10, 9m, 42)]);

        await Quotations(context).RespondAsync(
            request.No, "QUICK", [new QuotationResponseLine(10, 12m, 2)]);

        return request;
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

    private PurchaseQuotationService Quotations(AsapDbContext context)
    {
        var catalog = new MessageCatalog(
            [.. PlatformMessages.All, .. InventoryMessages.All, .. PurchasingMessages.All]);

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

        return new PurchaseQuotationService(
            context,
            orders,
            catalog,
            _requestNumbers,
            _setup,
            _tenancy,
            who,
            _clock,
            NullLogger<PurchaseQuotationService>.Instance);
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
        public Guid? UserId { get; } = Guid.Parse("dddddddd-0000-0000-0000-0000000000ee");

        public string? UserName => "buyer";

        public string? DisplayName => "The buyer";

        public string? Culture => "en";

        public bool IsSuperUser => true;

        public IReadOnlySet<string> Permissions => new HashSet<string>();

        public bool Has(string permissionKey) => true;

        public Guid RequireUserId() => UserId ?? Guid.Empty;
    }

    private sealed class StubClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private sealed class StubSetup : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(string key, CancellationToken cancellationToken = default)
        {
            object value = key switch
            {
                "Purchasing.Approval.Threshold" => 1_000_000m,
                "Purchasing.Quotations.NumberSeries" => "PURCH-RFQ",
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
