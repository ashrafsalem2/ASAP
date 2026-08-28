using ASAP.Modules.Hr.Entitlements;
using ASAP.Modules.Hr.People;
using ASAP.Platform.Core.Messaging;
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
/// Covers recording leave as it is actually taken.
/// </summary>
/// <remarks>
/// Before <see cref="LeaveRegisterService"/> existed,
/// <see cref="EmployeeService.EntitlementsAsync"/> computed everybody's balance as though nobody
/// had ever taken a day. What matters here is that a record is refused when it cannot be true --
/// backwards, outside employment, or double-booked -- and that once written, it actually reaches
/// the balance <see cref="EmployeeService.EntitlementsAsync"/> reports.
/// </remarks>
public sealed class LeaveRegisterTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000081");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000081");
    private static readonly DateOnly Today = new(2026, 8, 28);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(Today);
    private readonly List<AsapDbContext> _opened = [];

    public LeaveRegisterTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-hr-leave-{Guid.CreateVersion7()}")
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

        // Six years' service: past the five-year band change, so a full year comfortably earns
        // thirty days and there is plenty of balance to test against.
        context.Set<Employee>().Add(new Employee
        {
            No = "EMP-0001",
            Name = "Salim Al Harbi",
            HiredOn = Today.AddYears(-6),
            Status = EmploymentStatus.Active,
            BasicWage = 8_000m,
            Allowances = 2_000m,
        });

        context.SaveChanges();
    }

    private static MessageCatalog Catalog() => new([.. PlatformMessages.All, .. HrMessages.All]);

    private LeaveRegisterService Service(AsapDbContext context)
        => new(
            context,
            new EmployeeService(
                context, Catalog(), new StubNumbers(), new StubSetup(), _clock,
                NullLogger<EmployeeService>.Instance),
            Catalog(),
            NullLogger<LeaveRegisterService>.Instance);

    [Fact]
    public async Task A_period_inside_employment_is_recorded()
    {
        using var context = NewContext();

        var result = await Service(context)
            .RecordAsync("EMP-0001", Today.AddDays(-10), Today.AddDays(-6));

        result.Succeeded.ShouldBeTrue();
        result.Value.Days.ShouldBe(5m);
        result.Messages.ShouldNotContain(m => m.Code.Value == "HR.LEAVE.EXCEEDS_BALANCE");
    }

    [Fact]
    public async Task The_last_day_cannot_come_before_the_first()
    {
        using var context = NewContext();

        var result = await Service(context)
            .RecordAsync("EMP-0001", Today, Today.AddDays(-1));

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("HR.LEAVE.DATES_BACKWARDS");
    }

    [Fact]
    public async Task Leave_before_hiring_is_refused()
    {
        using var context = NewContext();

        var result = await Service(context)
            .RecordAsync("EMP-0001", Today.AddYears(-7), Today.AddYears(-7).AddDays(3));

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("HR.LEAVE.OUTSIDE_EMPLOYMENT");
    }

    [Fact]
    public async Task Leave_after_somebody_left_is_refused()
    {
        using var context = NewContext();

        var employee = await context.Set<Employee>().SingleAsync();
        employee.LeftOn = Today.AddDays(-30);
        employee.Status = EmploymentStatus.Left;
        await context.SaveChangesAsync();

        var result = await Service(context)
            .RecordAsync("EMP-0001", Today.AddDays(-10), Today.AddDays(-5));

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("HR.LEAVE.OUTSIDE_EMPLOYMENT");
    }

    [Fact]
    public async Task A_second_record_cannot_share_a_day_with_the_first()
    {
        using var context = NewContext();
        var service = Service(context);

        (await service.RecordAsync("EMP-0001", Today.AddDays(-10), Today.AddDays(-6))).Succeeded
            .ShouldBeTrue();

        var overlapping = await service.RecordAsync("EMP-0001", Today.AddDays(-8), Today.AddDays(-3));

        overlapping.Failed.ShouldBeTrue();

        var refusal = overlapping.Messages.Single(m => m.IsFailure);
        refusal.Code.Value.ShouldBe("HR.LEAVE.OVERLAPS");
        refusal.Detail.ShouldNotBeNull().ShouldContain("EMP-0001");
    }

    [Fact]
    public async Task Adjoining_periods_do_not_overlap()
    {
        // The day after one ends is fair game for the next -- a boundary is not a shared day.
        using var context = NewContext();
        var service = Service(context);

        (await service.RecordAsync("EMP-0001", Today.AddDays(-10), Today.AddDays(-6))).Succeeded
            .ShouldBeTrue();

        var adjoining = await service.RecordAsync("EMP-0001", Today.AddDays(-5), Today.AddDays(-3));

        adjoining.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Taking_more_than_the_current_balance_warns_without_blocking()
    {
        using var context = NewContext();
        var service = Service(context);

        // Six years at thirty days a year is roughly a hundred and eighty days earned; ask for
        // comfortably more than that in one record.
        var result = await service.RecordAsync("EMP-0001", Today.AddDays(-400), Today);

        result.Succeeded.ShouldBeTrue("a request that outruns the balance is recorded, not refused");
        result.Messages.ShouldContain(m => m.Code.Value == "HR.LEAVE.EXCEEDS_BALANCE");
        result.Messages.Single(m => m.Code.Value == "HR.LEAVE.EXCEEDS_BALANCE").Severity
            .ShouldBe(MessageSeverity.Warning);
    }

    [Fact]
    public async Task What_was_taken_reaches_the_entitlements_report()
    {
        using var context = NewContext();

        var employees = new EmployeeService(
            context, Catalog(), new StubNumbers(), new StubSetup(), _clock,
            NullLogger<EmployeeService>.Instance);

        var before = (await employees.EntitlementsAsync(Today)).Single();

        (await Service(context).RecordAsync("EMP-0001", Today.AddDays(-10), Today.AddDays(-6)))
            .Succeeded.ShouldBeTrue();

        var after = (await employees.EntitlementsAsync(Today)).Single();

        after.LeaveDays.ShouldBe(before.LeaveDays - 5m);
        after.LeaveLiability.ShouldBeLessThan(before.LeaveLiability);

        // Leave taken changes nothing about what the company would owe on the way out -- the two
        // are unrelated liabilities.
        after.EndOfService.ShouldBe(before.EndOfService);
    }

    [Fact]
    public async Task Recording_leave_for_nobody_is_refused_rather_than_silently_ignored()
    {
        using var context = NewContext();

        var result = await Service(context).RecordAsync("EMP-9999", Today.AddDays(-2), Today);

        result.Failed.ShouldBeTrue();
        result.Messages.Single(m => m.IsFailure).Code.Value.ShouldBe("HR.EMPLOYEE.NOT_FOUND");
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
        public Guid? TenantId { get; set; }

        public Guid? CompanyId { get; set; }

        public Guid? BranchId { get; set; }

        public bool IsCrossTenantOperation { get; set; }

        public Guid RequireTenantId() => TenantId ?? Guid.Empty;

        public Guid RequireCompanyId() => CompanyId ?? Guid.Empty;
    }

    private sealed class StubUser : IUserContext
    {
        public Guid? UserId { get; } = Guid.Parse("eeeeeeee-0000-0000-0000-000000000081");

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
        public Task<Result<string>> NextAsync(
            string seriesCode, DateOnly documentDate, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success("EMP-0001"));

        public Task<Result<string>> PeekAsync(
            string seriesCode, DateOnly documentDate, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success("EMP-0001"));

        public Task<Result> ValidateManualAsync(
            string seriesCode,
            string number,
            DateOnly documentDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }

    private sealed class StubSetup : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((TValue)(object)"EMP");

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
