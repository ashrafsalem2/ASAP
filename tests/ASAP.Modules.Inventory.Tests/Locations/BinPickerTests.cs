using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Inventory.Tests.Locations;

/// <summary>
/// Which shelves a shipment comes off when nobody said.
/// </summary>
/// <remarks>
/// A warehouse with bins used to be unable to ship a sales order at all, because the shipment had
/// no bin to name and posting rightly refuses to guess one. The picker answers the way a picker
/// would: from the shelves that hold the item, in pick order.
/// </remarks>
public sealed class BinPickerTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000b7");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000b7");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    private readonly Location _warehouse;
    private readonly Bin _front;
    private readonly Bin _back;
    private readonly Bin _receiving;
    private readonly Item _widget;
    private readonly Item _car;

    /// <summary>
    /// A warehouse whose front shelf is picked before the back one, holding six widgets at the
    /// front and ten at the back, and a car on the back shelf.
    /// </summary>
    public BinPickerTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-bin-picker-{Guid.CreateVersion7()}")
            .Options;

        using var context = NewContext();

        _warehouse = new Location { TenantId = Tenant, CompanyId = Company, Code = "WH", Name = "Warehouse", UsesBins = true };
        var shop = new Location { TenantId = Tenant, CompanyId = Company, Code = "SHOP", Name = "Shop" };
        context.Set<Location>().AddRange(_warehouse, shop);

        _widget = new Item { TenantId = Tenant, CompanyId = Company, No = "WIDGET", Description = "Widget", BaseUnitOfMeasure = "EA" };
        _car = new Item { TenantId = Tenant, CompanyId = Company, No = "CAR", Description = "Car", BaseUnitOfMeasure = "EA", Tracking = ItemTracking.Serial, CostingMethod = CostingMethod.Specific };
        context.Set<Item>().AddRange(_widget, _car);
        context.SaveChanges();

        // Named so that alphabetical order and pick order disagree: the walk decides, not the label.
        _front = new Bin { TenantId = Tenant, CompanyId = Company, LocationId = _warehouse.Id, Code = "Z-FRONT", PickOrder = 1 };
        _back = new Bin { TenantId = Tenant, CompanyId = Company, LocationId = _warehouse.Id, Code = "A-BACK", PickOrder = 2 };
        _receiving = new Bin { TenantId = Tenant, CompanyId = Company, LocationId = _warehouse.Id, Code = "DOCK", PickOrder = 9, IsReceiving = true };
        context.Set<Bin>().AddRange(_front, _back, _receiving);
        context.SaveChanges();

        Held(context, _widget, _front, 6m);
        Held(context, _widget, _back, 10m);
        Held(context, _car, _front, 1m, serial: "VIN-9");
        Held(context, _car, _back, 1m, serial: "VIN-7");
        context.SaveChanges();
    }

    /// <summary>A line that fits on the first shelf in pick order comes off that shelf alone.</summary>
    [Fact]
    public async Task The_first_shelf_in_pick_order_is_picked()
    {
        var picked = await PickAsync(Out("WIDGET", 4m));

        picked.Single().BinCode.ShouldBe("Z-FRONT", "pick order, not the alphabet");
        picked.Single().Quantity.ShouldBe(-4m);
    }

    /// <summary>A line too big for one shelf is split, and what it sold for is shared out whole.</summary>
    [Fact]
    public async Task A_line_too_big_for_one_shelf_is_split()
    {
        await using var context = NewContext();

        var result = await new BinPicker(context, Catalog()).PickAsync([Out("WIDGET", 9m) with { SalesAmount = 100m, LineNo = 20 }]);

        result.Value.Select(static m => (m.BinCode, m.Quantity)).ShouldBe([("Z-FRONT", -6m), ("A-BACK", -3m)]);
        result.Value.Sum(static m => m.SalesAmount).ShouldBe(100m);
        result.Value.ShouldAllBe(m => m.LineNo == 20);

        var note = result.Messages.Single();
        note.Code.ShouldBe(InventoryMessages.BinsPicked);
        note.Detail.ShouldNotBeNull().ShouldContain("Z-FRONT (6), A-BACK (3)");
    }

    /// <summary>Two lines of one posting do not both take the same six off the front shelf.</summary>
    [Fact]
    public async Task Two_lines_do_not_pick_the_same_stock_twice()
    {
        var picked = await PickAsync(Out("WIDGET", 5m), Out("WIDGET", 5m));

        picked.Select(static m => (m.BinCode, m.Quantity)).ShouldBe([("Z-FRONT", -5m), ("Z-FRONT", -1m), ("A-BACK", -4m)]);
    }

    /// <summary>More than the shelves hold leaves the shortfall on the last shelf, for posting to judge.</summary>
    [Fact]
    public async Task A_shortfall_stays_on_the_last_shelf_picked()
    {
        var picked = await PickAsync(Out("WIDGET", 20m));

        picked.Select(static m => (m.BinCode, m.Quantity)).ShouldBe([("Z-FRONT", -6m), ("A-BACK", -14m)]);
    }

    /// <summary>With nothing on any shelf, the goods are taken from where their receipt will land.</summary>
    [Fact]
    public async Task Nothing_on_any_shelf_falls_to_the_receiving_bin()
    {
        await using (var context = NewContext())
        {
            context.Set<Item>().Add(new Item { TenantId = Tenant, CompanyId = Company, No = "GHOST", Description = "Not here", BaseUnitOfMeasure = "EA" });
            await context.SaveChangesAsync();
        }

        (await PickAsync(Out("GHOST", 2m))).Single().BinCode.ShouldBe("DOCK");
    }

    /// <summary>A named serial comes off the shelf it is standing on, even when an earlier shelf holds another.</summary>
    [Fact]
    public async Task A_serial_comes_off_its_own_shelf()
        => (await PickAsync(Out("CAR", 1m) with { TrackingNo = "vin-7" })).Single().BinCode.ShouldBe("A-BACK");

    /// <summary>What somebody named is left alone, and so are places without bins and goods coming in.</summary>
    [Fact]
    public async Task Named_bins_plain_locations_and_receipts_are_left_alone()
    {
        var picked = await PickAsync(
            Out("WIDGET", 2m) with { BinCode = "A-BACK" },
            Out("WIDGET", 2m) with { LocationCode = "SHOP" },
            new StockMovementRequest("WIDGET", "WH", 5m, 10m, ItemLedgerEntryType.Purchase));

        picked[0].BinCode.ShouldBe("A-BACK");
        picked[1].BinCode.ShouldBeNull();
        picked[2].BinCode.ShouldBeNull("a receipt with no bin is posting's to send to the dock");
    }

    /// <summary>Closes every context the test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private static StockMovementRequest Out(string itemNo, decimal quantity)
        => new(itemNo, "WH", -quantity, EntryType: ItemLedgerEntryType.Sale);

    private async Task<List<StockMovementRequest>> PickAsync(params StockMovementRequest[] movements)
    {
        await using var context = NewContext();

        return (await new BinPicker(context, Catalog()).PickAsync(movements)).Value;
    }

    private void Held(AsapDbContext context, Item item, Bin bin, decimal quantity, string? serial = null)
    {
        context.Set<ItemLedgerEntry>().Add(new ItemLedgerEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            ItemId = item.Id,
            ItemNo = item.No,
            PostingDate = new DateOnly(2026, 9, 1),
            LocationId = _warehouse.Id,
            LocationCode = _warehouse.Code,
            BinId = bin.Id,
            BinCode = bin.Code,
            Quantity = quantity,
            RemainingQuantity = quantity,
            SerialNo = serial,
            EntryType = ItemLedgerEntryType.Purchase,
            SourceCode = "PURCH",
        });
    }

    private static MessageCatalog Catalog() => new([.. PlatformMessages.All, .. InventoryMessages.All]);

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
        _opened.Add(context);
        return context;
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

    private sealed class StubUser : IUserContext
    {
        public Guid? UserId => null;

        public string? UserName => "tests";

        public string? DisplayName => "Tests";

        public string? Culture => "en";

        public bool IsSuperUser => true;

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();

        public bool Has(string permissionKey) => true;

        public Guid RequireUserId() => Guid.Empty;
    }

    private sealed class StubClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }
}
