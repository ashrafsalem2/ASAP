using ASAP.Platform.Core.Numbering;
using ASAP.Platform.Core.Sync;
using ASAP.Platform.Kernel.Sync;
using ASAP.Platform.Persistence;
using ASAP.Platform.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Platform.Tests.Sync;

/// <summary>
/// Covers what reaches the feed when something is saved.
/// </summary>
/// <remarks>
/// <para>
/// Capture lives in the context rather than in each module, for the same reason tenancy stamping
/// does: a module that had to remember to publish would one day forget, and the failure is a shop
/// quietly selling at last month's prices.
/// </para>
/// <para>
/// The case that matters most here is the one that does <em>not</em> publish. Running figures sit
/// on master data for the convenience of screens and move on every posting, and a feed that
/// carried them would bury the changes a branch actually needs under the day's trading.
/// </para>
/// <para>
/// The entity under test is a number series line, because the platform test project cannot
/// reference a module and a line is the closest platform equivalent of the real thing: a
/// definition nobody changes often, carrying a counter that moves every time anybody sells
/// anything.
/// </para>
/// </remarks>
public sealed class SyncCaptureTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000051");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000005a");

    private readonly TestContextHarness _harness = new();

    public SyncCaptureTests()
    {
        _harness.Tenancy.TenantId = Tenant;
        _harness.Tenancy.CompanyId = Company;
        _harness.User.UserId = Guid.Parse("eeeeeeee-0000-0000-0000-00000000005e");
    }

    private AsapDbContext NewContext() => _harness.NewContext(new SyncRegistry([new Publisher()]));

    /// <summary>Adds a series with one line and returns the line, saved.</summary>
    private static NumberSeriesLine Line(AsapDbContext context, string startingNumber = "INV-000001")
    {
        var series = new NumberSeries { Code = "SALES-INV", Description = "Sales invoices" };

        series.Lines.Add(new NumberSeriesLine
        {
            StartingDate = new DateOnly(2026, 1, 1),
            StartingNumber = startingNumber,
        });

        context.NumberSeries.Add(series);
        context.SaveChanges();

        return series.Lines.Single();
    }

    [Fact]
    public void Creating_something_a_branch_holds_a_copy_of_reaches_the_feed()
    {
        using var context = NewContext();

        Line(context);

        var change = context.SyncChanges.Single();

        change.EntityType.ShouldBe("Test.SeriesLine");
        change.Operation.ShouldBe(SyncOperation.Upsert);
        change.CompanyId.ShouldBe(Company);
    }

    [Fact]
    public void The_series_itself_is_not_captured_because_nobody_registered_it()
    {
        // One entity registered, one change. The parent was saved in the same transaction and
        // stayed off the feed, which is how the audit log stays off it too.
        using var context = NewContext();

        Line(context);

        context.SyncChanges.Count().ShouldBe(1);
    }

    [Fact]
    public void Changing_the_definition_reaches_the_feed()
    {
        using var context = NewContext();

        var line = Line(context);

        line.EndingNumber = "INV-999999";
        context.SaveChanges();

        context.SyncChanges.Count().ShouldBe(2);
    }

    [Fact]
    public void A_running_figure_moving_does_not()
    {
        // The case this exists for. A busy day moves this on every document, and a branch holds
        // the line for its rules, not for where head office has got to.
        using var context = NewContext();

        var line = Line(context);
        var afterCreate = context.SyncChanges.Count();

        line.LastNumberUsed = "INV-004000";
        line.LastDateUsed = new DateOnly(2026, 8, 27);
        context.SaveChanges();

        context.SyncChanges.Count().ShouldBe(afterCreate, "trading is not a master data change");
    }

    [Fact]
    public void A_running_figure_moving_alongside_a_real_change_still_does()
    {
        // The rule is only that a volatile column cannot be the whole reason.
        using var context = NewContext();

        var line = Line(context);
        var afterCreate = context.SyncChanges.Count();

        line.LastNumberUsed = "INV-005000";
        line.Increment = 5;
        context.SaveChanges();

        context.SyncChanges.Count().ShouldBe(afterCreate + 1);
    }

    [Fact]
    public void An_audit_stamp_alone_is_not_a_change()
    {
        // Stamping runs immediately before capture, so ModifiedAtUtc moves on every single
        // update. Without excluding it, every row that changed for any reason looks like a row
        // whose definition changed, and the volatile list does nothing at all. This is exactly
        // what went wrong the first time the filter was tried against a real database.
        using var context = NewContext();

        var line = Line(context);
        var afterCreate = context.SyncChanges.Count();

        context.Entry(line).Property(l => l.ModifiedAtUtc).IsModified = true;
        context.SaveChanges();

        context.SyncChanges.Count().ShouldBe(afterCreate);
    }

    [Fact]
    public void Nothing_reaches_the_feed_when_nothing_is_registered()
    {
        // A deployment that does not synchronise pays nothing for the machinery.
        using var context = _harness.NewContext();

        Line(context);

        context.SyncChanges.ShouldBeEmpty();
    }

    /// <summary>A module publishing one platform entity, so capture can be tested without one.</summary>
    private sealed class Publisher : Kernel.Modules.IAsapModule, ISyncContributor
    {
        public string ModuleId => "Test";

        public Kernel.Messaging.LocalizedText DisplayName => new("Test");

        public Kernel.Messaging.LocalizedText Description => new("Test");

        public Version Version => new(1, 0, 0);

        public IReadOnlyCollection<SyncEntityDescriptor> SyncEntities =>
        [
            new(
                "Test.SeriesLine",
                typeof(NumberSeriesLine),
                SyncDirection.Down,
                "Test",
                [nameof(NumberSeriesLine.LastNumberUsed), nameof(NumberSeriesLine.LastDateUsed)]),
        ];

        public void ConfigureServices(
            Microsoft.Extensions.DependencyInjection.IServiceCollection services,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
        }
    }

    public void Dispose() => _harness.Dispose();
}
