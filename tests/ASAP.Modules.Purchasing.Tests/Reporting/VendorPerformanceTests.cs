using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Purchasing.Orders;
using ASAP.Modules.Purchasing.Reporting;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Purchasing.Tests.Reporting;

/// <summary>
/// Covers what counts as a fair measure of a vendor.
/// </summary>
/// <remarks>
/// The arithmetic here is trivial and the judgment is not. Two decisions decide whether the report
/// tells anybody anything: what to do with a vendor who arrives early as often as they arrive late,
/// and what to do with one who never promised a date at all. Get either wrong and the worst
/// supplier on the list comes out top.
/// </remarks>
public sealed class VendorPerformanceTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000b1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000b1");
    private static readonly DateOnly Promised = new(2026, 8, 10);
    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenantContext _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];
    private int _orderNo;

    public VendorPerformanceTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-vendorperf-{Guid.CreateVersion7()}")
            .Options;

        _tenancy.TenantId = Tenant;
        _tenancy.CompanyId = Company;
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(
            _options,
            _tenancy,
            new StubUser(),
            _clock,
            [new PurchasingSchema(), new InventorySchema()]);

        _opened.Add(context);
        return context;
    }

    private PurchaseReportService Reports(AsapDbContext context) => new(context, _clock);

    /// <summary>An order that arrived on a given day, promised for another.</summary>
    private async Task DeliveryAsync(
        string vendorNo,
        DateOnly? promised,
        DateOnly arrived,
        decimal quantity = 10m,
        int lines = 1)
    {
        await using var context = NewContext();

        var no = $"PO-{++_orderNo:0000}";

        context.Set<PurchaseOrder>().Add(new PurchaseOrder
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = no,
            VendorNo = vendorNo,
            VendorName = vendorNo == "V-1" ? "Punctual Supplies" : "Erratic Trading",
            OrderDate = From,
            ExpectedReceiptDate = promised,
            Status = PurchaseOrderStatus.Received,
        });

        for (var line = 0; line < lines; line++)
        {
            context.Set<ItemLedgerEntry>().Add(new ItemLedgerEntry
            {
                TenantId = Tenant,
                CompanyId = Company,
                ItemNo = $"ITEM-{line}",
                EntryType = ItemLedgerEntryType.Purchase,
                PostingDate = arrived,
                LocationCode = "HO",
                Quantity = quantity,
                RemainingQuantity = quantity,
                DocumentNo = no,
                SourceCode = "PURCH",
            });
        }

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Lateness_is_averaged_over_the_late_ones_only()
    {
        // A fortnight late, then five days early. Averaging both would pull this vendor towards
        // punctual, which is the opposite of what anybody wants to know: an erratic
        // supplier is worse than a consistently slow one, because nothing can be planned around
        // them.
        await DeliveryAsync("V-2", Promised, Promised.AddDays(14));
        await DeliveryAsync("V-2", Promised, Promised.AddDays(-5));

        await using var context = NewContext();

        var row = (await Reports(context).VendorPerformanceAsync(From, To)).Single();

        row.Deliveries.ShouldBe(2);
        row.OnTime.ShouldBe(1);
        row.Late.ShouldBe(1);
        row.AverageDaysLate.ShouldBe(14m);
        row.WorstDaysLate.ShouldBe(14);
    }

    [Fact]
    public async Task A_vendor_who_promises_nothing_does_not_come_out_punctual()
    {
        // Scoring an unpromised delivery as on time would make the vendor who never commits to a
        // date the best performer on the report.
        await DeliveryAsync("V-2", promised: null, arrived: Promised.AddDays(10));
        await DeliveryAsync("V-2", promised: null, arrived: Promised.AddDays(15));

        await using var context = NewContext();

        var row = (await Reports(context).VendorPerformanceAsync(From, To)).Single();

        row.Deliveries.ShouldBe(2);
        row.OnTime.ShouldBe(0);
        row.Late.ShouldBe(0);
        row.Unpromised.ShouldBe(2);
        row.AverageDaysLate.ShouldBeNull();
    }

    [Fact]
    public async Task Arriving_on_the_promised_day_is_on_time()
    {
        // On the day, not before it. A vendor who hits the date exactly every time is the best
        // possible supplier and must not be scored as a day late by an off-by-one.
        await DeliveryAsync("V-1", Promised, Promised);

        await using var context = NewContext();

        var row = (await Reports(context).VendorPerformanceAsync(From, To)).Single();

        row.OnTime.ShouldBe(1);
        row.Late.ShouldBe(0);
    }

    [Fact]
    public async Task One_lorry_with_six_items_on_it_is_one_delivery()
    {
        // Counting lines would make a vendor's record depend on how the buyer chose to split the
        // order, which is not a fact about the vendor at all.
        await DeliveryAsync("V-1", Promised, Promised, lines: 6);

        await using var context = NewContext();

        var row = (await Reports(context).VendorPerformanceAsync(From, To)).Single();

        row.Deliveries.ShouldBe(1);
    }

    [Fact]
    public async Task Two_arrivals_on_different_days_are_two_deliveries()
    {
        await DeliveryAsync("V-1", Promised, Promised);
        await DeliveryAsync("V-1", Promised, Promised.AddDays(3));

        await using var context = NewContext();

        var row = (await Reports(context).VendorPerformanceAsync(From, To)).Single();

        row.Deliveries.ShouldBe(2);
        row.OnTime.ShouldBe(1);
        row.Late.ShouldBe(1);
        row.AverageDaysLate.ShouldBe(3m);
    }

    [Fact]
    public async Task The_worst_average_comes_first()
    {
        await DeliveryAsync("V-1", Promised, Promised.AddDays(1));
        await DeliveryAsync("V-2", Promised, Promised.AddDays(20));

        await using var context = NewContext();

        var rows = await Reports(context).VendorPerformanceAsync(From, To);

        rows[0].VendorNo.ShouldBe("V-2");
        rows[1].VendorNo.ShouldBe("V-1");
    }

    [Fact]
    public async Task Nothing_delivered_in_the_period_reports_nothing()
    {
        await DeliveryAsync("V-1", Promised, new DateOnly(2026, 12, 1));

        await using var context = NewContext();

        (await Reports(context).VendorPerformanceAsync(From, To)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_order_still_to_arrive_is_open_and_says_how_late_it_is()
    {
        await using (var context = NewContext())
        {
            var order = new PurchaseOrder
            {
                TenantId = Tenant,
                CompanyId = Company,
                No = "PO-OPEN",
                VendorNo = "V-1",
                VendorName = "Punctual Supplies",
                OrderDate = From,

                // Twenty-one days before the stub clock's today.
                ExpectedReceiptDate = new DateOnly(2026, 8, 10),
                Status = PurchaseOrderStatus.Released,
            };

            order.Lines.Add(new PurchaseOrderLine
            {
                TenantId = Tenant,
                CompanyId = Company,
                LineNo = 10,
                Type = PurchaseLineType.Item,
                ItemNo = "ITEM-0",
                Description = "Something",
                Quantity = 10m,
                DirectUnitCost = 5m,
                QuantityReceived = 4m,
            });

            context.Set<PurchaseOrder>().Add(order);
            await context.SaveChangesAsync();
        }

        await using var check = NewContext();

        var row = (await Reports(check).OpenOrdersAsync()).Single();

        row.OrderNo.ShouldBe("PO-OPEN");
        row.QuantityOutstanding.ShouldBe(6m);
        row.ValueOutstanding.ShouldBe(30m);
        row.DaysOverdue.ShouldBe(21);
    }

    [Fact]
    public async Task A_fully_received_order_is_not_open()
    {
        await using (var context = NewContext())
        {
            var order = new PurchaseOrder
            {
                TenantId = Tenant,
                CompanyId = Company,
                No = "PO-DONE",
                VendorNo = "V-1",
                VendorName = "Punctual Supplies",
                OrderDate = From,
                Status = PurchaseOrderStatus.Received,
            };

            order.Lines.Add(new PurchaseOrderLine
            {
                TenantId = Tenant,
                CompanyId = Company,
                LineNo = 10,
                Type = PurchaseLineType.Item,
                ItemNo = "ITEM-0",
                Description = "Something",
                Quantity = 10m,
                DirectUnitCost = 5m,
                QuantityReceived = 10m,
            });

            context.Set<PurchaseOrder>().Add(order);
            await context.SaveChangesAsync();
        }

        await using var check = NewContext();

        (await Reports(check).OpenOrdersAsync()).ShouldBeEmpty();
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
}
