using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Promotions.Offers;
using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Promotions.Tests;

/// <summary>
/// Changing an offer that already exists.
/// </summary>
/// <remarks>
/// Replacing the things an offer applies to used to empty the parent's collection, which makes EF
/// treat every target as the orphan of a required parent and then hold two opinions about the same
/// row in one save. Against SQL Server it surfaced as an update expected to affect one row
/// affecting none, and no offer with targets could be edited at all -- found by trying to correct
/// the Arabic name on a real one.
/// </remarks>
public sealed class OfferEditingTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000b9");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000b9");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up two items an offer can point at.</summary>
    public OfferEditingTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-offer-editing-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        context.Set<Item>().AddRange(
            Item("WATER", "Bottled water"),
            Item("JUICE", "Orange juice"));

        context.SaveChanges();

        static Item Item(string no, string description) => new()
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = no,
            Description = description,
            BaseUnitOfMeasure = "CASE",
            UnitPrice = 20m,
            UnitCost = 10m,
            LastDirectCost = 10m,
        };
    }

    /// <summary>An offer with targets can be edited without the save falling over.</summary>
    [Fact]
    public async Task An_offer_with_targets_can_be_changed()
    {
        await using (var context = NewContext())
        {
            (await Offers(context).SaveAsync(Offer("مياه", "WATER"))).Succeeded.ShouldBeTrue();
        }

        await using (var context = NewContext())
        {
            var again = await Offers(context).SaveAsync(Offer("مياه، ثلاثة بسعر اثنين", "WATER"));

            again.Succeeded.ShouldBeTrue();
            again.Value.NameArabic.ShouldBe("مياه، ثلاثة بسعر اثنين");
        }
    }

    /// <summary>The targets given replace the targets held, rather than adding to them.</summary>
    [Fact]
    public async Task The_targets_given_replace_the_targets_held()
    {
        await using (var context = NewContext())
        {
            await Offers(context).SaveAsync(Offer("مياه", "WATER"));
        }

        await using (var context = NewContext())
        {
            var again = await Offers(context).SaveAsync(Offer("عصير", "JUICE"));

            again.Succeeded.ShouldBeTrue();

            again.Value.Targets.Count.ShouldBe(1, "the water target went with the old sheet");
            again.Value.Targets.Single().ItemNo.ShouldBe("JUICE");
        }
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private static Offer Offer(string nameArabic, params string[] itemNos) => new()
    {
        TenantId = Tenant,
        CompanyId = Company,
        Code = "WATER-3FOR2",
        Name = "Three for two",
        NameArabic = nameArabic,
        Kind = OfferKind.Percentage,
        Scope = OfferScope.Item,
        Value = 10m,
        StartsOn = new DateOnly(2026, 1, 1),
        Targets =
        [
            .. itemNos.Select(static no => new OfferTarget
            {
                TenantId = Tenant,
                CompanyId = Company,
                ItemNo = no,
            }),
        ],
    };

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

    private OfferService Offers(AsapDbContext context)
        => new(
            context,
            new MessageCatalog([.. PlatformMessages.All, .. InventoryMessages.All, .. PromotionsMessages.All]),
            new OverrideAuditor(context, _tenancy, new StubUser(), _clock),
            new StubSetup(),
            NullLogger<OfferService>.Instance);

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

    /// <summary>Never below cost, which is the setting that ships.</summary>
    private sealed class StubSetup : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((TValue)(object)0m);

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
