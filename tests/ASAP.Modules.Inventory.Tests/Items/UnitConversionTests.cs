using ASAP.Modules.Inventory.Items;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Inventory.Tests.Items;

/// <summary>
/// Covers turning what somebody scanned or keyed into the unit stock is kept in.
/// </summary>
/// <remarks>
/// The one place a unit is allowed to matter, so the one place it can go wrong. A conversion that
/// is out by a factor of twelve does not look like an error: it looks like a stock figure, and it
/// keeps looking like one until somebody counts the shelf.
/// </remarks>
public sealed class UnitConversionTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000e1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000e1");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    public UnitConversionTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-units-{Guid.CreateVersion7()}")
            .Options;

        using var context = NewContext();

        var beans = new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "BEANS",
            Description = "Baked beans, 400g",
            BaseUnitOfMeasure = "EACH",
            Barcode = "5000000000001",
        };

        var flour = new Item
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "FLOUR",
            Description = "Plain flour, loose",
            BaseUnitOfMeasure = "KG",
        };

        context.Set<Item>().AddRange(beans, flour);
        context.SaveChanges();

        // None for the things that are counted, three for the things that are weighed.
        context.Set<UnitOfMeasure>().AddRange(
            Measure("EACH", 0),
            Measure("BOX", 0),
            Measure("KG", 3));

        context.Set<ItemUnit>().AddRange(
            Unit(beans.Id, "EACH", 1m, "5000000000001"),
            Unit(beans.Id, "BOX", 12m, "5000000000012"),
            Unit(beans.Id, "PALLET", 720m),

            // A box of one item is twelve and of another is six, which is why this is a fact
            // about the item rather than about boxes.
            Unit(flour.Id, "SACK", 25m, "5000000000025"),
            Unit(flour.Id, "BROKEN", 0m));

        context.SaveChanges();

        static UnitOfMeasure Measure(string code, int places)
            => new()
            {
                TenantId = Tenant,
                CompanyId = Company,
                Code = code,
                Name = code,
                NameArabic = code,
                DecimalPlaces = places,
            };

        static ItemUnit Unit(Guid itemId, string code, decimal per, string? barcode = null)
            => new()
            {
                TenantId = Tenant,
                CompanyId = Company,
                ItemId = itemId,
                UnitCode = code,
                QuantityPerUnit = per,
                Barcode = barcode,
            };
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
        _opened.Add(context);
        return context;
    }

    private UnitConversionService Service(AsapDbContext context)
        => new(context, new MessageCatalog(InventoryMessages.All));

    [Fact]
    public async Task Scanning_a_case_adds_what_the_case_holds()
    {
        await using var context = NewContext();

        // The whole point of a barcode per unit. Falling back to the item's barcode first would
        // make every case scan add one, and nobody would notice until a stock count.
        var scanned = await Service(context).ScanAsync("5000000000012");

        scanned.Succeeded.ShouldBeTrue();
        scanned.Value.ItemNo.ShouldBe("BEANS");
        scanned.Value.UnitCode.ShouldBe("BOX");
        scanned.Value.BaseQuantity.ShouldBe(12m);
        scanned.Value.BaseUnitCode.ShouldBe("EACH");
    }

    [Fact]
    public async Task Scanning_a_single_adds_one()
    {
        await using var context = NewContext();

        var scanned = await Service(context).ScanAsync("5000000000001");

        scanned.Succeeded.ShouldBeTrue();
        scanned.Value.BaseQuantity.ShouldBe(1m);
    }

    [Fact]
    public async Task A_barcode_nothing_carries_is_refused_rather_than_guessed_at()
    {
        await using var context = NewContext();

        var scanned = await Service(context).ScanAsync("9999999999999");

        scanned.Failed.ShouldBeTrue();
        scanned.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.BARCODE.NOT_FOUND");
    }

    [Fact]
    public async Task A_quantity_keyed_in_a_larger_unit_is_stored_in_the_base_one()
    {
        await using var context = NewContext();

        // Three boxes is thirty-six tins. The ledger only ever sees thirty-six, because a stock
        // figure with mixed units in it cannot be added up.
        var converted = await Service(context).ConvertAsync("BEANS", "BOX", 3m);

        converted.Succeeded.ShouldBeTrue();
        converted.Value.Quantity.ShouldBe(3m);
        converted.Value.BaseQuantity.ShouldBe(36m);
    }

    [Fact]
    public async Task An_item_sold_only_in_its_base_unit_needs_nothing_set_up()
    {
        await using var context = NewContext();

        // Naming the base unit explicitly, on an item that has no row for it.
        var converted = await Service(context).ConvertAsync("FLOUR", "KG", 2.5m);

        converted.Succeeded.ShouldBeTrue();
        converted.Value.BaseQuantity.ShouldBe(2.5m);
    }

    [Fact]
    public async Task Naming_no_unit_at_all_means_the_base_one()
    {
        await using var context = NewContext();

        var converted = await Service(context).ConvertAsync("BEANS", null, 5m);

        converted.Succeeded.ShouldBeTrue();
        converted.Value.BaseQuantity.ShouldBe(5m);
        converted.Value.UnitCode.ShouldBe("EACH");
    }

    [Fact]
    public async Task A_unit_this_item_does_not_have_is_refused_by_name()
    {
        await using var context = NewContext();

        // Flour comes in sacks, not boxes. The refusal names the item and its base unit, because
        // "no such unit" alone sends somebody to the wrong screen.
        var converted = await Service(context).ConvertAsync("FLOUR", "BOX", 1m);

        converted.Failed.ShouldBeTrue();

        var refusal = converted.Messages.Single(m => m.IsFailure);
        refusal.Code.Value.ShouldBe("INV.UNIT.NOT_SET_UP");
        refusal.Detail.ShouldNotBeNull().ShouldContain("KG");
    }

    [Fact]
    public async Task A_unit_that_holds_nothing_is_refused_rather_than_multiplying_to_nought()
    {
        await using var context = NewContext();

        // The worst failure available here: nought reads as a clean zero on every report rather
        // than as a mistake, so it is refused at the edge instead.
        var converted = await Service(context).ConvertAsync("FLOUR", "BROKEN", 4m);

        converted.Failed.ShouldBeTrue();
        converted.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.UNIT.FACTOR_UNUSABLE");
    }

    [Fact]
    public async Task The_units_an_item_may_be_handled_in_include_its_base_one()
    {
        await using var context = NewContext();

        // Flour has a sack row and no KG row, and is still sellable by the kilo — a list that
        // omitted the base unit would refuse the commonest case.
        var units = await Service(context).UnitsAsync("FLOUR");

        units.Select(u => u.UnitCode).ShouldContain("KG");
        units[0].UnitCode.ShouldBe("KG", "the base unit comes first, because it is the default");
    }

    [Fact]
    public async Task Converting_back_from_the_base_unit_does_not_round_away_stock()
    {
        var unit = new ItemUnit { UnitCode = "BOX", QuantityPerUnit = 12m };

        // Seven tins is 0.5833 of a box. Rounding here would lose stock; the caller decides how
        // to show it, and what it shows is never what it stores.
        unit.FromBase(7m).ShouldBe(7m / 12m);

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private sealed class StubTenant : ITenantContext
    {
        public Guid? TenantId => Tenant;

        public Guid? CompanyId => Company;

        public Guid? BranchId => null;

        public bool IsCrossTenantOperation => false;

        public Guid RequireTenantId() => Tenant;

        public Guid RequireCompanyId() => Company;
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

    [Fact]
    public async Task Half_of_something_sold_one_at_a_time_is_refused()
    {
        await using var context = NewContext();

        // Not a rounding question. A till that accepts this has taken an order nobody can pick,
        // and the shortfall shows up as a picking problem rather than as a mistake at the till.
        var converted = await Service(context).ConvertAsync("BEANS", "EACH", 2.5m);

        converted.Failed.ShouldBeTrue();
        converted.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.UNIT.TOO_MANY_DECIMALS");
    }

    [Fact]
    public async Task Half_of_something_sold_by_weight_is_fine()
    {
        await using var context = NewContext();

        var converted = await Service(context).ConvertAsync("FLOUR", "KG", 2.5m);

        converted.Succeeded.ShouldBeTrue();
        converted.Value.BaseQuantity.ShouldBe(2.5m);
    }

    [Fact]
    public async Task A_weighed_unit_still_stops_somewhere()
    {
        await using var context = NewContext();

        // Three places, because that is what a scale reports. A fourth is a scale nobody has.
        var converted = await Service(context).ConvertAsync("FLOUR", "KG", 2.5001m);

        converted.Failed.ShouldBeTrue();
        converted.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.UNIT.TOO_MANY_DECIMALS");
    }

    [Fact]
    public async Task A_unit_nobody_defined_is_not_checked_for_places()
    {
        await using var context = NewContext();

        // PALLET is set up on the item but never added to the company's unit list. Refusing here
        // would turn a missing setup into a shop that cannot sell.
        var converted = await Service(context).ConvertAsync("BEANS", "PALLET", 0.5m);

        converted.Succeeded.ShouldBeTrue();
        converted.Value.BaseQuantity.ShouldBe(360m);
    }
}
