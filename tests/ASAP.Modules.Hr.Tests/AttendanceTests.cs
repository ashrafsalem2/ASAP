using ASAP.Modules.Hr.Attendance;
using ASAP.Modules.Hr.Leave;
using ASAP.Modules.Hr.People;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Hr.Tests;

/// <summary>
/// What a clock-in and a clock-out come to against a shift.
/// </summary>
/// <remarks>
/// The awkward cases are all arithmetic, and the awkwardest is the night shift: somebody starting
/// at ten in the evening and leaving at six in the morning worked eight hours, not minus sixteen.
/// </remarks>
public sealed class ShiftMathTests
{
    /// <summary>A full day at a day shift is the shift, less its break.</summary>
    [Fact]
    public void A_full_day_is_the_shift_less_its_break()
    {
        var worked = ShiftMath.Worked(Day(), new TimeOnly(8, 0), new TimeOnly(17, 0));

        worked.WorkedMinutes.ShouldBe(480, "nine hours less the hour for lunch");
        worked.LateMinutes.ShouldBe(0);
        worked.EarlyLeaveMinutes.ShouldBe(0);
        worked.OvertimeMinutes.ShouldBe(0);
    }

    /// <summary>Arriving inside the grace is not late.</summary>
    [Fact]
    public void Arriving_inside_the_grace_is_not_late()
        => ShiftMath.Worked(Day(), new TimeOnly(8, 10), new TimeOnly(17, 0))
            .LateMinutes.ShouldBe(0);

    /// <summary>Past the grace, only the time past the start counts, not past the grace.</summary>
    [Fact]
    public void Lateness_is_measured_from_the_start_not_from_the_grace()
        => ShiftMath.Worked(Day(), new TimeOnly(8, 25), new TimeOnly(17, 0))
            .LateMinutes.ShouldBe(15, "twenty-five minutes late, ten of them forgiven");

    /// <summary>
    /// Being late and staying on are two facts, and neither cancels the other.
    /// </summary>
    /// <remarks>
    /// Reporting a net figure would answer no question anybody asked. The manager wants to know
    /// they were late; the payroll clerk wants to know they worked an extra hour.
    /// </remarks>
    [Fact]
    public void Late_and_overtime_do_not_cancel_out()
    {
        var worked = ShiftMath.Worked(Day(), new TimeOnly(8, 30), new TimeOnly(18, 0));

        worked.LateMinutes.ShouldBe(20);
        worked.OvertimeMinutes.ShouldBe(60);
    }

    /// <summary>Leaving early is measured against the shift's end.</summary>
    [Fact]
    public void Leaving_early_is_measured_against_the_end()
        => ShiftMath.Worked(Day(), new TimeOnly(8, 0), new TimeOnly(15, 30))
            .EarlyLeaveMinutes.ShouldBe(90);

    /// <summary>Coming in early does not start the paid day early, but does count as overtime.</summary>
    [Fact]
    public void Coming_in_early_is_overtime_not_an_early_start()
    {
        var worked = ShiftMath.Worked(Day(), new TimeOnly(7, 0), new TimeOnly(17, 0));

        worked.WorkedMinutes.ShouldBe(480, "the shift is what it is");
        worked.OvertimeMinutes.ShouldBe(60, "but the hour was worked");
    }

    /// <summary>
    /// A night shift's clock does not reset at midnight.
    /// </summary>
    /// <remarks>
    /// Nothing distinguishes a night shift from a day one except that its end is earlier than its
    /// start, which is deliberate: a separate flag would be a second thing to keep in step.
    /// </remarks>
    [Fact]
    public void A_night_shift_runs_into_the_next_morning()
    {
        var worked = ShiftMath.Worked(Night(), new TimeOnly(22, 0), new TimeOnly(6, 0));

        worked.WorkedMinutes.ShouldBe(450, "eight hours less the half-hour break");
        worked.LateMinutes.ShouldBe(0);
        worked.OvertimeMinutes.ShouldBe(0);
    }

    /// <summary>And lateness on a night shift is measured the same way.</summary>
    [Fact]
    public void Lateness_on_a_night_shift_reads_correctly()
        => ShiftMath.Worked(Night(), new TimeOnly(22, 20), new TimeOnly(6, 0))
            .LateMinutes.ShouldBe(20);

    /// <summary>And staying past the end of a night shift is overtime, not a wrap.</summary>
    [Fact]
    public void Staying_past_a_night_shift_is_overtime()
        => ShiftMath.Worked(Night(), new TimeOnly(22, 0), new TimeOnly(7, 30))
            .OvertimeMinutes.ShouldBe(90);

    /// <summary>Out before in, once the wrap is allowed for, says nothing rather than guessing.</summary>
    [Fact]
    public void Out_before_in_says_nothing()
        => ShiftMath.Worked(Day(), new TimeOnly(17, 0), new TimeOnly(8, 0))
            .ShouldBe(new WorkedDay(0, 0, 0, 0));

    private static Shift Day() => new()
    {
        TenantId = Guid.Empty,
        CompanyId = Guid.Empty,
        Code = "DAY",
        Name = "Day shift",
        StartsAt = new TimeOnly(8, 0),
        EndsAt = new TimeOnly(17, 0),
        BreakMinutes = 60,
        GraceMinutes = 10,
    };

    private static Shift Night() => new()
    {
        TenantId = Guid.Empty,
        CompanyId = Guid.Empty,
        Code = "NIGHT",
        Name = "Night shift",
        StartsAt = new TimeOnly(22, 0),
        EndsAt = new TimeOnly(6, 0),
        BreakMinutes = 30,
        GraceMinutes = 0,
    };
}

/// <summary>
/// Recording what happened, and what the record refuses to say.
/// </summary>
/// <remarks>
/// Attendance records and measures. It does not pay anybody and it does not dock anybody: a clock
/// showing somebody late is a fact, and a deduction for it is a decision somebody must make.
/// </remarks>
public sealed class AttendanceTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000a7");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a7");

    // A Monday, so a five-day shift runs on it.
    private static readonly DateOnly Monday = new(2026, 8, 31);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];
    private Guid _employeeId;

    /// <summary>Sets up one employee and a Sunday-to-Thursday day shift.</summary>
    public AttendanceTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-attendance-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        var employee = new Employee
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "EMP-0001",
            Name = "Salim Al Harbi",
            HiredOn = new DateOnly(2025, 1, 1),
            Status = EmploymentStatus.Active,
            BasicWage = 6000m,
        };

        context.Set<Employee>().Add(employee);
        context.SaveChanges();

        _employeeId = employee.Id;
    }

    /// <summary>A day within the shift is present, and the hours are measured.</summary>
    [Fact]
    public async Task A_normal_day_is_present_and_measured()
    {
        using var context = NewContext();
        var attendance = Attendance(context);

        await SetUpShiftAsync(attendance);

        var day = await attendance.RecordAsync(
            new AttendanceRequest("EMP-0001", Monday, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        day.Succeeded.ShouldBeTrue();
        day.Value.Status.ShouldBe(AttendanceStatus.Present);
        day.Value.WorkedMinutes.ShouldBe(480);
        day.Value.ShiftCode.ShouldBe("DAY");
    }

    /// <summary>Coming in late is recorded as late, with the minutes.</summary>
    [Fact]
    public async Task Coming_in_late_is_recorded_as_late()
    {
        using var context = NewContext();
        var attendance = Attendance(context);

        await SetUpShiftAsync(attendance);

        var day = await attendance.RecordAsync(
            new AttendanceRequest("EMP-0001", Monday, new TimeOnly(8, 45), new TimeOnly(17, 0)));

        day.Value.Status.ShouldBe(AttendanceStatus.Late);
        day.Value.LateMinutes.ShouldBe(35);
    }

    /// <summary>One day cannot be recorded twice.</summary>
    [Fact]
    public async Task One_day_cannot_be_recorded_twice()
    {
        using var context = NewContext();
        var attendance = Attendance(context);

        await SetUpShiftAsync(attendance);

        await attendance.RecordAsync(
            new AttendanceRequest("EMP-0001", Monday, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        var again = await attendance.RecordAsync(
            new AttendanceRequest("EMP-0001", Monday, new TimeOnly(9, 0), new TimeOnly(17, 0)));

        again.Failed.ShouldBeTrue();
        again.Messages.ShouldContain(m => m.Code == HrMessages.AttendanceAlreadyRecorded);
    }

    /// <summary>But it can be amended, and the figures are worked out again.</summary>
    [Fact]
    public async Task A_day_can_be_amended()
    {
        using var context = NewContext();
        var attendance = Attendance(context);

        await SetUpShiftAsync(attendance);

        await attendance.RecordAsync(
            new AttendanceRequest("EMP-0001", Monday, new TimeOnly(9, 0), new TimeOnly(17, 0)));

        var amended = await attendance.RecordAsync(
            new AttendanceRequest("EMP-0001", Monday, new TimeOnly(8, 0), new TimeOnly(17, 0), "Clock was wrong"),
            amend: true);

        amended.Succeeded.ShouldBeTrue();
        amended.Value.LateMinutes.ShouldBe(0);
        amended.Value.Note.ShouldBe("Clock was wrong");
    }

    /// <summary>A day the shift does not run is all overtime, and says so.</summary>
    [Fact]
    public async Task A_rest_day_worked_is_all_overtime()
    {
        using var context = NewContext();
        var attendance = Attendance(context);

        await SetUpShiftAsync(attendance);

        // The Friday after, which the Sunday-to-Thursday shift does not run on.
        var friday = new DateOnly(2026, 9, 4);

        var day = await attendance.RecordAsync(
            new AttendanceRequest("EMP-0001", friday, new TimeOnly(9, 0), new TimeOnly(13, 0)));

        day.Succeeded.ShouldBeTrue();
        day.Value.OvertimeMinutes.ShouldBe(240);
        day.Value.LateMinutes.ShouldBe(0, "there is no shift to be late for");
        day.Messages.ShouldContain(m => m.Code == HrMessages.AttendedOnRestDay);
    }

    /// <summary>Somebody on no shift has their hours recorded but nothing measured.</summary>
    [Fact]
    public async Task Somebody_on_no_shift_is_recorded_but_not_measured()
    {
        using var context = NewContext();

        var day = await Attendance(context).RecordAsync(
            new AttendanceRequest("EMP-0001", Monday, new TimeOnly(8, 45), new TimeOnly(17, 0)));

        day.Succeeded.ShouldBeTrue();
        day.Value.LateMinutes.ShouldBe(0, "nothing to be late against");
        day.Messages.ShouldContain(m => m.Code == HrMessages.NoShiftOnDate);
    }

    /// <summary>Coming in on a day of approved leave is said, not refused.</summary>
    [Fact]
    public async Task Coming_in_on_a_day_of_leave_is_said_not_refused()
    {
        using var context = NewContext();
        var attendance = Attendance(context);

        await SetUpShiftAsync(attendance);

        context.Set<LeaveRequest>().Add(new LeaveRequest
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = $"LV-{Guid.CreateVersion7():N}"[..12],
            EmployeeId = _employeeId,
            EmployeeNo = "EMP-0001",
            EmployeeName = "Salim Al Harbi",
            Kind = LeaveKind.Annual,
            FromDate = Monday,
            ToDate = Monday,
            Status = LeaveStatus.Approved,
        });

        await context.SaveChangesAsync();

        var day = await attendance.RecordAsync(
            new AttendanceRequest("EMP-0001", Monday, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        day.Succeeded.ShouldBeTrue("people do come in on days off");
        day.Messages.ShouldContain(m => m.Code == HrMessages.AttendedWhileOnLeave);
    }

    /// <summary>
    /// A working day with no attendance and no leave counts as unexplained.
    /// </summary>
    /// <remarks>
    /// Reported, never deducted. A clock is not authority to dock anybody's pay.
    /// </remarks>
    [Fact]
    public async Task A_day_with_neither_attendance_nor_leave_is_unexplained()
    {
        using var context = NewContext();
        var attendance = Attendance(context);

        await SetUpShiftAsync(attendance);

        // Sunday to Thursday of the week beginning 30 August: five working days.
        var from = new DateOnly(2026, 8, 30);
        var to = new DateOnly(2026, 9, 5);

        await attendance.RecordAsync(
            new AttendanceRequest("EMP-0001", from, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        await attendance.RecordAsync(
            new AttendanceRequest("EMP-0001", Monday, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        var absences = await attendance.UnexplainedAbsenceAsync(from, to);

        absences[_employeeId].ShouldBe(3, "Tuesday, Wednesday and Thursday");
    }

    /// <summary>Approved leave explains a day, so it is not counted as an absence.</summary>
    [Fact]
    public async Task Approved_leave_explains_the_day()
    {
        using var context = NewContext();
        var attendance = Attendance(context);

        await SetUpShiftAsync(attendance);

        var from = new DateOnly(2026, 8, 30);
        var to = new DateOnly(2026, 9, 5);

        context.Set<LeaveRequest>().Add(new LeaveRequest
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = $"LV-{Guid.CreateVersion7():N}"[..12],
            EmployeeId = _employeeId,
            EmployeeNo = "EMP-0001",
            EmployeeName = "Salim Al Harbi",
            Kind = LeaveKind.Annual,
            FromDate = from,
            ToDate = to,
            Status = LeaveStatus.Approved,
        });

        await context.SaveChangesAsync();

        (await attendance.UnexplainedAbsenceAsync(from, to)).ShouldBeEmpty();
    }

    /// <summary>Moving somebody to another shift closes the one before it the day before.</summary>
    [Fact]
    public async Task Moving_shift_closes_the_one_before_it()
    {
        using var context = NewContext();
        var attendance = Attendance(context);

        await SetUpShiftAsync(attendance);

        await attendance.SaveShiftAsync(new ShiftRequest(
            "NIGHT",
            "Night shift",
            new TimeOnly(22, 0),
            new TimeOnly(6, 0),
            BreakMinutes: 30,
            DaysOfWeek: 0b0011_1111));

        var moved = await attendance.AssignAsync("EMP-0001", "NIGHT", new DateOnly(2026, 9, 1));

        moved.Succeeded.ShouldBeTrue();

        var all = await attendance.AssignmentsAsync("EMP-0001");

        all.Count.ShouldBe(2);
        all[0].ToDate.ShouldBe(new DateOnly(2026, 8, 31));

        (await attendance.ShiftOnAsync("EMP-0001", new DateOnly(2026, 8, 31)))
            .ShouldNotBeNull().Code.ShouldBe("DAY");

        (await attendance.ShiftOnAsync("EMP-0001", new DateOnly(2026, 9, 1)))
            .ShouldNotBeNull().Code.ShouldBe("NIGHT");
    }

    /// <summary>A shift whose break swallows it is refused.</summary>
    [Fact]
    public async Task A_break_longer_than_the_shift_is_refused()
    {
        using var context = NewContext();

        var result = await Attendance(context).SaveShiftAsync(new ShiftRequest(
            "SHORT",
            "Too short",
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            BreakMinutes: 90));

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == HrMessages.ShiftBreakTooLong);
    }

    /// <summary>A shift that runs on no day would never expect anybody in.</summary>
    [Fact]
    public async Task A_shift_that_runs_on_no_day_is_refused()
    {
        using var context = NewContext();

        var result = await Attendance(context).SaveShiftAsync(new ShiftRequest(
            "NEVER",
            "Never",
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            DaysOfWeek: 0));

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code == HrMessages.ShiftRunsNoDay);
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    /// <summary>A Sunday-to-Thursday day shift, and somebody on it from the start of the year.</summary>
    private static async Task SetUpShiftAsync(AttendanceService attendance)
    {
        var saved = await attendance.SaveShiftAsync(new ShiftRequest(
            "DAY",
            "Day shift",
            new TimeOnly(8, 0),
            new TimeOnly(17, 0),
            BreakMinutes: 60,

            // Sunday to Thursday: bit 0 is Sunday.
            DaysOfWeek: 0b0001_1111,
            GraceMinutes: 10));

        saved.Succeeded.ShouldBeTrue();

        (await attendance.AssignAsync("EMP-0001", "DAY", new DateOnly(2026, 1, 1)))
            .Succeeded.ShouldBeTrue();
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new HrSchema()]);

        _opened.Add(context);

        return context;
    }

    private AttendanceService Attendance(AsapDbContext context)
        => new(
            context,
            new MessageCatalog([.. PlatformMessages.All, .. HrMessages.All]),
            _tenancy,
            new StubUser());

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
