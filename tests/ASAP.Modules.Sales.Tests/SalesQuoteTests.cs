using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Sales.Orders;
using ASAP.Modules.Sales.Pricing;
using ASAP.Modules.Sales.Quotes;
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

namespace ASAP.Modules.Sales.Tests;

/// <summary>
/// What a quote promises, and what it refuses to do about it later.
/// </summary>
/// <remarks>
/// Two of these are the reason the module exists in this shape. A quote is a promise about price
/// and not about stock, so it must be possible to quote for goods that are not there. And the
/// prices go onto the order exactly as quoted, because the customer accepted the number in front
/// of them rather than the number a price list happens to hold three weeks later.
/// </remarks>
public sealed class SalesQuoteTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000051");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000005a");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc));
    private readonly StubSetup _setup = new();

    // One allocator each, held for the life of the test. A fresh one per call would hand two
    // quotes the same number, which SQL Server would refuse and the in-memory provider would not.
    private readonly StubNumbers _quoteNumbers = new("QT");
    private readonly StubNumbers _orderNumbers = new("SO");
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up a company with one customer, one item and one shop.</summary>
    public SalesQuoteTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-quotes-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        context.Set<Location>().Add(new Location
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "JED-01",
            Name = "Jeddah shop",
            IsSellable = true,
        });

        context.Set<Item>().Add(new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "ITEM-1001",
            Description = "Desk lamp",
            BaseUnitOfMeasure = "EA",
            UnitPrice = 100m,
            UnitCost = 40m,
        });

        context.Set<Customer>().Add(new Customer
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "C-0001",
            Name = "Al Faisaliah Trading",
        });

        context.SaveChanges();
    }

    /// <summary>
    /// A quote can be made for goods that are not on the shelf, because that is what a lead time
    /// is for.
    /// </summary>
    [Fact]
    public async Task Nothing_on_hand_does_not_stop_a_quote()
    {
        using var context = NewContext();

        var result = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 500m)],
            locationCode: "JED-01");

        result.Succeeded.ShouldBeTrue("a quote reserves nothing and promises no stock");
        result.Value.Lines.Single().UnitPrice.ShouldBe(100m);
        result.Value.Status.ShouldBe(SalesQuoteStatus.Draft);
    }

    /// <summary>An item nobody has entered cannot be quoted for.</summary>
    [Fact]
    public async Task An_item_that_does_not_exist_is_refused()
    {
        using var context = NewContext();

        var result = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "NOSUCH", 1m)]);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == SalesMessages.ItemNotFound);
    }

    /// <summary>The quote takes the customer's agreed price, as an order would.</summary>
    [Fact]
    public async Task A_quote_takes_the_price_this_customer_was_agreed()
    {
        using var context = NewContext();

        await Prices(context).SaveAsync(new PriceListRequest(
            "TRADE",
            "Trade",
            Lines: [new PriceListLineRequest("ITEM-1001", 80m)]));

        await Prices(context).AssignAsync("C-0001", "TRADE");

        var result = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 2m)],
            locationCode: "JED-01");

        result.Value.Lines.Single().UnitPrice.ShouldBe(80m);
    }

    /// <summary>
    /// The price on the quote goes onto the order, even when the price list has moved since.
    /// </summary>
    /// <remarks>
    /// This is the one that matters. The customer accepted the figure in front of them. Looking it
    /// up again on acceptance would charge them something they never agreed to, and every report
    /// would show a perfectly ordinary order.
    /// </remarks>
    [Fact]
    public async Task Accepting_carries_the_quoted_price_even_after_the_list_moves()
    {
        using var context = NewContext();

        await Prices(context).SaveAsync(new PriceListRequest(
            "TRADE",
            "Trade",
            Lines: [new PriceListLineRequest("ITEM-1001", 80m)]));

        await Prices(context).AssignAsync("C-0001", "TRADE");

        var quote = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 2m)],
            locationCode: "JED-01");

        quote.Value.Lines.Single().UnitPrice.ShouldBe(80m);

        // The list goes up between the quote and the acceptance.
        await Prices(context).SaveAsync(new PriceListRequest(
            "TRADE",
            "Trade",
            Lines: [new PriceListLineRequest("ITEM-1001", 95m)]));

        var order = await Quotes(context).AcceptAsync(quote.Value.No);

        order.Succeeded.ShouldBeTrue();
        order.Value.Lines.Single().UnitPrice.ShouldBe(
            80m,
            "the customer accepted 80, whatever the list says now");
    }

    /// <summary>Accepting marks the quote and records which order it became.</summary>
    [Fact]
    public async Task Accepting_records_the_order_it_became()
    {
        using var context = NewContext();

        var quote = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 1m)],
            locationCode: "JED-01");

        var order = await Quotes(context).AcceptAsync(quote.Value.No);

        var stored = await Quotes(context).LoadAsync(quote.Value.No);

        stored.ShouldNotBeNull();
        stored.Status.ShouldBe(SalesQuoteStatus.Accepted);
        stored.OrderNo.ShouldBe(order.Value.No);
    }

    /// <summary>
    /// Accepting twice is refused: two orders behind one agreement would both look legitimate.
    /// </summary>
    [Fact]
    public async Task A_quote_can_only_be_accepted_once()
    {
        using var context = NewContext();

        var quote = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 1m)],
            locationCode: "JED-01");

        (await Quotes(context).AcceptAsync(quote.Value.No)).Succeeded.ShouldBeTrue();

        var again = await Quotes(context).AcceptAsync(quote.Value.No);

        again.Failed.ShouldBeTrue();
        again.Messages.ShouldContain(m => m.Code == SalesMessages.QuoteAlreadyAccepted);
    }

    /// <summary>
    /// An expired quote is refused rather than repriced.
    /// </summary>
    /// <remarks>
    /// Repricing without saying so is the same wrong as looking the price up again on acceptance,
    /// in a different coat: the customer is charged a number they never saw.
    /// </remarks>
    [Fact]
    public async Task An_expired_quote_is_refused_rather_than_repriced()
    {
        using var context = NewContext();

        var quote = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 1m)],
            validUntil: new DateOnly(2026, 6, 10),
            locationCode: "JED-01");

        quote.Succeeded.ShouldBeTrue();

        _clock.Advance(new DateTime(2026, 6, 11, 9, 0, 0, DateTimeKind.Utc));

        var accepted = await Quotes(context).AcceptAsync(quote.Value.No);

        accepted.Failed.ShouldBeTrue();
        accepted.Messages.ShouldContain(m => m.Code == SalesMessages.QuoteHasExpired);
        context.Set<SalesOrder>().Count().ShouldBe(0, "nothing was ordered");
    }

    /// <summary>A quote whose expiry is already past is refused when it is made.</summary>
    [Fact]
    public async Task A_quote_that_runs_out_before_it_starts_is_refused()
    {
        using var context = NewContext();

        var result = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 1m)],
            validUntil: new DateOnly(2026, 5, 1));

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == SalesMessages.QuoteExpiresBeforeItIsMade);
    }

    /// <summary>A declined quote cannot then be turned into an order.</summary>
    [Fact]
    public async Task A_declined_quote_cannot_be_accepted()
    {
        using var context = NewContext();

        var quote = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 1m)],
            locationCode: "JED-01");

        await Quotes(context).DeclineAsync(quote.Value.No, "Bought elsewhere");

        var accepted = await Quotes(context).AcceptAsync(quote.Value.No);

        accepted.Failed.ShouldBeTrue();
        accepted.Messages.ShouldContain(m => m.Code == SalesMessages.QuoteWasDeclined);
    }

    /// <summary>The sweep marks what ran out, and leaves what did not.</summary>
    [Fact]
    public async Task The_sweep_marks_only_what_ran_out()
    {
        using var context = NewContext();

        var stale = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 1m)],
            validUntil: new DateOnly(2026, 6, 5));

        var live = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 1m)],
            validUntil: new DateOnly(2026, 7, 1));

        _clock.Advance(new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc));

        (await Quotes(context).ExpireAsync()).ShouldBe(1);

        (await Quotes(context).LoadAsync(stale.Value.No))!.Status
            .ShouldBe(SalesQuoteStatus.Expired);

        (await Quotes(context).LoadAsync(live.Value.No))!.Status
            .ShouldBe(SalesQuoteStatus.Draft);
    }

    /// <summary>An accepted quote is left alone by the sweep, however old it is.</summary>
    [Fact]
    public async Task The_sweep_leaves_an_accepted_quote_alone()
    {
        using var context = NewContext();

        var quote = await Quotes(context).CreateAsync(
            "C-0001",
            [new SalesQuoteLineRequest(SalesLineType.Item, "ITEM-1001", 1m)],
            validUntil: new DateOnly(2026, 6, 5),
            locationCode: "JED-01");

        await Quotes(context).AcceptAsync(quote.Value.No);

        _clock.Advance(new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc));

        (await Quotes(context).ExpireAsync()).ShouldBe(0);

        (await Quotes(context).LoadAsync(quote.Value.No))!.Status
            .ShouldBe(SalesQuoteStatus.Accepted);
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(
            _options,
            _tenancy,
            new StubUser(),
            _clock,
            [new InventorySchema(), new SalesSchema(), new Finance.FinanceSchema()]);

        _opened.Add(context);

        return context;
    }

    private MessageCatalog Catalog()
        => new([.. PlatformMessages.All, .. InventoryMessages.All, .. SalesMessages.All]);

    private PricingService Prices(AsapDbContext context)
        => new(context, Catalog(), _tenancy);

    private SalesQuoteService Quotes(AsapDbContext context)
    {
        var catalog = Catalog();

        var orders = new SalesOrderService(
            context,
            catalog,
            new OverrideAuditor(context, _tenancy, new StubUser(), _clock),
            _orderNumbers,
            _setup,
            _tenancy,
            new StubUser(),
            _clock,
            new PricingService(context, catalog, _tenancy),
            NullLogger<SalesOrderService>.Instance);

        return new SalesQuoteService(
            context,
            orders,
            new PricingService(context, catalog, _tenancy),
            catalog,
            _quoteNumbers,
            _setup,
            _tenancy,
            new StubUser(),
            _clock,
            NullLogger<SalesQuoteService>.Instance);
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
        public Guid? UserId { get; } = Guid.Parse("eeeeeeee-0000-0000-0000-00000000005e");

        public string? UserName => "salim";

        public string? DisplayName => "Salim";

        public string? Culture => "en";

        public bool IsSuperUser => false;

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();

        public bool Has(string permissionKey) => Permissions.Contains(permissionKey);

        public Guid RequireUserId() => UserId ?? Guid.Empty;
    }

    /// <summary>A clock the tests can move, because expiry is a question about time passing.</summary>
    private sealed class StubClock(DateTime utcNow) : IClock
    {
        private DateTime _now = utcNow;

        public DateTime UtcNow => _now;

        public DateOnly Today => DateOnly.FromDateTime(_now);

        public void Advance(DateTime to) => _now = to;
    }

    private sealed class StubNumbers(string prefix) : INumberSeriesService
    {
        private int _next;

        public Task<Result<string>> NextAsync(
            string seriesCode,
            DateOnly documentDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success($"{prefix}-{++_next:0000}"));

        public Task<Result<string>> PeekAsync(
            string seriesCode,
            DateOnly documentDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success($"{prefix}-{_next + 1:0000}"));

        public Task<Result> ValidateManualAsync(
            string seriesCode,
            string number,
            DateOnly documentDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }

    /// <summary>
    /// Answers the two settings a quote reads, and the series an order reads.
    /// </summary>
    private sealed class StubSetup : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(
            string key,
            CancellationToken cancellationToken = default)
        {
            object value = key switch
            {
                "Sales.Quotes.ValidForDays" => 30,
                "Sales.Quotes.NumberSeries" => "SALES-QTE",
                _ => "SALES-ORD",
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
