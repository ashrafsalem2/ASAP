using ASAP.Modules.Hr.Entitlements;
using ASAP.Modules.Hr.People;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Accounting;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Hr.Tests;

/// <summary>
/// Covers moving what the company owes in unused leave into the ledger.
/// </summary>
/// <remarks>
/// <para>
/// HR never touches Finance's tables -- see <see cref="HrModule.DependsOn"/> and
/// <see cref="ProvisionPostingService"/>'s own remarks -- so what is checked here is HR's half of
/// the arrangement: that it asks for the right amount, on the right accounts, only when something
/// has actually moved, and refuses cleanly when the accounts are not set up. Whether Finance
/// posts a <see cref="LedgerPostingRequested"/> correctly is that module's own test suite's job.
/// </para>
/// <para>
/// The end-of-service side is deliberately absent from every posting here, and one test says so
/// outright: the payroll run charges it as it is earned. Two writers on one provision is exactly
/// the double-booking this suite exists to catch.
/// </para>
/// </remarks>
public sealed class ProvisionPostingTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000071");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000071");
    private static readonly DateOnly Today = new(2026, 8, 28);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(Today);
    private readonly List<AsapDbContext> _opened = [];

    public ProvisionPostingTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-hr-provisions-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Seed();
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new HrSchema()]);

        _opened.Add(context);

        return context;
    }

    private void Seed()
    {
        using var context = NewContext();

        // Ten years' service, comfortably past the end-of-service band change and the leave rate
        // change alike, so both provisions have something real behind them.
        context.Set<Employee>().Add(new Employee
        {
            No = "EMP-0001",
            Name = "Salim Al Harbi",
            HiredOn = Today.AddYears(-10),
            Status = EmploymentStatus.Active,
            BasicWage = 8_000m,
            Allowances = 2_000m,
        });

        context.SaveChanges();
    }

    private ProvisionPostingService Service(AsapDbContext context, FakeEvents events, StubSetup setup)
        => new(
            context,
            Employees(context, setup, _clock),
            Catalog(),
            setup,
            events,
            new StubTransactionNumbers(),
            _clock,
            NullLogger<ProvisionPostingService>.Instance);

    private static EmployeeService Employees(AsapDbContext context, StubSetup setup, IClock clock)
        => new(
            context,
            new Leave.LeaveService(
                context,
                Catalog(),
                new StubNumbers(),
                setup,
                new StubUser(),
                clock,
                NullLogger<Leave.LeaveService>.Instance),
            Catalog(),
            new StubNumbers(),
            setup,
            clock,
            NullLogger<EmployeeService>.Instance);

    private static MessageCatalog Catalog() => new([.. PlatformMessages.All, .. HrMessages.All]);

    private static StubSetup FullyConfiguredSetup() => new(new Dictionary<string, string?>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Hr.Posting.EndOfServiceAccount"] = "2500",
        ["Hr.Posting.EndOfServiceExpenseAccount"] = "6110",
        ["Hr.Posting.LeaveProvisionAccount"] = "2410",
        ["Hr.Posting.LeaveExpenseAccount"] = "6120",
    });

    [Fact]
    public async Task The_first_run_posts_the_whole_figure_as_the_movement()
    {
        using var context = NewContext();
        var events = new FakeEvents();
        var setup = FullyConfiguredSetup();

        var expected = await Employees(context, setup, _clock).EntitlementsAsync(Today);

        var expectedEndOfService = expected.Sum(e => e.EndOfService);
        var expectedLeave = expected.Sum(e => e.LeaveLiability);

        var result = await Service(context, events, setup).PostAsync(Today);

        result.Succeeded.ShouldBeTrue();
        result.Value.LeaveTotal.ShouldBe(expectedLeave);
        result.Value.LeaveMovement.ShouldBe(expectedLeave);
        result.Value.TransactionNo.ShouldNotBeNull();

        // Reported so the caller can see it, and pointedly not posted.
        result.Value.EndOfServiceTotal.ShouldBe(expectedEndOfService);

        var request = events.Published.ShouldHaveSingleItem();
        request.SourceModule.ShouldBe(HrModule.Id);
        request.Lines.Count.ShouldBe(2);
        request.Lines.Sum(l => l.Amount).ShouldBe(0m, "a posting that does not balance is a defect");

        request.Lines.Single(l => l.AccountNo == "6120").Amount.ShouldBe(expectedLeave);
        request.Lines.Single(l => l.AccountNo == "2410").Amount.ShouldBe(-expectedLeave);
    }

    [Fact]
    public async Task The_end_of_service_provision_is_left_to_payroll()
    {
        using var context = NewContext();
        var events = new FakeEvents();
        var setup = FullyConfiguredSetup();

        var result = await Service(context, events, setup).PostAsync(Today);

        result.Succeeded.ShouldBeTrue();
        result.Value.EndOfServiceTotal.ShouldBeGreaterThan(
            0m, "the employee has ten years' service, so there is something to not post");

        var request = events.Published.ShouldHaveSingleItem();

        // PayrollService charges these two per branch, in the month the service was earned. A
        // line on either of them from here would book the same liability a second time.
        request.Lines.ShouldNotContain(
            l => l.AccountNo == "2500", "payroll owns the end-of-service liability");
        request.Lines.ShouldNotContain(
            l => l.AccountNo == "6110", "payroll owns the end-of-service charge");
    }

    [Fact]
    public async Task Running_it_again_with_nothing_changed_posts_nothing()
    {
        using var first = NewContext();
        var setup = FullyConfiguredSetup();

        await Service(first, new FakeEvents(), setup).PostAsync(Today);

        using var second = NewContext();
        var events = new FakeEvents();

        var result = await Service(second, events, setup).PostAsync(Today);

        result.Succeeded.ShouldBeTrue("nothing having moved is not a failure");
        result.Value.TransactionNo.ShouldBeNull();
        result.Value.LeaveMovement.ShouldBe(0m);
        result.Messages.ShouldContain(m => m.Code.Value == "HR.PROVISION.NOTHING_TO_POST");
        events.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_later_run_posts_only_what_changed_since_the_last_one()
    {
        using var first = NewContext();
        var setup = FullyConfiguredSetup();

        var firstRun = await Service(first, new FakeEvents(), setup).PostAsync(Today);

        // A year passes and the wage rises: another year of leave is earned, and every day
        // already earned is now worth more, on top of what the first run already carried.
        using (var raise = NewContext())
        {
            var employee = await raise.Set<Employee>().SingleAsync();
            employee.BasicWage += 500m;
            await raise.SaveChangesAsync();
        }

        var laterClock = new StubClock(Today.AddYears(1));
        using var second = NewContext();
        var events = new FakeEvents();

        var later = await new ProvisionPostingService(
                second,
                Employees(second, setup, laterClock),
                Catalog(),
                setup,
                events,
                new StubTransactionNumbers(),
                laterClock,
                NullLogger<ProvisionPostingService>.Instance)
            .PostAsync(Today.AddYears(1));

        later.Succeeded.ShouldBeTrue();

        // The wage rose and a year was earned, so the total is larger -- but what was asked to be
        // posted is only the difference, not the whole new total again.
        later.Value.LeaveTotal.ShouldBeGreaterThan(firstRun.Value.LeaveTotal);
        later.Value.LeaveMovement.ShouldBe(later.Value.LeaveTotal - firstRun.Value.LeaveTotal);

        var request = events.Published.ShouldHaveSingleItem();
        request.Lines.Single(l => l.AccountNo == "2410").Amount.ShouldBe(-later.Value.LeaveMovement);
    }

    [Fact]
    public async Task A_provision_with_no_account_set_up_is_refused_rather_than_posted_short()
    {
        using var context = NewContext();
        var events = new FakeEvents();

        // The liability side is missing; the expense side is fine. Half a posting is not a
        // posting -- both halves must be resolvable or neither is asked for.
        var setup = new StubSetup(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hr.Posting.LeaveProvisionAccount"] = null,
            ["Hr.Posting.LeaveExpenseAccount"] = "6120",
        });

        var result = await Service(context, events, setup).PostAsync(Today);

        result.Failed.ShouldBeTrue();

        var refusal = result.Messages.Single(m => m.IsFailure);
        refusal.Code.Value.ShouldBe("HR.SETUP.NO_PROVISION_ACCOUNT");
        refusal.Detail.ShouldNotBeNull().ShouldContain("Hr.Posting.LeaveProvisionAccount");

        events.Published.ShouldBeEmpty("nothing should reach the ledger from a refused posting");
    }

    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private sealed class FakeEvents : IEventPublisher
    {
        public List<LedgerPostingRequested> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent asapEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            if (asapEvent is LedgerPostingRequested request)
            {
                request.WasHandled = true;
                Published.Add(request);
            }

            return Task.CompletedTask;
        }

        public Task<Result> PublishVetoableAsync<TEvent>(
            TEvent asapEvent, CancellationToken cancellationToken = default)
            where TEvent : VetoableEvent
            => throw new NotSupportedException("Provision posting raises no vetoable event.");

        public void Enqueue<TEvent>(TEvent asapEvent)
            where TEvent : IIntegrationEvent
            => throw new NotSupportedException("Provision posting raises no integration event.");
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
        public Guid? UserId { get; } = Guid.Parse("eeeeeeee-0000-0000-0000-000000000071");

        public string? UserName => "salim";

        public string? DisplayName => "Salim";

        public string? Culture => "en";

        public bool IsSuperUser => false;

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();

        public bool Has(string permissionKey) => Permissions.Contains(permissionKey);

        public Guid RequireUserId() => UserId ?? Guid.Empty;
    }

    private sealed class StubClock(DateOnly today) : IClock
    {
        public DateTime UtcNow { get; } = today.ToDateTime(TimeOnly.MinValue);

        public DateOnly Today { get; } = today;
    }

    private sealed class StubNumbers : INumberSeriesService
    {
        private int _next = 1;

        public Task<Result<string>> NextAsync(
            string seriesCode, DateOnly documentDate, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success($"EMP-{_next++:0000}"));

        public Task<Result<string>> PeekAsync(
            string seriesCode, DateOnly documentDate, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success($"EMP-{_next:0000}"));

        public Task<Result> ValidateManualAsync(
            string seriesCode,
            string number,
            DateOnly documentDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }

    private sealed class StubTransactionNumbers : ITransactionNumberAllocator
    {
        private long _next = 1;

        public Task<long> NextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_next++);
    }

    private sealed class StubSetup(IReadOnlyDictionary<string, string?> values) : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(
                (TValue)(object?)(values.TryGetValue(key, out var value) ? value : null)!);

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
