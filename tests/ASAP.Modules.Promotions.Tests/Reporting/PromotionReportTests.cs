using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Promotions.Offers;
using ASAP.Modules.Promotions.Reporting;
using ASAP.Platform.Kernel.Promotions;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Promotions.Tests.Reporting;

/// <summary>
/// What an offer actually did, and what a report is allowed to claim about it.
/// </summary>
/// <remarks>
/// Most of this is about refusing to answer. A margin measured on lines with no cost, a percentage
/// with nothing to divide by, a cannibalisation figure inferred from two numbers -- each of them is
/// a confident answer produced by missing information, and each is believed because it has a
/// decimal point in it.
/// </remarks>
public sealed class PromotionReportTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000a1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000aa");
    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc));
    private readonly StubUsage _usage = new();
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up two offers, one of which nobody uses.</summary>
    public PromotionReportTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-promotion-reports-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        context.Set<Item>().Add(new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "WATER",
            Description = "Bottled water",
            BaseUnitOfMeasure = "CASE",
            UnitPrice = 20m,
            UnitCost = 12m,
            LastDirectCost = 12m,
        });

        context.Set<Offer>().AddRange(
            new Offer
            {
                TenantId = Tenant,
                CompanyId = Company,
                Code = "USED",
                Name = "Ten per cent off water",
                Kind = OfferKind.Percentage,
                Scope = OfferScope.Item,
                Value = 10m,
                StartsOn = From,
                EndsOn = To,
                Targets = [new OfferTarget { TenantId = Tenant, CompanyId = Company, ItemNo = "WATER" }],
            },
            new Offer
            {
                TenantId = Tenant,
                CompanyId = Company,
                Code = "IGNORED",
                Name = "Nobody used this one",
                Kind = OfferKind.Percentage,
                Scope = OfferScope.Item,
                Value = 5m,
                StartsOn = From,
                EndsOn = To,
                Targets = [new OfferTarget { TenantId = Tenant, CompanyId = Company, ItemNo = "WATER" }],
            });

        context.SaveChanges();
    }

    /// <summary>
    /// An offer nobody used appears, because it is the most useful row in the report.
    /// </summary>
    [Fact]
    public async Task An_offer_nobody_used_still_appears()
    {
        _usage.Lines.Add(Sold("USED", 10m, discount: 20m, net: 180m, cost: 12m));

        await using var context = NewContext();

        var rows = await Reports(context).UptakeAsync(From, To);

        rows.Count.ShouldBe(2);

        var ignored = rows.Single(r => r.OfferCode == "IGNORED");

        ignored.TimesApplied.ShouldBe(0);
        ignored.DiscountGiven.ShouldBe(0m);
        ignored.Margin.ShouldBeNull("no lines means no margin, not a margin of nothing");
    }

    /// <summary>What an offer gave away and what it left, counted from what actually sold.</summary>
    [Fact]
    public async Task Uptake_counts_what_was_given_away_and_what_was_left()
    {
        _usage.Lines.Add(Sold("USED", 10m, discount: 20m, net: 180m, cost: 12m, document: "R-1"));
        _usage.Lines.Add(Sold("USED", 5m, discount: 10m, net: 90m, cost: 12m, document: "R-2"));

        await using var context = NewContext();

        var row = (await Reports(context).UptakeAsync(From, To)).Single(r => r.OfferCode == "USED");

        row.TimesApplied.ShouldBe(2);
        row.Documents.ShouldBe(2);
        row.Quantity.ShouldBe(15m);
        row.DiscountGiven.ShouldBe(30m);
        row.NetSold.ShouldBe(270m);
        row.CostOfSales.ShouldBe(180m, "fifteen units at twelve");
        row.Margin.ShouldBe(90m);
        row.MarginPercent.ShouldBe(Math.Round(90m / 270m * 100m, 2));
    }

    /// <summary>
    /// Lines with no recorded cost are excluded from the margin and counted where they can be seen.
    /// </summary>
    /// <remarks>
    /// The alternative is treating a missing cost as nought, which reports a hundred per cent
    /// margin on every line nobody has a figure for.
    /// </remarks>
    [Fact]
    public async Task Lines_with_no_cost_are_left_out_of_the_margin_and_said_so()
    {
        _usage.Lines.Add(Sold("USED", 10m, discount: 20m, net: 180m, cost: 12m, document: "R-1"));
        _usage.Lines.Add(Sold("USED", 4m, discount: 8m, net: 72m, cost: null, document: "R-2"));

        await using var context = NewContext();

        var row = (await Reports(context).UptakeAsync(From, To)).Single(r => r.OfferCode == "USED");

        row.Quantity.ShouldBe(14m, "everything sold is counted");
        row.CostOfSales.ShouldBe(120m, "only the ten units that have a cost");
        row.Margin.ShouldBe(60m, "measured against the net of those same ten units");

        row.QuantityWithoutCost.ShouldBe(
            4m,
            "the report has to be able to say the margin covers less than everything");
    }

    /// <summary>
    /// An offer that gave the goods away has a real negative margin and no percentage.
    /// </summary>
    [Fact]
    public async Task Nothing_to_divide_by_prints_no_percentage()
    {
        _usage.Lines.Add(Sold("USED", 10m, discount: 200m, net: 0m, cost: 12m));

        await using var context = NewContext();

        var row = (await Reports(context).UptakeAsync(From, To)).Single(r => r.OfferCode == "USED");

        row.Margin.ShouldBe(-120m, "the goods cost something and brought in nothing");

        row.MarginPercent.ShouldBeNull(
            "nought per cent would be a lie a spreadsheet then averages");
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private static OfferUsageLine Sold(
        string offerCode,
        decimal quantity,
        decimal discount,
        decimal net,
        decimal? cost,
        string document = "R-1")
        => new(offerCode, From.AddDays(3), document, "WATER", quantity, discount, net, cost, "POS");

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(
            _options,
            _tenancy,
            new StubUser(),
            _clock,
            [new InventorySchema(), new PromotionsSchema()]);

        _opened.Add(context);

        return context;
    }

    private PromotionReportService Reports(AsapDbContext context)
        => new(context, [_usage], _clock);

    /// <summary>Stands in for the till, which Promotions cannot see.</summary>
    private sealed class StubUsage : IOfferUsage
    {
        public List<OfferUsageLine> Lines { get; } = [];

        public string SourceCode => "POS";

        public Task<IReadOnlyList<OfferUsageLine>> BetweenAsync(
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OfferUsageLine>>(
                [.. Lines.Where(l => l.SoldOn >= from && l.SoldOn <= to)]);
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

}
