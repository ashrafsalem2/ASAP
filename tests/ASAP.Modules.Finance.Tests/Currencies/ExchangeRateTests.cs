using ASAP.Modules.Finance.Currencies;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Currencies;

/// <summary>
/// Covers what a currency was worth on a day, and every refusal to guess.
/// </summary>
/// <remarks>
/// The refusals matter more than the arithmetic. Converting at the wrong rate produces books that
/// balance and are wrong, which nothing downstream will ever catch, so every case where the right
/// rate is not knowable has to stop rather than approximate.
/// </remarks>
public sealed class ExchangeRateTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000091");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000091");

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    public ExchangeRateTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-fx-{Guid.CreateVersion7()}")
            .Options;

        Seed();
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new FinanceSchema()]);
        _opened.Add(context);
        return context;
    }

    private ExchangeRateService Service(AsapDbContext context)
        => new(context, new MessageCatalog(FinanceMessages.All));

    private void Seed()
    {
        using var context = NewContext();

        context.Set<Currency>().Add(new Currency
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "USD",
            Name = "US dollar",
            Rates =
            [
                Rate(new DateOnly(2026, 1, 1), 1m, 3.75m),
                Rate(new DateOnly(2026, 6, 1), 1m, 3.80m),
            ],
        });

        // Quoted per hundred, which is the whole reason the rate is a pair rather than a number.
        context.Set<Currency>().Add(new Currency
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "JPY",
            Name = "Japanese yen",
            DecimalPlaces = 0,
            Rates = [Rate(new DateOnly(2026, 1, 1), 100m, 2.53m)],
        });

        context.Set<Currency>().Add(new Currency
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "GBP",
            Name = "Pound sterling",
            IsActive = false,
            Rates = [Rate(new DateOnly(2026, 1, 1), 1m, 4.75m)],
        });

        context.SaveChanges();

        static ExchangeRate Rate(DateOnly from, decimal currencyAmount, decimal baseAmount)
            => new()
            {
                TenantId = Tenant,
                CompanyId = Company,
                StartingDate = from,
                CurrencyAmount = currencyAmount,
                BaseAmount = baseAmount,
            };
    }

    [Fact]
    public async Task The_rate_in_force_is_the_latest_that_had_started_not_the_newest_on_file()
    {
        await using var context = NewContext();

        // March is after the January rate and before the June one. A system that took the newest
        // row would restate every March invoice the moment June's rate was entered.
        var march = await Service(context).RateOnAsync("USD", new DateOnly(2026, 3, 15));

        march.Succeeded.ShouldBeTrue();
        march.Value.Multiplier.ShouldBe(3.75m);
        march.Value.ToBase(1_000m).ShouldBe(3_750m);

        var july = await Service(context).RateOnAsync("USD", new DateOnly(2026, 7, 1));

        july.Value.Multiplier.ShouldBe(3.80m);
        july.Value.ToBase(1_000m).ShouldBe(3_800m);
    }

    [Fact]
    public async Task A_rate_quoted_per_hundred_converts_the_hundred_not_the_one()
    {
        await using var context = NewContext();

        var rate = await Service(context).RateOnAsync("JPY", new DateOnly(2026, 6, 1));

        rate.Succeeded.ShouldBeTrue();
        rate.Value.ToBase(100_000m).ShouldBe(2_530m);

        // And the currency's own places are respected: a fraction of a yen is not a thing anybody
        // can pay.
        rate.Value.Round(1_234.56m).ShouldBe(1_235m);
    }

    [Fact]
    public async Task A_day_before_any_rate_started_is_refused_rather_than_guessed()
    {
        await using var context = NewContext();

        var result = await Service(context).RateOnAsync("USD", new DateOnly(2025, 12, 31));

        result.Failed.ShouldBeTrue(
            "there is no rate for that day, and the nearest one is not the right one");

        var refusal = result.Messages.Single(m => m.IsFailure);
        refusal.Code.Value.ShouldBe("FIN.CURRENCY.NO_RATE");
        refusal.Detail.ShouldNotBeNull().ShouldContain("USD");
        refusal.Resolution.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_currency_the_company_does_not_have_is_named_in_the_refusal()
    {
        await using var context = NewContext();

        var result = await Service(context).RateOnAsync("EUR", new DateOnly(2026, 6, 1));

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("FIN.CURRENCY.NOT_FOUND");
    }

    [Fact]
    public async Task A_withdrawn_currency_is_refused_separately_from_a_missing_one()
    {
        await using var context = NewContext();

        // It has a perfectly good rate. The refusal is about the currency, not the rate, and
        // saying "no rate" would send somebody to enter one that already exists.
        var result = await Service(context).RateOnAsync("GBP", new DateOnly(2026, 6, 1));

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("FIN.CURRENCY.BLOCKED");
    }

    [Fact]
    public async Task Resolving_several_at_once_reports_every_one_that_is_missing()
    {
        await using var context = NewContext();

        var (rates, found) = await Service(context).ResolveAsync(
        [
            ("USD", new DateOnly(2026, 6, 1)),
            ("EUR", new DateOnly(2026, 6, 1)),
            ("CHF", new DateOnly(2026, 6, 1)),
        ]);

        rates.Count.ShouldBe(1);

        // Both, not the first. Somebody who entered three currencies and has rates for one should
        // be told about both gaps once, rather than fix one and be told about the other.
        found.Count(m => m.IsFailure).ShouldBe(2);
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
