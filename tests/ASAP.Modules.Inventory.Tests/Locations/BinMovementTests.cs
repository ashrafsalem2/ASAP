using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Inventory.Tests.Locations;

/// <summary>
/// Moving goods between shelves inside one place.
/// </summary>
/// <remarks>
/// The thing worth proving is what does <em>not</em> change. A box moved from one shelf to the
/// next leaves the quantity at the location alone and leaves what the stock is worth alone; only
/// the record of which shelf it is standing on moves. A bin movement that touched either would be
/// an adjustment wearing a different name.
/// </remarks>
public sealed class BinMovementTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000b1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000b1");
    private static readonly DateOnly Today = new(2026, 8, 31);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc));
    private readonly StubNumbers _numbers = new("BM");
    private readonly List<AsapDbContext> _opened = [];

    private Guid _locationId;
    private Guid _binA;
    private Guid _binB;

    /// <summary>Sets up a bin-tracked warehouse with forty on shelf A and nothing on shelf B.</summary>
    public BinMovementTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-bin-movements-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        var location = new Location
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "WH",
            Name = "The warehouse",
            UsesBins = true,
        };

        context.Set<Location>().Add(location);

        var plain = new Location
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "SHOP",
            Name = "The shop",
            UsesBins = false,
        };

        context.Set<Location>().Add(plain);
        context.SaveChanges();

        _locationId = location.Id;

        var a = new Bin { TenantId = Tenant, CompanyId = Company, LocationId = location.Id, Code = "A-01" };
        var b = new Bin { TenantId = Tenant, CompanyId = Company, LocationId = location.Id, Code = "B-01" };

        context.Set<Bin>().AddRange(a, b);

        var item = new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "WIDGET",
            Description = "Widget",
            BaseUnitOfMeasure = "EA",
            UnitCost = 10m,
        };

        context.Set<Item>().Add(item);
        context.SaveChanges();

        _binA = a.Id;
        _binB = b.Id;

        // Forty received onto shelf A, at ten each.
        context.Set<ItemLedgerEntry>().Add(new ItemLedgerEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            ItemId = item.Id,
            ItemNo = "WIDGET",
            PostingDate = new DateOnly(2026, 8, 1),
            LocationId = location.Id,
            LocationCode = "WH",
            BinId = a.Id,
            BinCode = "A-01",
            Quantity = 40m,
            RemainingQuantity = 40m,
            EntryType = ItemLedgerEntryType.Purchase,
            DocumentNo = "GRN-0001",
            SourceCode = "PURCHASE",
        });

        context.Set<ValueEntry>().Add(new ValueEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            ItemNo = "WIDGET",
            PostingDate = new DateOnly(2026, 8, 1),
            Quantity = 40m,
            CostAmount = 400m,
            EntryType = ValueEntryType.DirectCost,
            DocumentNo = "GRN-0001",
            SourceCode = "PURCHASE",
        });

        context.SaveChanges();
    }

    /// <summary>Ten move from one shelf to the other, and the bins say so.</summary>
    [Fact]
    public async Task Goods_move_from_one_shelf_to_another()
    {
        using var context = NewContext();

        var moved = await Movements(context).PostAsync(
            "WH",
            [new BinMovementLineRequest("WIDGET", "A-01", "B-01", 10m)],
            Today);

        moved.Succeeded.ShouldBeTrue();
        moved.Value.Status.ShouldBe(BinMovementStatus.Posted);
        moved.Value.Lines.Count.ShouldBe(1);

        (await HeldAsync(context, _binA)).ShouldBe(30m);
        (await HeldAsync(context, _binB)).ShouldBe(10m);
    }

    /// <summary>
    /// The quantity at the location does not change, and neither does what it is worth.
    /// </summary>
    /// <remarks>
    /// The whole point. A bin movement that moved either figure would be an adjustment wearing a
    /// different name, and the valuation would drift every time somebody tidied a shelf.
    /// </remarks>
    [Fact]
    public async Task Nothing_about_the_location_or_the_value_changes()
    {
        using var context = NewContext();

        var before = await OnHandAsync(context);
        var valueBefore = await ValueAsync(context);

        await Movements(context).PostAsync(
            "WH",
            [new BinMovementLineRequest("WIDGET", "A-01", "B-01", 10m)],
            Today);

        (await OnHandAsync(context)).ShouldBe(before, "the location has exactly what it had");
        (await ValueAsync(context)).ShouldBe(valueBefore, "and it is worth exactly what it was");
    }

    /// <summary>
    /// The entries written are not cost layers, so nothing later consumes them.
    /// </summary>
    /// <remarks>
    /// Remaining quantity is what makes an entry a layer available to be drawn from. A bin
    /// movement creating one would give the costing a free forty units at whatever cost it
    /// happened to carry.
    /// </remarks>
    [Fact]
    public async Task The_entries_are_not_cost_layers()
    {
        using var context = NewContext();

        await Movements(context).PostAsync(
            "WH",
            [new BinMovementLineRequest("WIDGET", "A-01", "B-01", 10m)],
            Today);

        var written = await context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.SourceCode == "BINMOVE")
            .ToListAsync();

        written.Count.ShouldBe(2, "a matched pair");
        written.Sum(static e => e.Quantity).ShouldBe(0m, "which nets to nothing");
        written.ShouldAllBe(e => e.RemainingQuantity == 0m);

        (await context.Set<ValueEntry>().CountAsync(e => e.SourceCode == "BINMOVE"))
            .ShouldBe(0, "no value was created and none consumed");
    }

    /// <summary>Moving more than is on the shelf is refused.</summary>
    [Fact]
    public async Task More_than_is_on_the_shelf_is_refused()
    {
        using var context = NewContext();

        var result = await Movements(context).PostAsync(
            "WH",
            [new BinMovementLineRequest("WIDGET", "A-01", "B-01", 50m)],
            Today);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == InventoryMessages.NotEnoughInBin);
    }

    /// <summary>
    /// Two lines drawing on the same shelf are checked against what is left, not what was there.
    /// </summary>
    /// <remarks>
    /// A sheet is one act. Checking each line against the same untouched figure would let eleven
    /// lines each take the whole shelf, and the bin would go negative on posting.
    /// </remarks>
    [Fact]
    public async Task Two_lines_off_one_shelf_are_checked_against_what_is_left()
    {
        using var context = NewContext();

        var result = await Movements(context).PostAsync(
            "WH",
            [
                new BinMovementLineRequest("WIDGET", "A-01", "B-01", 30m),
                new BinMovementLineRequest("WIDGET", "A-01", "B-01", 30m),
            ],
            Today);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == InventoryMessages.NotEnoughInBin);

        (await HeldAsync(context, _binA)).ShouldBe(40m, "and none of it moved");
    }

    /// <summary>Moving goods to the shelf they are already on is refused.</summary>
    [Fact]
    public async Task Moving_to_the_same_shelf_is_refused()
    {
        using var context = NewContext();

        var result = await Movements(context).PostAsync(
            "WH",
            [new BinMovementLineRequest("WIDGET", "A-01", "A-01", 10m)],
            Today);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == InventoryMessages.BinMovementToItself);
    }

    /// <summary>A place that does not track bins has nothing to move between.</summary>
    [Fact]
    public async Task A_place_without_bins_is_refused()
    {
        using var context = NewContext();

        var result = await Movements(context).PostAsync(
            "SHOP",
            [new BinMovementLineRequest("WIDGET", "A-01", "B-01", 10m)],
            Today);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == InventoryMessages.BinMovementWithoutBins);
    }

    /// <summary>A shelf that is not there is refused rather than created.</summary>
    [Fact]
    public async Task A_shelf_that_is_not_there_is_refused()
    {
        using var context = NewContext();

        var result = await Movements(context).PostAsync(
            "WH",
            [new BinMovementLineRequest("WIDGET", "A-01", "NOSUCH", 10m)],
            Today);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == InventoryMessages.BinNotFound);
    }

    /// <summary>
    /// One bad line refuses the whole sheet.
    /// </summary>
    /// <remarks>
    /// Somebody restocking moves eleven things at once, and posting ten of them is worse than
    /// posting none: the shelf and the record disagree and nothing says which ten went through.
    /// </remarks>
    [Fact]
    public async Task One_bad_line_refuses_the_whole_sheet()
    {
        using var context = NewContext();

        var result = await Movements(context).PostAsync(
            "WH",
            [
                new BinMovementLineRequest("WIDGET", "A-01", "B-01", 10m),
                new BinMovementLineRequest("WIDGET", "A-01", "NOSUCH", 10m),
            ],
            Today);

        result.Failed.ShouldBeTrue();

        (await HeldAsync(context, _binB)).ShouldBe(0m, "the good line did not go through either");
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private static async Task<decimal> OnHandAsync(AsapDbContext context)
        => await context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.ItemNo == "WIDGET")
            .SumAsync(static e => e.Quantity);

    private static async Task<decimal> ValueAsync(AsapDbContext context)
        => await context.Set<ValueEntry>()
            .AsNoTracking()
            .Where(e => e.ItemNo == "WIDGET")
            .SumAsync(static e => e.CostAmount);

    private static async Task<decimal> HeldAsync(AsapDbContext context, Guid binId)
        => await context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.BinId == binId)
            .SumAsync(static e => e.Quantity);

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(
            _options,
            _tenancy,
            new StubUser(),
            _clock,
            [new InventorySchema()]);

        _opened.Add(context);

        return context;
    }

    private BinMovementService Movements(AsapDbContext context)
        => new(
            context,
            new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All]),
            _numbers,
            new StubSetup(),
            new StubTransactions(),
            new LocationBranchLookup(context),
            _tenancy,
            new StubUser(),
            _clock);

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

    private sealed class StubTransactions : ITransactionNumberAllocator
    {
        private long _next;

        public Task<long> NextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(++_next);
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

        public string? UserName => "picker";

        public string? DisplayName => "Picker";

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

    private sealed class StubSetup : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((TValue)(object)"BINMOVE");

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
