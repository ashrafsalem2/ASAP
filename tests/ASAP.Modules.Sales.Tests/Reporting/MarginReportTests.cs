using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Sales.Orders;
using ASAP.Modules.Sales.Reporting;
using ASAP.Platform.Kernel.Documents;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Sales.Tests.Reporting;

/// <summary>
/// Covers what a margin report is allowed to claim.
/// </summary>
/// <remarks>
/// The arithmetic is a subtraction. The judgment is what to do about a cost nobody has confirmed
/// yet, and what to print where a percentage has no answer -- both of which decide whether somebody
/// can act on the figure or is about to be surprised by it.
/// </remarks>
public sealed class MarginReportTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000f5");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000f5");
    private static readonly DateOnly Day = new(2026, 8, 10);
    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    public MarginReportTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-margin-{Guid.CreateVersion7()}")
            .Options;

        _tenancy.TenantId = Tenant;
        _tenancy.CompanyId = Company;

        using var context = NewContext();

        context.Set<Item>().AddRange(
            Item("GOOD", "Healthy widget"),
            Item("THIN", "Barely worth selling"));

        context.SaveChanges();

        static Item Item(string no, string description)
            => new()
            {
                TenantId = Tenant,
                CompanyId = Company,
                No = no,
                Description = description,
                BaseUnitOfMeasure = "PCS",
                CostingMethod = CostingMethod.Fifo,
            };
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(
            _options,
            _tenancy,
            new StubUser(),
            _clock,
            [new SalesSchema(), new InventorySchema()]);

        _opened.Add(context);
        return context;
    }

    private SalesReportService Reports(AsapDbContext context, params IDocumentParties[] parties)
        => new(context, _clock, parties);

    /// <summary>Something sold, at a price, for a cost.</summary>
    private async Task SoldAsync(
        string itemNo,
        decimal quantity,
        decimal revenue,
        decimal cost,
        string? documentNo = null,
        bool estimated = false,
        ItemLedgerEntryType type = ItemLedgerEntryType.Sale)
    {
        await using var context = NewContext();

        context.Set<ValueEntry>().Add(new ValueEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            ItemNo = itemNo,
            EntryType = ValueEntryType.DirectCost,
            ItemLedgerEntryType = type,
            PostingDate = Day,
            Quantity = -quantity,
            SalesAmount = revenue,

            // Negative, because stock left.
            CostAmount = -cost,
            UnitCost = quantity == 0m ? 0m : cost / quantity,
            IsExpected = estimated,
            DocumentNo = documentNo,
            SourceCode = "TEST",
        });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Margin_is_revenue_less_what_the_goods_cost()
    {
        await SoldAsync("GOOD", 10m, revenue: 300m, cost: 100m);

        await using var context = NewContext();

        var row = (await Reports(context).MarginByItemAsync(From, To)).Single();

        row.Revenue.ShouldBe(300m);
        row.Cost.ShouldBe(100m);
        row.Margin.ShouldBe(200m);
        row.MarginPercent.ShouldBe(66.67m);
        row.EstimatedCost.ShouldBe(0m);
    }

    [Fact]
    public async Task A_margin_resting_on_an_estimate_says_how_much_of_it_does()
    {
        // Sold from stock that had not arrived. The cost is a guess until the goods are received
        // and the settlement runs, so this margin will move -- and a figure somebody acts on that
        // changes underneath them is worse than one that admitted it was provisional.
        await SoldAsync("GOOD", 10m, revenue: 300m, cost: 100m, estimated: true);

        await using var context = NewContext();

        var row = (await Reports(context).MarginByItemAsync(From, To)).Single();

        row.Margin.ShouldBe(200m);
        row.EstimatedCost.ShouldBe(100m);
    }

    [Fact]
    public async Task The_thinnest_margin_comes_first()
    {
        // A margin report is read to find the problems. Sorting the healthy items to the top would
        // bury the ones losing money underneath them.
        await SoldAsync("GOOD", 10m, revenue: 300m, cost: 100m);
        await SoldAsync("THIN", 10m, revenue: 300m, cost: 290m);

        await using var context = NewContext();

        var rows = await Reports(context).MarginByItemAsync(From, To);

        rows[0].Key.ShouldBe("THIN");
        rows[1].Key.ShouldBe("GOOD");
    }

    [Fact]
    public async Task A_margin_on_no_revenue_has_no_percentage()
    {
        // Goods given away. The margin is a real negative number; the percentage is a division by
        // nought, and printing it as nought would be a lie a spreadsheet then averages into
        // everything else.
        await SoldAsync("GOOD", 5m, revenue: 0m, cost: 50m);

        await using var context = NewContext();

        var row = (await Reports(context).MarginByItemAsync(From, To)).Single();

        row.Margin.ShouldBe(-50m);
        row.MarginPercent.ShouldBeNull();
    }

    [Fact]
    public async Task Goods_coming_back_are_netted_off_the_month_they_came_back_in()
    {
        // A month with a lot of returns should report the margin the company actually made, not
        // the one it made before anybody changed their mind.
        await SoldAsync("GOOD", 10m, revenue: 300m, cost: 100m);

        await SoldAsync(
            "GOOD",
            -2m,
            revenue: -60m,
            cost: -20m,
            type: ItemLedgerEntryType.SalesReturn);

        await using var context = NewContext();

        var row = (await Reports(context).MarginByItemAsync(From, To)).Single();

        row.Revenue.ShouldBe(240m);
        row.Cost.ShouldBe(80m);
        row.Margin.ShouldBe(160m);
    }

    [Fact]
    public async Task Margin_by_customer_spans_every_channel()
    {
        // One sale on an invoice, one at a till. Sales cannot see the till and the till cannot see
        // Sales, so each answers for its own documents and the report takes both.
        await SoldAsync("GOOD", 10m, revenue: 300m, cost: 100m, documentNo: "SO-1");
        await SoldAsync("GOOD", 4m, revenue: 100m, cost: 40m, documentNo: "R-1");

        await using var context = NewContext();

        var rows = await Reports(
            context,
            new StubParties(("SO-1", "C-1", "Trading Company")),
            new StubParties(("R-1", "WALKIN", "Walk-in")))
            .MarginByCustomerAsync(From, To);

        rows.Count.ShouldBe(2);
        rows.Single(r => r.Key == "C-1").Revenue.ShouldBe(300m);
        rows.Single(r => r.Key == "WALKIN").Revenue.ShouldBe(100m);
    }

    [Fact]
    public async Task A_sale_nothing_claims_is_left_out_rather_than_lumped_under_a_blank()
    {
        // It means the module owning that document is not installed. Inventing a row would put
        // somebody else's trade into this company's worst-margin list.
        await SoldAsync("GOOD", 10m, revenue: 300m, cost: 100m, documentNo: "SO-1");
        await SoldAsync("GOOD", 4m, revenue: 100m, cost: 40m, documentNo: "UNKNOWN-1");

        await using var context = NewContext();

        var rows = await Reports(context, new StubParties(("SO-1", "C-1", "Trading Company")))
            .MarginByCustomerAsync(From, To);

        rows.ShouldHaveSingleItem().Key.ShouldBe("C-1");
    }

    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private sealed class StubParties(params (string Document, string No, string Name)[] parties) : IDocumentParties
    {
        public Task<IReadOnlyList<DocumentParty>> ForAsync(
            IReadOnlyCollection<string> documentNos,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DocumentParty>>(
            [
                .. parties
                    .Where(p => documentNos.Contains(p.Document))
                    .Select(static p => new DocumentParty(p.Document, p.No, p.Name)),
            ]);
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
}
