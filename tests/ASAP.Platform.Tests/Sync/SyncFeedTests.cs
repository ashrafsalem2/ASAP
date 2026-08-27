using ASAP.Platform.Core.Numbering;
using ASAP.Platform.Core.Sync;
using ASAP.Platform.Kernel.Sync;
using ASAP.Platform.Persistence;
using ASAP.Platform.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Platform.Tests.Sync;

/// <summary>
/// Covers the contract a branch relies on to keep selling when the line is down.
/// </summary>
/// <remarks>
/// Three properties, and a shop breaks if any of them fails. The feed is ordered, so an item
/// created and then blocked is never applied the other way round. It is resumable, so a branch
/// that has been off for a week asks from where it left off. And a push is idempotent, so a till
/// whose connection dropped mid-post can retry without anybody working out whether the first
/// attempt landed.
/// </remarks>
public sealed class SyncFeedTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000041");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000004a");
    private static readonly Guid Jeddah = Guid.Parse("dddddddd-0000-0000-0000-00000000004d");
    private static readonly Guid Riyadh = Guid.Parse("dddddddd-0000-0000-0000-00000000004e");

    private readonly TestContextHarness _harness = new();

    public SyncFeedTests()
    {
        _harness.Tenancy.TenantId = Tenant;
        _harness.Tenancy.CompanyId = Company;
        _harness.User.UserId = Guid.Parse("eeeeeeee-0000-0000-0000-00000000004e");
    }

    private SyncService Service(AsapDbContext context)
        => new(context, _harness.Tenancy, _harness.Clock, NullLogger<SyncService>.Instance);

    /// <summary>
    /// Writes a change straight to the feed.
    /// </summary>
    /// <remarks>
    /// The in-memory provider does not generate sequences, so they are assigned here. What is
    /// under test is the protocol over the feed, not the database's identity column.
    /// </remarks>
    private void Publish(AsapDbContext context, long sequence, string entityType, string displayNo,
        SyncOperation operation = SyncOperation.Upsert)
    {
        context.SyncChanges.Add(new SyncChange
        {
            TenantId = Tenant,
            CompanyId = Company,
            Sequence = sequence,
            EntityType = entityType,
            EntityId = Guid.CreateVersion7(),
            DisplayNo = displayNo,
            Operation = operation,
            OccurredAtUtc = _harness.Clock.UtcNow,
        });
    }

    [Fact]
    public async Task The_feed_comes_back_in_order()
    {
        // Applied out of order, an item created and then blocked ends up on sale.
        using var context = _harness.NewContext();

        Publish(context, 3, "Inventory.Item", "ITEM-1001", SyncOperation.Delete);
        Publish(context, 1, "Inventory.Item", "ITEM-1001");
        Publish(context, 2, "Finance.TaxCode", "VAT");

        await context.SaveChangesAsync();

        var page = await Service(context).PullAsync(since: 0);

        page.Changes.Select(static c => c.Sequence).ShouldBe([1, 2, 3]);
        page.Changes[^1].Operation.ShouldBe(SyncOperation.Delete);
    }

    [Fact]
    public async Task A_branch_asks_from_where_it_left_off()
    {
        using var context = _harness.NewContext();

        for (var i = 1; i <= 5; i++)
        {
            Publish(context, i, "Inventory.Item", $"ITEM-{i}");
        }

        await context.SaveChangesAsync();

        var page = await Service(context).PullAsync(since: 3);

        page.Changes.Select(static c => c.Sequence).ShouldBe([4, 5]);
        page.Cursor.ShouldBe(5);
        page.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task A_branch_that_is_up_to_date_still_learns_where_the_feed_has_got_to()
    {
        // Empty page, and the cursor comes back rather than zero. A branch that read zero here
        // would ask for the whole feed again on its next round.
        using var context = _harness.NewContext();

        Publish(context, 7, "Inventory.Item", "ITEM-1");
        await context.SaveChangesAsync();

        var page = await Service(context).PullAsync(since: 7);

        page.Changes.ShouldBeEmpty();
        page.Cursor.ShouldBe(7);
    }

    [Fact]
    public async Task A_long_absence_comes_back_in_pages()
    {
        // A month off is thousands of changes. One response carrying all of them is a response
        // that times out and is retried, forever.
        using var context = _harness.NewContext();

        for (var i = 1; i <= 10; i++)
        {
            Publish(context, i, "Inventory.Item", $"ITEM-{i}");
        }

        await context.SaveChangesAsync();

        var first = await Service(context).PullAsync(since: 0, pageSize: 4);

        first.Changes.Count.ShouldBe(4);
        first.HasMore.ShouldBeTrue();
        first.Cursor.ShouldBe(4);

        var second = await Service(context).PullAsync(since: first.Cursor, pageSize: 4);

        second.Changes.Select(static c => c.Sequence).ShouldBe([5, 6, 7, 8]);
    }

    [Fact]
    public async Task The_cursor_never_runs_past_what_was_returned()
    {
        // A branch that crashes between reading a page and applying it asks for the same page
        // again, which is exactly what should happen.
        using var context = _harness.NewContext();

        for (var i = 1; i <= 6; i++)
        {
            Publish(context, i, "Inventory.Item", $"ITEM-{i}");
        }

        await context.SaveChangesAsync();

        var page = await Service(context).PullAsync(since: 0, pageSize: 2);

        page.Cursor.ShouldBe(2, "not 6, which is where the feed has got to");
    }

    [Fact]
    public async Task Pushing_the_same_key_twice_records_one_document()
    {
        using var context = _harness.NewContext();
        var sync = Service(context);

        var first = await sync.PushAsync(Jeddah, "till-1-000042", "Pos.Receipt", "JED-2026-000042");

        first.Accepted.ShouldBeTrue();
        first.WasReplay.ShouldBeFalse();

        // The connection dropped and the till tried again.
        var second = await sync.PushAsync(Jeddah, "till-1-000042", "Pos.Receipt", "JED-2026-000042");

        second.Accepted.ShouldBeTrue("a retry is answered, not refused");
        second.WasReplay.ShouldBeTrue();
        second.DocumentNo.ShouldBe("JED-2026-000042");

        context.SyncInbox.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Two_branches_may_choose_the_same_key()
    {
        // Keys are often counters, so two shops picking the same one is likelier than it sounds.
        using var context = _harness.NewContext();
        var sync = Service(context);

        await sync.PushAsync(Jeddah, "000001", "Pos.Receipt", "JED-2026-000001");
        var riyadh = await sync.PushAsync(Riyadh, "000001", "Pos.Receipt", "RUH-2026-000001");

        riyadh.WasReplay.ShouldBeFalse();
        context.SyncInbox.Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_document_waiting_on_master_data_is_kept_rather_than_refused()
    {
        // A branch that has been offline sells an item created this morning. Refusing it would
        // mean a sale that happened has no record, which is worse than a record that is waiting.
        using var context = _harness.NewContext();

        var result = await Service(context).PushAsync(
            Jeddah,
            "till-1-000043",
            "Pos.Receipt",
            "JED-2026-000043",
            heldReason: "ITEM-2001 is not in the catalogue at head office yet.");

        result.Accepted.ShouldBeTrue();
        result.HeldReason.ShouldNotBeNull();

        context.SyncInbox.Single().IsApplied.ShouldBeFalse();
    }

    [Fact]
    public async Task Acknowledging_never_moves_a_branch_backwards()
    {
        // A late reply from an earlier pull would otherwise make a branch ask again for changes
        // it has already applied. Harmless, and it looks exactly like a fault.
        using var context = _harness.NewContext();
        var sync = Service(context);

        await sync.AcknowledgeAsync(Jeddah, 50);
        var status = await sync.AcknowledgeAsync(Jeddah, 20);

        status.LastAppliedSequence.ShouldBe(50);
    }

    [Fact]
    public async Task Head_office_can_see_which_shops_are_behind()
    {
        using var context = _harness.NewContext();
        var sync = Service(context);

        for (var i = 1; i <= 30; i++)
        {
            Publish(context, i, "Inventory.Item", $"ITEM-{i}");
        }

        await context.SaveChangesAsync();

        await sync.AcknowledgeAsync(Jeddah, 30);
        await sync.AcknowledgeAsync(Riyadh, 10);

        var status = await sync.StatusAsync();

        status[0].BranchId.ShouldBe(Riyadh, "furthest behind first, because that is who to ring");
        status[0].Behind.ShouldBe(20);
        status.Single(s => s.BranchId == Jeddah).Behind.ShouldBe(0);
    }

    [Fact]
    public void The_registry_refuses_two_modules_claiming_one_name()
    {
        // A branch applying two different tables under one name would overwrite one with the
        // other, and the symptom would appear a long way from the cause.
        var clash = Should.Throw<InvalidOperationException>(
            () => new SyncRegistry([new Publisher("Alpha"), new Publisher("Beta")]));

        clash.Message.ShouldContain("Alpha");
        clash.Message.ShouldContain("Beta");
    }

    [Fact]
    public void An_entity_nobody_registered_does_not_synchronise()
    {
        // Which is how the audit log stays out of the feed.
        var registry = new SyncRegistry([new Publisher("Alpha")]);

        registry.Describe(typeof(NumberSeries)).ShouldBeNull();
        registry.Describe("Platform.AuditLog").ShouldBeNull();
    }

    /// <summary>A module that publishes one entity, for the registry tests.</summary>
    private sealed class Publisher(string module) : Kernel.Modules.IAsapModule, ISyncContributor
    {
        public string ModuleId => module;

        public Kernel.Messaging.LocalizedText DisplayName => new(module);

        public Kernel.Messaging.LocalizedText Description => new(module);

        public Version Version => new(1, 0, 0);

        public IReadOnlyCollection<SyncEntityDescriptor> SyncEntities =>
            [new("Test.Thing", typeof(NumberSeriesLine), SyncDirection.Down, module)];

        public void ConfigureServices(
            Microsoft.Extensions.DependencyInjection.IServiceCollection services,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
        }
    }

    public void Dispose() => _harness.Dispose();
}
