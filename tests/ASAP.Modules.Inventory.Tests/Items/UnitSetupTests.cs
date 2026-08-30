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
/// Covers setting up what a company measures in, and what one item's box holds.
/// </summary>
/// <remarks>
/// Every refusal here exists because the alternative is silent. A duplicate barcode makes a scan
/// return whichever row the database reached first; a factor of nought reads as a clean zero on
/// every report. Neither looks like an error until somebody counts a shelf.
/// </remarks>
public sealed class UnitSetupTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000e2");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000e2");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    public UnitSetupTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-unit-setup-{Guid.CreateVersion7()}")
            .Options;

        using var context = NewContext();

        context.Set<UnitOfMeasure>().AddRange(
            Measure("EACH", 0),
            Measure("BOX", 0),
            Measure("KG", 3));

        context.Set<Item>().AddRange(
            new Item
            {
                TenantId = Tenant,
                CompanyId = Company,
                No = "BEANS",
                Description = "Baked beans, 400g",
                BaseUnitOfMeasure = "EACH",
                Barcode = "5000000000001",
            },
            new Item
            {
                TenantId = Tenant,
                CompanyId = Company,
                No = "SOUP",
                Description = "Tomato soup, 400g",
                BaseUnitOfMeasure = "EACH",
            });

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
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new InventorySchema()]);
        _opened.Add(context);
        return context;
    }

    private UnitSetupService Service(AsapDbContext context)
        => new(context, new MessageCatalog(InventoryMessages.All), _tenancy);

    [Fact]
    public async Task A_box_can_be_told_what_it_holds()
    {
        await using var context = NewContext();

        var saved = await Service(context)
            .SaveItemUnitAsync("BEANS", new ItemUnitRequest("BOX", 12m, "5000000000012"));

        saved.Succeeded.ShouldBeTrue();
        saved.Value.QuantityPerUnit.ShouldBe(12m);

        var scanned = await new UnitConversionService(context, new MessageCatalog(InventoryMessages.All))
            .ScanAsync("5000000000012");

        scanned.Succeeded.ShouldBeTrue();
        scanned.Value.BaseQuantity.ShouldBe(12m);
    }

    [Fact]
    public async Task Saying_it_twice_changes_it_rather_than_adding_a_second()
    {
        await using var context = NewContext();
        var setup = Service(context);

        await setup.SaveItemUnitAsync("BEANS", new ItemUnitRequest("BOX", 12m));

        // A supplier repacked. The old row must not survive beside the new one, because two rows
        // for one unit means a conversion that depends on which was read.
        var again = await setup.SaveItemUnitAsync("BEANS", new ItemUnitRequest("BOX", 6m));

        again.Succeeded.ShouldBeTrue();

        var rows = await context.Set<ItemUnit>().Where(u => u.UnitCode == "BOX").ToListAsync();

        rows.Count.ShouldBe(1);
        rows[0].QuantityPerUnit.ShouldBe(6m);
    }

    [Fact]
    public async Task A_barcode_another_item_already_carries_is_refused()
    {
        await using var context = NewContext();

        // BEANS carries this one on the item itself. Letting SOUP take it would make the scan
        // return whichever the database reached first.
        var saved = await Service(context)
            .SaveItemUnitAsync("SOUP", new ItemUnitRequest("BOX", 12m, "5000000000001"));

        saved.Failed.ShouldBeTrue();

        var refusal = saved.Messages.Single(m => m.IsFailure);
        refusal.Code.Value.ShouldBe("INV.BARCODE.IN_USE");
        refusal.Detail.ShouldNotBeNull().ShouldContain("BEANS");
    }

    [Fact]
    public async Task A_barcode_another_unit_already_carries_is_refused()
    {
        await using var context = NewContext();
        var setup = Service(context);

        await setup.SaveItemUnitAsync("BEANS", new ItemUnitRequest("BOX", 12m, "5000000000012"));

        var saved = await setup.SaveItemUnitAsync("SOUP", new ItemUnitRequest("BOX", 6m, "5000000000012"));

        saved.Failed.ShouldBeTrue();
        saved.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.BARCODE.IN_USE");
    }

    [Fact]
    public async Task Keeping_its_own_barcode_is_not_a_clash_with_itself()
    {
        await using var context = NewContext();
        var setup = Service(context);

        await setup.SaveItemUnitAsync("BEANS", new ItemUnitRequest("BOX", 12m, "5000000000012"));

        var again = await setup.SaveItemUnitAsync("BEANS", new ItemUnitRequest("BOX", 6m, "5000000000012"));

        again.Succeeded.ShouldBeTrue();
        again.Value.QuantityPerUnit.ShouldBe(6m);
    }

    [Fact]
    public async Task A_unit_the_company_never_agreed_on_is_refused()
    {
        await using var context = NewContext();

        // Free text here is how one company ends up with CTN, CARTON and CASE all meaning the
        // same thing and none of them adding up.
        var saved = await Service(context)
            .SaveItemUnitAsync("BEANS", new ItemUnitRequest("CTN", 12m));

        saved.Failed.ShouldBeTrue();
        saved.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.UNIT.NOT_IN_LIST");
    }

    [Fact]
    public async Task The_base_unit_cannot_be_told_it_holds_something_else()
    {
        await using var context = NewContext();

        // Twelve EACH in one EACH would make every stock figure BEANS has wrong by twelve.
        var saved = await Service(context)
            .SaveItemUnitAsync("BEANS", new ItemUnitRequest("EACH", 12m));

        saved.Failed.ShouldBeTrue();
        saved.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.UNIT.BASE_NOT_ONE");
    }

    [Fact]
    public async Task The_base_unit_may_be_written_down_as_one()
    {
        await using var context = NewContext();

        var saved = await Service(context)
            .SaveItemUnitAsync("BEANS", new ItemUnitRequest("EACH", 1m));

        saved.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_box_that_holds_nothing_is_refused()
    {
        await using var context = NewContext();

        var saved = await Service(context)
            .SaveItemUnitAsync("BEANS", new ItemUnitRequest("BOX", 0m));

        saved.Failed.ShouldBeTrue();
        saved.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.UNIT.FACTOR_UNUSABLE");
    }

    [Fact]
    public async Task More_precision_than_a_quantity_holds_is_refused()
    {
        await using var context = NewContext();

        var saved = await Service(context)
            .SaveUnitAsync(new UnitRequest("TONNE", "Tonne", "طن", 9));

        saved.Failed.ShouldBeTrue();
        saved.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.UNIT.PLACES_OUT_OF_RANGE");
    }

    [Fact]
    public async Task A_unit_with_no_code_is_refused()
    {
        await using var context = NewContext();

        var saved = await Service(context)
            .SaveUnitAsync(new UnitRequest("   ", "Nameless", null, 0));

        saved.Failed.ShouldBeTrue();
        saved.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.UNIT.CODE_REQUIRED");
    }

    [Fact]
    public async Task Adding_a_unit_makes_it_choosable_on_an_item()
    {
        await using var context = NewContext();
        var setup = Service(context);

        await setup.SaveUnitAsync(new UnitRequest("PALLET", "Pallet", "منصة", 0));

        var saved = await setup.SaveItemUnitAsync("BEANS", new ItemUnitRequest("PALLET", 720m));

        saved.Succeeded.ShouldBeTrue();
        saved.Value.QuantityPerUnit.ShouldBe(720m);
    }

    [Fact]
    public async Task Removing_a_unit_stops_it_being_chosen_again()
    {
        await using var context = NewContext();
        var setup = Service(context);

        await setup.SaveItemUnitAsync("BEANS", new ItemUnitRequest("BOX", 12m));

        var removed = await setup.RemoveItemUnitAsync("BEANS", "BOX");

        removed.Succeeded.ShouldBeTrue();

        var converted = await new UnitConversionService(context, new MessageCatalog(InventoryMessages.All))
            .ConvertAsync("BEANS", "BOX", 1m);

        converted.Failed.ShouldBeTrue();
        converted.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.UNIT.NOT_SET_UP");
    }

    [Fact]
    public async Task Removing_one_the_item_never_had_says_so()
    {
        await using var context = NewContext();

        var removed = await Service(context).RemoveItemUnitAsync("BEANS", "BOX");

        removed.Failed.ShouldBeTrue();
        removed.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("INV.UNIT.NOT_SET_UP");
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
}
