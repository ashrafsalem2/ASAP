using ASAP.Modules.Hr.People;
using ASAP.Modules.Hr.Reporting;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Hr.Tests;

/// <summary>
/// Covers reporting on the staff list as a group, rather than on what any one person is owed.
/// </summary>
public sealed class HrReportingTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000091");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000091");
    private static readonly DateOnly Today = new(2026, 8, 28);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(Today);
    private readonly List<AsapDbContext> _opened = [];
    private Guid _jeddahId;
    private Guid _riyadhId;

    public HrReportingTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-hr-reporting-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Seed();
    }

    private AsapDbContext NewContext()
    {
        // Branches live in the platform schema, not HR's, but the platform's own tables register
        // themselves on every context regardless of which module schemas are passed in.
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new HrSchema()]);

        _opened.Add(context);

        return context;
    }

    private void Seed()
    {
        using var context = NewContext();

        var jeddah = new Branch { Code = "JED", Name = "Jeddah", Kind = BranchKind.Store };
        var riyadh = new Branch { Code = "RUH", Name = "Riyadh", Kind = BranchKind.Store };

        context.Set<Branch>().AddRange(jeddah, riyadh);
        context.SaveChanges();

        _jeddahId = jeddah.Id;
        _riyadhId = riyadh.Id;

        void Hire(string no, DateOnly hiredOn, decimal basicWage, decimal allowances, Guid branchId, DateOnly? leftOn = null)
        {
            var employee = new Employee
            {
                No = no,
                Name = no,
                HiredOn = hiredOn,
                LeftOn = leftOn,
                Status = leftOn is null ? EmploymentStatus.Active : EmploymentStatus.Left,
                BasicWage = basicWage,
                Allowances = allowances,
            };

            employee.BranchAssignments.Add(new BranchAssignment { BranchId = branchId, FromDate = hiredOn });

            context.Set<Employee>().Add(employee);
        }

        // Two currently at Jeddah, one at Riyadh, and one who left before today -- present for
        // turnover, absent from headcount and cost.
        Hire("EMP-0001", Today.AddYears(-3), 8_000m, 2_000m, _jeddahId);
        Hire("EMP-0002", Today.AddYears(-1), 6_000m, 1_000m, _jeddahId);
        Hire("EMP-0003", Today.AddYears(-2), 7_000m, 1_500m, _riyadhId);
        Hire("EMP-0004", Today.AddYears(-4), 5_000m, 500m, _riyadhId, leftOn: Today.AddMonths(-2));

        context.SaveChanges();
    }

    private HrReportingService Service(AsapDbContext context) => new(context, _clock);

    [Fact]
    public async Task Headcount_is_grouped_by_where_people_currently_are()
    {
        using var context = NewContext();

        var rows = await Service(context).HeadcountByBranchAsync(Today);

        rows.Single(r => r.BranchId == _jeddahId).Count.ShouldBe(2);
        rows.Single(r => r.BranchId == _riyadhId).Count.ShouldBe(1);

        // The leaver is neither branch's headcount today -- they are gone.
        rows.Sum(static r => r.Count).ShouldBe(3);
    }

    [Fact]
    public async Task Cost_by_branch_sums_the_current_wage_of_who_is_there()
    {
        using var context = NewContext();

        var rows = await Service(context).CostByBranchAsync(Today);

        rows.Single(r => r.BranchId == _jeddahId).MonthlyWageCost.ShouldBe(10_000m + 7_000m);
        rows.Single(r => r.BranchId == _riyadhId).MonthlyWageCost.ShouldBe(8_500m);
    }

    [Fact]
    public async Task Turnover_counts_who_was_here_at_each_end_and_who_moved_between()
    {
        using var context = NewContext();

        // A year-long window that catches EMP-0004 leaving but nobody being hired inside it --
        // all four hire dates fall before this window opens.
        var from = Today.AddYears(-1);
        var to = Today;

        var summary = await Service(context).TurnoverAsync(from, to);

        summary.OpeningHeadcount.ShouldBe(4, "all four were employed the day before the window opened");
        summary.Hired.ShouldBe(0);
        summary.Left.ShouldBe(1);
        summary.ClosingHeadcount.ShouldBe(3);

        // One leaver against an average of (4 + 3) / 2 = 3.5.
        summary.TurnoverRate.ShouldBe(Math.Round(1m / 3.5m, 4));
    }

    [Fact]
    public async Task A_period_nobody_left_in_has_a_turnover_rate_of_nothing()
    {
        using var context = NewContext();

        // A window entirely after EMP-0004's leaving date, so nobody in it moves at all.
        var summary = await Service(context).TurnoverAsync(Today.AddDays(-30), Today);

        summary.Left.ShouldBe(0);
        summary.TurnoverRate.ShouldBe(0m);
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
        public Guid? UserId { get; } = Guid.Parse("eeeeeeee-0000-0000-0000-000000000091");

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
}
