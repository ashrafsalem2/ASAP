using ASAP.Platform.Core.Cqrs;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Tests.Modules;
using ASAP.Platform.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ASAP.Platform.Tests.Cqrs;

[RequiresPermission("Finance", "Journal", PermissionAction.Post)]
internal sealed record PostGeneralJournalCommand(string BatchNo) : ICommand<string>;

[RequiresPermission("Finance", "Journal", PermissionAction.Read)]
internal sealed record GetJournalQuery(string BatchNo) : IQuery<string>;

[NoPermissionRequired("Every signed-in user needs their own menu.")]
internal sealed record GetMyMenuQuery : IQuery<string>;

/// <summary>Declares nothing at all, which the audit should flag.</summary>
internal sealed record UnguardedCommand : ICommand;

internal sealed class PostJournalHandler : IRequestHandler<PostGeneralJournalCommand, Result<string>>
{
    public int CallCount { get; private set; }

    public Task<Result<string>> HandleAsync(
        PostGeneralJournalCommand request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(Result<string>.Success($"GJ-2026-00042 from {request.BatchNo}"));
    }
}

internal sealed class GetJournalHandler : IRequestHandler<GetJournalQuery, string>
{
    public Task<string> HandleAsync(GetJournalQuery request, CancellationToken cancellationToken = default)
        => Task.FromResult($"journal {request.BatchNo}");
}

public sealed class PermissionBehaviorTests
{
    private sealed class Fixture
    {
        private readonly ServiceCollection _services = [];

        public StubUserContext User { get; } = new();

        public MutableTenantContext Tenancy { get; } = new()
        {
            TenantId = Guid.Parse("11111111-0000-0000-0000-000000000001"),
            CompanyId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a"),
        };

        public PostJournalHandler PostHandler { get; } = new();

        public Fixture(bool financeLicensed = true)
        {
            var catalog = financeLicensed
                ? new ModuleCatalog([new FakeModule("Finance")])
                : new ModuleCatalog([new FakeModule("Finance")], new UnlicensedCheck());

            _services.AddSingleton<IUserContext>(User);
            _services.AddSingleton<Kernel.Tenancy.ITenantContext>(Tenancy);
            _services.AddSingleton<Kernel.Modules.IModuleCatalog>(catalog);
            _services.AddSingleton<IMessageCatalog>(new MessageCatalog(PlatformMessages.All));
            _services.AddSingleton<IRequestHandler<PostGeneralJournalCommand, Result<string>>>(PostHandler);
            _services.AddSingleton<IRequestHandler<GetJournalQuery, string>, GetJournalHandler>();
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PermissionBehavior<,>));
        }

        private sealed class UnlicensedCheck : IModuleLicenseCheck
        {
            public bool IsLicensed(string licenseFeature, Guid? tenantId) => false;
        }

        public Dispatcher Build() => new(_services.BuildServiceProvider());
    }

    public PermissionBehaviorTests()
    {
        // Grant nothing by default; each test grants what it needs.
    }

    private static void Grant(StubUserContext user, params string[] permissions)
        => user.Permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task Runs_the_handler_when_the_caller_holds_the_permission()
    {
        var fixture = new Fixture();
        Grant(fixture.User, "Finance.Journal.Post");

        var result = await fixture.Build().SendAsync(new PostGeneralJournalCommand("GJ-BATCH-1"));

        result.Succeeded.ShouldBeTrue();
        result.Value.ShouldContain("GJ-2026-00042");
        fixture.PostHandler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Refuses_before_the_handler_runs()
    {
        // The refusal must cost nothing. Nothing should open a transaction or read a row for a
        // request that was going to be turned away.
        var fixture = new Fixture();

        await Should.ThrowAsync<AsapMessageException>(
            () => fixture.Build().SendAsync(new PostGeneralJournalCommand("GJ-BATCH-1")));

        fixture.PostHandler.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_refusal_names_the_permission_and_says_how_to_get_it()
    {
        var fixture = new Fixture();

        var thrown = await Should.ThrowAsync<AsapMessageException>(
            () => fixture.Build().SendAsync(new PostGeneralJournalCommand("GJ-BATCH-1")));

        var message = thrown.AsapMessage;
        message.Code.Value.ShouldBe("SEC.PERMISSION.DENIED");
        message.Severity.ShouldBe(MessageSeverity.Blocked);
        message.Detail.ShouldNotBeNull().ShouldContain("Finance.Journal.Post");
        message.Resolution.ShouldNotBeNull().ShouldContain("Ask an administrator");
        thrown.IsPermissionFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task The_refusal_describes_the_operation_in_words()
    {
        // "Post general journal", not "PostGeneralJournalCommand". The person reading this is
        // often a manager deciding whether to grant the permission.
        var fixture = new Fixture();

        var thrown = await Should.ThrowAsync<AsapMessageException>(
            () => fixture.Build().SendAsync(new PostGeneralJournalCommand("GJ-BATCH-1")));

        thrown.AsapMessage.Detail.ShouldNotBeNull().ShouldContain("Post general journal");
    }

    [Fact]
    public async Task An_unlicensed_module_is_reported_as_licensing_not_as_permission()
    {
        // Telling someone they lack a permission for a module the organisation never bought
        // sends them to an administrator who cannot help them.
        var fixture = new Fixture(financeLicensed: false);
        Grant(fixture.User, "Finance.Journal.Post");

        var thrown = await Should.ThrowAsync<AsapMessageException>(
            () => fixture.Build().SendAsync(new PostGeneralJournalCommand("GJ-BATCH-1")));

        thrown.AsapMessage.Code.Value.ShouldBe("SEC.MODULE.NOT_LICENSED");
        thrown.AsapMessage.Detail.ShouldNotBeNull().ShouldContain("Finance");
    }

    [Fact]
    public async Task A_super_user_passes_every_check()
    {
        var fixture = new Fixture();
        fixture.User.IsSuperUser = true;

        var result = await fixture.Build().SendAsync(new PostGeneralJournalCommand("GJ-BATCH-1"));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Guards_a_query_the_same_way_as_a_command()
    {
        // A query returns a report rather than a Result, so there is nowhere in its return type
        // to carry a refusal. Throwing keeps the behaviour identical either way.
        var fixture = new Fixture();

        await Should.ThrowAsync<AsapMessageException>(
            () => fixture.Build().SendAsync(new GetJournalQuery("GJ-BATCH-1")));

        Grant(fixture.User, "Finance.Journal.Read");

        (await fixture.Build().SendAsync(new GetJournalQuery("GJ-BATCH-1"))).ShouldBe("journal GJ-BATCH-1");
    }

    [Fact]
    public async Task Reports_a_missing_handler_clearly()
    {
        var fixture = new Fixture();
        fixture.User.IsSuperUser = true;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => fixture.Build().SendAsync(new UnguardedCommand()));

        thrown.Message.ShouldContain("No handler is registered");
    }
}

public sealed class PermissionAuditTests
{
    [Fact]
    public void Reports_the_permission_a_request_declares()
    {
        var report = PermissionAudit.Describe(typeof(PostGeneralJournalCommand));

        report.RequiredPermissions.ShouldBe(["Finance.Journal.Post"]);
        report.IsUndeclared.ShouldBeFalse();
    }

    [Fact]
    public void A_deliberately_open_request_is_not_flagged()
    {
        var report = PermissionAudit.Describe(typeof(GetMyMenuQuery));

        report.RequiredPermissions.ShouldBeEmpty();
        report.DeliberatelyOpenReason.ShouldNotBeNull().ShouldContain("own menu");
        report.IsUndeclared.ShouldBeFalse();
    }

    [Fact]
    public void A_request_that_declares_nothing_is_flagged()
    {
        // The weakness of declarative permissions is that forgetting the attribute leaves an
        // operation open and nothing complains. This is what closes that gap.
        PermissionAudit.Describe(typeof(UnguardedCommand)).IsUndeclared.ShouldBeTrue();
    }

    [Fact]
    public void Finds_every_request_in_an_assembly()
    {
        var reports = PermissionAudit.AuditAll([typeof(PostGeneralJournalCommand).Assembly]);

        reports.ShouldContain(r => r.RequestType == typeof(PostGeneralJournalCommand));
        reports.ShouldContain(r => r.RequestType == typeof(GetJournalQuery));
        reports.ShouldContain(r => r.RequestType == typeof(UnguardedCommand));
    }

    [Fact]
    public void Orders_the_audit_the_same_way_every_run()
    {
        var first = PermissionAudit.AuditAll([typeof(PostGeneralJournalCommand).Assembly]);
        var second = PermissionAudit.AuditAll([typeof(PostGeneralJournalCommand).Assembly]);

        first.Select(static r => r.RequestType.FullName)
             .ShouldBe(second.Select(static r => r.RequestType.FullName));
    }
}
