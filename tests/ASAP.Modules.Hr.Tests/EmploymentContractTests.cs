using ASAP.Modules.Hr.Payroll;
using ASAP.Modules.Hr.People;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Hr.Tests;

/// <summary>
/// What somebody was engaged on, and what stops two answers existing for one day.
/// </summary>
/// <remarks>
/// The wage used to live on the employee, so it had exactly one value: today's. A raise in April
/// silently restated March, and re-running March's payroll paid April's figure with nothing
/// saying the number had ever changed. Everything here defends the one invariant that fixes it —
/// for any person and any day, at most one contract.
/// </remarks>
public sealed class EmploymentContractTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000e1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000e1");
    private static readonly DateOnly Hired = new(2025, 1, 1);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up one employee hired at the start of 2025.</summary>
    public EmploymentContractTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-contracts-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        context.Set<Employee>().Add(new Employee
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "EMP-0001",
            Name = "Salim Al Harbi",
            HiredOn = Hired,
            Status = EmploymentStatus.Active,
            BasicWage = 6000m,
            Allowances = 1500m,
        });

        context.SaveChanges();
    }

    /// <summary>A contract is recorded and is the one in force on its days.</summary>
    [Fact]
    public async Task A_contract_is_in_force_on_its_own_days()
    {
        using var context = NewContext();
        var contracts = Contracts(context);

        (await contracts.RecordAsync(Request(Hired, 6000m))).Succeeded.ShouldBeTrue();

        (await contracts.InForceAsync("EMP-0001", new DateOnly(2025, 6, 1)))
            .ShouldNotBeNull().BasicWage.ShouldBe(6000m);

        (await contracts.InForceAsync("EMP-0001", new DateOnly(2024, 6, 1)))
            .ShouldBeNull("it had not started");
    }

    /// <summary>
    /// Two contracts covering the same day are refused.
    /// </summary>
    /// <remarks>
    /// The invariant everything else defends. Two wages for one day means payroll pays whichever
    /// row it read first, and the difference shows up in nobody's total — only in one person's
    /// pay, which is where nobody is looking.
    /// </remarks>
    [Fact]
    public async Task Two_contracts_cannot_cover_the_same_day()
    {
        using var context = NewContext();
        var contracts = Contracts(context);

        await contracts.RecordAsync(Request(Hired, 6000m));

        var second = await contracts.RecordAsync(Request(new DateOnly(2026, 1, 1), 7000m));

        second.Failed.ShouldBeTrue();
        second.Messages.ShouldContain(m => m.Code == HrMessages.ContractOverlaps);
    }

    /// <summary>Superseding closes the old contract the day before the new one starts.</summary>
    [Fact]
    public async Task Superseding_closes_the_old_one_the_day_before()
    {
        using var context = NewContext();
        var contracts = Contracts(context);

        await contracts.RecordAsync(Request(Hired, 6000m));

        var raise = await contracts.SupersedeAsync(Request(new DateOnly(2026, 4, 1), 7000m));

        raise.Succeeded.ShouldBeTrue();

        var all = await contracts.ListAsync("EMP-0001");

        all.Count.ShouldBe(2);
        all[0].EndsOn.ShouldBe(new DateOnly(2026, 3, 31), "closed the day before, so no gap and no overlap");
        all[1].StartsOn.ShouldBe(new DateOnly(2026, 4, 1));

        (await contracts.InForceAsync("EMP-0001", new DateOnly(2026, 3, 31)))
            .ShouldNotBeNull().BasicWage.ShouldBe(6000m, "March is still on the old figure");

        (await contracts.InForceAsync("EMP-0001", new DateOnly(2026, 4, 1)))
            .ShouldNotBeNull().BasicWage.ShouldBe(7000m);
    }

    /// <summary>
    /// A refused supersede does not leave the old contract closed.
    /// </summary>
    /// <remarks>
    /// The close was only ever in aid of the new contract. Leaving it applied would end
    /// somebody's contract on the strength of a save that did not happen.
    /// </remarks>
    [Fact]
    public async Task A_refused_supersede_leaves_the_old_contract_alone()
    {
        using var context = NewContext();
        var contracts = Contracts(context);

        await contracts.RecordAsync(Request(Hired, 6000m));

        var refused = await contracts.SupersedeAsync(Request(new DateOnly(2026, 4, 1), 0m));

        refused.Failed.ShouldBeTrue();
        refused.Messages.ShouldContain(m => m.Code == HrMessages.ContractPaysNothing);

        var all = await contracts.ListAsync("EMP-0001");

        all.Count.ShouldBe(1);
        all[0].EndsOn.ShouldBeNull("nothing was superseded, so nothing was closed");
    }

    /// <summary>A contract cannot begin before the person was hired.</summary>
    [Fact]
    public async Task A_contract_cannot_start_before_hiring()
    {
        using var context = NewContext();

        var result = await Contracts(context).RecordAsync(Request(new DateOnly(2024, 6, 1), 6000m));

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == HrMessages.ContractBeforeHiring);
    }

    /// <summary>A fixed term with no end is a permanent contract somebody mislabelled.</summary>
    [Fact]
    public async Task A_fixed_term_needs_a_term()
    {
        using var context = NewContext();

        var result = await Contracts(context).RecordAsync(
            Request(Hired, 6000m) with { Kind = ContractKind.FixedTerm });

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == HrMessages.ContractHasNoEnd);
    }

    /// <summary>And a permanent contract with an end is a fixed term somebody mislabelled.</summary>
    [Fact]
    public async Task A_permanent_contract_may_not_have_an_end()
    {
        using var context = NewContext();

        var result = await Contracts(context).RecordAsync(
            Request(Hired, 6000m) with { EndsOn = new DateOnly(2026, 12, 31) });

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == HrMessages.ContractShouldNotEnd);
    }

    /// <summary>The employee record is kept showing what they are on today.</summary>
    [Fact]
    public async Task The_employee_record_follows_the_contract_in_force_today()
    {
        using var context = NewContext();

        await Contracts(context).RecordAsync(Request(Hired, 8000m, allowances: 2000m));

        var employee = await context.Set<Employee>().FirstAsync(e => e.No == "EMP-0001");

        employee.BasicWage.ShouldBe(8000m);
        employee.Allowances.ShouldBe(2000m);
    }

    /// <summary>A contract that has not started yet does not restate today's figures.</summary>
    [Fact]
    public async Task A_future_contract_does_not_restate_todays_figures()
    {
        using var context = NewContext();

        await Contracts(context).RecordAsync(Request(new DateOnly(2027, 1, 1), 9000m));

        var employee = await context.Set<Employee>().FirstAsync(e => e.No == "EMP-0001");

        employee.BasicWage.ShouldBe(6000m, "the raise has not happened yet");
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private static EmploymentContractRequest Request(
        DateOnly startsOn,
        decimal basicWage,
        decimal allowances = 1500m)
        => new("EMP-0001", startsOn, basicWage, allowances);

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new HrSchema()]);

        _opened.Add(context);

        return context;
    }

    private EmploymentContractService Contracts(AsapDbContext context)
        => new(
            context,
            new MessageCatalog([.. PlatformMessages.All, .. HrMessages.All]),
            _tenancy,
            new StubUser(),
            _clock,
            NullLogger<EmploymentContractService>.Instance);

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

        public string? UserName => "hr";

        public string? DisplayName => "HR";

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

/// <summary>
/// Which contract paid for which days of a period.
/// </summary>
/// <remarks>
/// A raise takes effect on a date, not at the start of a month. Somebody promoted on the
/// sixteenth is owed half a month at the old figure and half at the new, and paying the whole
/// month at either one is wrong by a real amount in somebody's actual pay.
/// </remarks>
public sealed class ContractApportionmentTests
{
    private static readonly DateOnly From = new(2026, 4, 1);
    private static readonly DateOnly To = new(2026, 4, 30);

    /// <summary>One contract covering the whole month is the whole month.</summary>
    [Fact]
    public void One_contract_covers_the_whole_period()
    {
        var covering = ContractApportionment.Covering([Contract(new DateOnly(2025, 1, 1), null, 6000m)], From, To);

        covering.Count.ShouldBe(1);
        covering[0].Days.ShouldBe(30);

        ContractApportionment.Wages(covering, 30, 30).ShouldBe((6000m, 1500m));
    }

    /// <summary>A raise mid-month splits the month between the two figures.</summary>
    [Fact]
    public void A_raise_mid_month_splits_the_month()
    {
        var covering = ContractApportionment.Covering(
            [
                Contract(new DateOnly(2025, 1, 1), new DateOnly(2026, 4, 15), 6000m),
                Contract(new DateOnly(2026, 4, 16), null, 9000m),
            ],
            From,
            To);

        covering.Count.ShouldBe(2);
        covering[0].Days.ShouldBe(15);
        covering[1].Days.ShouldBe(15);

        var (basic, _) = ContractApportionment.Wages(covering, 30, 30);

        basic.ShouldBe(7500m, "half a month at six thousand and half at nine");
    }

    /// <summary>A contract that starts mid-period covers only its own days.</summary>
    [Fact]
    public void A_contract_starting_mid_period_covers_only_its_own_days()
    {
        var covering = ContractApportionment.Covering(
            [Contract(new DateOnly(2026, 4, 21), null, 6000m)],
            From,
            To);

        covering.Count.ShouldBe(1);
        covering[0].Days.ShouldBe(10);
    }

    /// <summary>A contract that ended before the period does not appear.</summary>
    [Fact]
    public void A_contract_that_ended_before_the_period_does_not_appear()
        => ContractApportionment
            .Covering([Contract(new DateOnly(2025, 1, 1), new DateOnly(2026, 3, 31), 6000m)], From, To)
            .ShouldBeEmpty();

    /// <summary>
    /// Days worked short of the period are spread over the contracts in proportion.
    /// </summary>
    /// <remarks>
    /// Somebody who joined mid-period and was then promoted has fewer worked days than covered
    /// ones. Charging the whole shortfall to one of the two contracts would pay the other in full
    /// for a stretch nobody worked.
    /// </remarks>
    [Fact]
    public void Part_worked_days_are_spread_over_both_contracts()
    {
        var covering = ContractApportionment.Covering(
            [
                Contract(new DateOnly(2025, 1, 1), new DateOnly(2026, 4, 15), 6000m),
                Contract(new DateOnly(2026, 4, 16), null, 9000m),
            ],
            From,
            To);

        var (basic, _) = ContractApportionment.Wages(covering, 15, 30);

        basic.ShouldBe(3750m, "half of the seven and a half thousand a full month would be");
    }

    /// <summary>No contract at all yields nothing, and the caller falls back.</summary>
    [Fact]
    public void No_contract_yields_nothing()
        => ContractApportionment.Wages([], 30, 30).ShouldBe((0m, 0m));

    private static EmploymentContract Contract(DateOnly startsOn, DateOnly? endsOn, decimal basicWage)
        => new()
        {
            TenantId = Guid.Empty,
            CompanyId = Guid.Empty,
            EmployeeNo = "EMP-0001",
            StartsOn = startsOn,
            EndsOn = endsOn,
            Kind = endsOn is null ? ContractKind.Permanent : ContractKind.FixedTerm,
            BasicWage = basicWage,
            Allowances = 1500m,
        };
}
