using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Core.Numbering;
using ASAP.Platform.Persistence;
using ASAP.Platform.Tests.Persistence;
using Shouldly;

namespace ASAP.Platform.Tests.Numbering;

/// <summary>
/// Covers the service that hands out document numbers.
/// </summary>
/// <remarks>
/// The formatter is tested separately and knows how to advance a number. What is tested here is
/// everything around it: which line applies on a given date, what happens at the end of a range,
/// and what the caller is told when a series cannot issue.
/// </remarks>
public sealed class NumberSeriesServiceTests : IDisposable
{
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Company = Guid.CreateVersion7();

    private readonly TestContextHarness _harness = new();

    public NumberSeriesServiceTests()
    {
        _harness.Tenancy.TenantId = Tenant;
        _harness.Tenancy.CompanyId = Company;
    }

    public void Dispose() => _harness.Dispose();

    private NumberSeriesService Service(AsapDbContext context)
        => new(context, _harness.Tenancy, new MessageCatalog(PlatformMessages.All));

    private void Seed(Action<NumberSeries> configure)
    {
        using var context = _harness.NewContext();

        var series = new NumberSeries
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "INV",
            Description = "Sales invoices",
        };

        configure(series);
        context.Set<NumberSeries>().Add(series);
        context.SaveChanges();
    }

    private static NumberSeriesLine Line(string starting, DateOnly from, string? ending = null)
        => new()
        {
            TenantId = Tenant,
            CompanyId = Company,
            StartingDate = from,
            StartingNumber = starting,
            EndingNumber = ending,
        };

    [Fact]
    public async Task The_first_number_issued_is_the_starting_number()
    {
        // Not the one after it. A series told to start at INV-2026-00001 that first issues
        // INV-2026-00002 has quietly thrown away the number its administrator chose.
        Seed(s => s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2026, 1, 1))));

        await using var context = _harness.NewContext();

        var issued = await Service(context).NextAsync("INV", new DateOnly(2026, 8, 26));

        issued.Succeeded.ShouldBeTrue();
        issued.Value.ShouldBe("INV-2026-00001");
    }

    [Fact]
    public async Task Numbers_advance_and_keep_their_width()
    {
        Seed(s => s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2026, 1, 1))));

        await using var context = _harness.NewContext();
        var service = Service(context);
        var date = new DateOnly(2026, 8, 26);

        (await service.NextAsync("INV", date)).Value.ShouldBe("INV-2026-00001");
        await context.SaveChangesAsync();

        (await service.NextAsync("INV", date)).Value.ShouldBe("INV-2026-00002");
        await context.SaveChangesAsync();

        (await service.NextAsync("INV", date)).Value.ShouldBe("INV-2026-00003");
    }

    [Fact]
    public async Task Peeking_does_not_consume_the_number()
    {
        Seed(s => s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2026, 1, 1))));

        await using var context = _harness.NewContext();
        var service = Service(context);
        var date = new DateOnly(2026, 8, 26);

        (await service.PeekAsync("INV", date)).Value.ShouldBe("INV-2026-00001");
        (await service.PeekAsync("INV", date)).Value.ShouldBe("INV-2026-00001");

        // Which is what a draft document shows. Taking it here would burn a number every time
        // somebody opened a screen and changed their mind.
        (await service.NextAsync("INV", date)).Value.ShouldBe("INV-2026-00001");
    }

    [Fact]
    public async Task The_latest_line_that_has_started_is_the_one_used()
    {
        // Dated lines are how a series restarts each January without losing last year's history.
        Seed(s =>
        {
            s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2026, 1, 1)));
            s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2027, 1, 1)));
        });

        await using var context = _harness.NewContext();
        var service = Service(context);

        (await service.NextAsync("INV", new DateOnly(2026, 12, 31))).Value.ShouldBe("INV-2026-00001");
        await context.SaveChangesAsync();

        // The 2027 line takes over on its own start date, with its own counter.
        (await service.NextAsync("INV", new DateOnly(2027, 1, 2))).Value.ShouldBe("INV-2027-00001");
    }

    [Fact]
    public async Task A_date_before_every_line_is_refused_with_a_way_forward()
    {
        Seed(s => s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2026, 1, 1))));

        await using var context = _harness.NewContext();

        var issued = await Service(context).NextAsync("INV", new DateOnly(2025, 12, 31));

        issued.Failed.ShouldBeTrue();

        var message = issued.Messages.ShouldHaveSingleItem();

        message.Code.Value.ShouldBe("PLAT.NUMBERSERIES.NO_LINE_FOR_DATE");
        message.Resolution.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_series_that_reaches_its_ceiling_stops_rather_than_running_past_it()
    {
        // A range registered with an authority or pre-printed on stationery is a promise about
        // which numbers exist. Issuing past it is worse than refusing.
        Seed(s => s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2026, 1, 1), ending: "INV-2026-00002")));

        await using var context = _harness.NewContext();
        var service = Service(context);
        var date = new DateOnly(2026, 8, 26);

        (await service.NextAsync("INV", date)).Value.ShouldBe("INV-2026-00001");
        await context.SaveChangesAsync();

        (await service.NextAsync("INV", date)).Value.ShouldBe("INV-2026-00002");
        await context.SaveChangesAsync();

        var exhausted = await service.NextAsync("INV", date);

        exhausted.Failed.ShouldBeTrue();
        exhausted.Messages.ShouldContain(m => m.Code.Value == "PLAT.NUMBERSERIES.EXHAUSTED");
    }

    [Fact]
    public async Task A_series_running_low_says_so_while_it_still_works()
    {
        Seed(s => s.Lines.Add(new NumberSeriesLine
        {
            TenantId = Tenant,
            CompanyId = Company,
            StartingDate = new DateOnly(2026, 1, 1),
            StartingNumber = "INV-{YYYY}-00001",
            EndingNumber = "INV-2026-00003",
            WarnWhenRemainingBelow = 2,
        }));

        await using var context = _harness.NewContext();

        var issued = await Service(context).NextAsync("INV", new DateOnly(2026, 8, 26));

        // Succeeds, and warns. Saying it at the moment the series stops would be saying it
        // mid-trading, which is exactly too late to be useful.
        issued.Succeeded.ShouldBeTrue();
        issued.Messages.ShouldContain(m => m.Code.Value == "PLAT.NUMBERSERIES.RUNNING_LOW");
    }

    [Fact]
    public async Task An_unknown_or_switched_off_series_is_refused_by_name()
    {
        Seed(s =>
        {
            s.IsActive = false;
            s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2026, 1, 1)));
        });

        await using var context = _harness.NewContext();
        var service = Service(context);
        var date = new DateOnly(2026, 8, 26);

        foreach (var code in new[] { "INV", "NOPE" })
        {
            var issued = await service.NextAsync(code, date);

            issued.Failed.ShouldBeTrue();
            issued.Messages.ShouldContain(m => m.Code.Value == "PLAT.NUMBERSERIES.UNAVAILABLE");
        }
    }

    [Fact]
    public async Task Date_order_is_enforced_only_where_the_series_asks_for_it()
    {
        Seed(s =>
        {
            s.EnforceDateOrder = true;
            s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2026, 1, 1)));
        });

        await using var context = _harness.NewContext();
        var service = Service(context);

        (await service.NextAsync("INV", new DateOnly(2026, 8, 26))).Succeeded.ShouldBeTrue();
        await context.SaveChangesAsync();

        var backwards = await service.NextAsync("INV", new DateOnly(2026, 8, 20));

        backwards.Failed.ShouldBeTrue();

        var message = backwards.Messages.ShouldHaveSingleItem();

        message.Code.Value.ShouldBe("PLAT.NUMBERSERIES.DATE_OUT_OF_ORDER");

        // Overridable, because a genuine back-dated correction is a real thing and somebody
        // senior enough should be able to make one.
        message.OverridePermission.ShouldBe("Platform.NumberSeries.Override");
    }

    [Fact]
    public async Task A_series_that_issues_its_own_numbers_refuses_a_typed_one()
    {
        Seed(s => s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2026, 1, 1))));

        await using var context = _harness.NewContext();

        var validated = await Service(context)
            .ValidateManualAsync("INV", "INV-2026-09999", new DateOnly(2026, 8, 26));

        validated.Failed.ShouldBeTrue();
        validated.Messages.ShouldContain(m => m.Code.Value == "PLAT.NUMBERSERIES.MANUAL_NOT_ALLOWED");
    }

    [Fact]
    public async Task A_typed_number_behind_the_counter_is_refused()
    {
        Seed(s =>
        {
            s.AllowManualEntry = true;
            s.Lines.Add(Line("INV-{YYYY}-00001", new DateOnly(2026, 1, 1)));
        });

        await using var context = _harness.NewContext();
        var service = Service(context);
        var date = new DateOnly(2026, 8, 26);

        await service.NextAsync("INV", date);
        await context.SaveChangesAsync();

        (await service.ValidateManualAsync("INV", "INV-2026-00001", date)).Failed.ShouldBeTrue();
        (await service.ValidateManualAsync("INV", "INV-2026-00500", date)).Succeeded.ShouldBeTrue();
    }
}
