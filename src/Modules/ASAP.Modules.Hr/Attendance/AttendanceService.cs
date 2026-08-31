using ASAP.Modules.Hr.Leave;
using ASAP.Modules.Hr.People;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Hr.Attendance;

/// <summary>A shift as somebody asks for it to be saved.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="StartsAt">When it starts.</param>
/// <param name="EndsAt">When it ends.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="BreakMinutes">Unpaid break in the middle of it.</param>
/// <param name="DaysOfWeek">Which days it runs, as a bit per day with Sunday as 1.</param>
/// <param name="GraceMinutes">Minutes after the start that are not counted as late.</param>
/// <param name="IsActive">Whether anybody may still be put on it.</param>
public sealed record ShiftRequest(
    string Code,
    string Name,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    string? NameArabic = null,
    int BreakMinutes = 0,
    int DaysOfWeek = 0b0111_1111,
    int GraceMinutes = 0,
    bool IsActive = true);

/// <summary>A day of attendance as somebody records it.</summary>
/// <param name="EmployeeNo">Whose day.</param>
/// <param name="OnDate">The day.</param>
/// <param name="ClockedInAt">When they clocked in.</param>
/// <param name="ClockedOutAt">When they clocked out.</param>
/// <param name="Note">Why it reads as it does.</param>
public sealed record AttendanceRequest(
    string EmployeeNo,
    DateOnly OnDate,
    TimeOnly? ClockedInAt = null,
    TimeOnly? ClockedOutAt = null,
    string? Note = null);

/// <summary>
/// Keeps the shifts, who is on which, and what actually happened each day.
/// </summary>
/// <remarks>
/// <para>
/// It records and it measures. It does not pay anybody and it does not dock anybody. A clock
/// showing somebody twenty minutes late is a fact; a deduction for it is a decision, and turning
/// the first into the second automatically would mean the first anybody knew of a new rule was a
/// short payslip. Payroll is told what the attendance says and says so out loud; somebody then
/// records the leave or the deduction deliberately.
/// </para>
/// <para>
/// Everything else here defends one invariant apiece: one shift assignment per person per day,
/// and one attendance record per person per day. Two of either is two accounts of the same day,
/// and every figure derived from them is the sum of things that were meant to be one.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="user">Says who recorded it.</param>
public sealed class AttendanceService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenancy,
    IUserContext user)
{
    /// <summary>The shifts.</summary>
    /// <param name="activeOnly">Whether to leave out the ones switched off.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The shifts, by code.</returns>
    public async Task<IReadOnlyList<Shift>> ShiftsAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<Shift>().AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(static s => s.IsActive);
        }

        return await query
            .OrderBy(s => s.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Writes a shift.</summary>
    /// <param name="request">The shift.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The shift, or why it could not be saved.</returns>
    public async Task<Result<Shift>> SaveShiftAsync(
        ShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length == 0 || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<Shift>.Failure(messages.Render(HrMessages.ShiftIncomplete, Args()));
        }

        var found = new List<AsapMessage>();

        if ((request.DaysOfWeek & 0b0111_1111) == 0)
        {
            found.Add(messages.Render(HrMessages.ShiftRunsNoDay, Args(("ShiftCode", code))));
        }

        var shift = await context.Set<Shift>()
            .FirstOrDefaultAsync(s => s.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (shift is null)
        {
            shift = new Shift
            {
                TenantId = tenancy.RequireTenantId(),
                CompanyId = tenancy.RequireCompanyId(),
                Code = code,
                Name = request.Name,
            };

            context.Set<Shift>().Add(shift);
        }

        shift.Name = request.Name;
        shift.NameArabic = request.NameArabic;
        shift.StartsAt = request.StartsAt;
        shift.EndsAt = request.EndsAt;
        shift.BreakMinutes = request.BreakMinutes;
        shift.DaysOfWeek = request.DaysOfWeek;
        shift.GraceMinutes = request.GraceMinutes;
        shift.IsActive = request.IsActive;

        // Checked after the times are on it, because the shift is the only thing that knows how
        // long it runs once a night shift's wrap is allowed for.
        if (shift.PaidMinutes <= 0)
        {
            var span = shift.PaidMinutes + request.BreakMinutes;

            found.Add(messages.Render(
                HrMessages.ShiftBreakTooLong,
                Args(
                    ("ShiftCode", code),
                    ("ShiftMinutes", span),
                    ("BreakMinutes", request.BreakMinutes))));
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<Shift>.Failure(found);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<Shift>.Success(shift, found);
    }

    /// <summary>Who is on which shift.</summary>
    /// <param name="employeeNo">Whose, or null for everybody's.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The assignments, earliest first.</returns>
    public async Task<IReadOnlyList<ShiftAssignment>> AssignmentsAsync(
        string? employeeNo = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<ShiftAssignment>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(employeeNo))
        {
            var no = employeeNo.Trim().ToUpperInvariant();
            query = query.Where(a => a.EmployeeNo == no);
        }

        return await query
            .OrderBy(a => a.EmployeeNo)
            .ThenBy(a => a.FromDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Puts somebody on a shift from a date, closing the one before it the day before.
    /// </summary>
    /// <remarks>
    /// Closes rather than refuses, exactly as a contract does. Moving somebody from days to
    /// nights is one act, and doing it as two saves leaves either an overlap or a day where
    /// nothing says what they were expected to work.
    /// </remarks>
    /// <param name="employeeNo">Whose.</param>
    /// <param name="shiftCode">The shift.</param>
    /// <param name="fromDate">The first day it applies.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The assignment, or why it was refused.</returns>
    public async Task<Result<ShiftAssignment>> AssignAsync(
        string employeeNo,
        string shiftCode,
        DateOnly fromDate,
        CancellationToken cancellationToken = default)
    {
        var no = employeeNo?.Trim().ToUpperInvariant() ?? string.Empty;
        var code = shiftCode?.Trim().ToUpperInvariant() ?? string.Empty;

        var employee = await context.Set<Employee>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.No == no, cancellationToken)
            .ConfigureAwait(false);

        if (employee is null)
        {
            return Result<ShiftAssignment>.Failure(
                messages.Render(HrMessages.EmployeeNotFound, Args(("EmployeeNo", no))));
        }

        var shift = await context.Set<Shift>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (shift is null)
        {
            return Result<ShiftAssignment>.Failure(
                messages.Render(HrMessages.ShiftNotFound, Args(("ShiftCode", code))));
        }

        if (!shift.IsActive)
        {
            return Result<ShiftAssignment>.Failure(messages.Render(
                HrMessages.ShiftWithdrawn,
                Args(("ShiftCode", code), ("Name", shift.Name))));
        }

        var current = await context.Set<ShiftAssignment>()
            .Where(a => a.EmployeeNo == no && a.FromDate < fromDate)
            .Where(a => a.ToDate == null || a.ToDate >= fromDate)
            .OrderByDescending(a => a.FromDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current is not null)
        {
            current.ToDate = fromDate.AddDays(-1);
        }

        var clash = await context.Set<ShiftAssignment>()
            .Where(a => a.EmployeeNo == no && a.FromDate >= fromDate)
            .OrderBy(a => a.FromDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (clash is not null)
        {
            return Result<ShiftAssignment>.Failure(messages.Render(
                HrMessages.ShiftAssignmentOverlaps,
                Args(
                    ("EmployeeNo", no),
                    ("ExistingFrom", clash.FromDate),
                    ("FromDate", fromDate))));
        }

        var assignment = new ShiftAssignment
        {
            TenantId = tenancy.RequireTenantId(),
            CompanyId = tenancy.RequireCompanyId(),
            EmployeeId = employee.Id,
            EmployeeNo = no,
            ShiftCode = code,
            FromDate = fromDate,
        };

        context.Set<ShiftAssignment>().Add(assignment);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ShiftAssignment>.Success(assignment);
    }

    /// <summary>Attendance over a stretch of days.</summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="employeeNo">Whose, or null for everybody's.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The records, by person and day.</returns>
    public async Task<IReadOnlyList<AttendanceRecord>> ListAsync(
        DateOnly from,
        DateOnly to,
        string? employeeNo = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<AttendanceRecord>()
            .AsNoTracking()
            .Where(a => a.OnDate >= from && a.OnDate <= to);

        if (!string.IsNullOrWhiteSpace(employeeNo))
        {
            var no = employeeNo.Trim().ToUpperInvariant();
            query = query.Where(a => a.EmployeeNo == no);
        }

        return await query
            .OrderBy(a => a.OnDate)
            .ThenBy(a => a.EmployeeNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records one day, measuring it against whatever shift the person was on that day.
    /// </summary>
    /// <param name="request">The day.</param>
    /// <param name="amend">Whether to replace a record already there rather than refuse.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The record, and anything worth saying about it.</returns>
    public async Task<Result<AttendanceRecord>> RecordAsync(
        AttendanceRequest request,
        bool amend = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var no = request.EmployeeNo?.Trim().ToUpperInvariant() ?? string.Empty;

        var employee = await context.Set<Employee>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.No == no, cancellationToken)
            .ConfigureAwait(false);

        if (employee is null)
        {
            return Result<AttendanceRecord>.Failure(
                messages.Render(HrMessages.EmployeeNotFound, Args(("EmployeeNo", no))));
        }

        var found = new List<AsapMessage>();

        var arguments = Args(
            ("EmployeeNo", no),
            ("OnDate", request.OnDate),
            ("ClockedInAt", request.ClockedInAt),
            ("ClockedOutAt", request.ClockedOutAt));

        var existing = await context.Set<AttendanceRecord>()
            .FirstOrDefaultAsync(a => a.EmployeeNo == no && a.OnDate == request.OnDate, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null && !amend)
        {
            return Result<AttendanceRecord>.Failure(
                messages.Render(HrMessages.AttendanceAlreadyRecorded, arguments));
        }

        var shift = await ShiftOnAsync(no, request.OnDate, cancellationToken).ConfigureAwait(false);

        if (shift is null)
        {
            found.Add(messages.Render(HrMessages.NoShiftOnDate, arguments));
        }
        else if (request.ClockedInAt is not null && !shift.RunsOn(request.OnDate))
        {
            found.Add(messages.Render(HrMessages.AttendedOnRestDay, arguments));
        }

        if (request.ClockedInAt is { } inAt
            && request.ClockedOutAt is { } outAt
            && shift is not null
            && ShiftMath.Minutes(outAt, shift.StartsAt, shift.CrossesMidnight)
               < ShiftMath.Minutes(inAt, shift.StartsAt, shift.CrossesMidnight))
        {
            found.Add(messages.Render(HrMessages.ClockedOutBeforeIn, arguments));
        }

        if (request.ClockedInAt is not null
            && await OnLeaveAsync(employee.Id, request.OnDate, cancellationToken).ConfigureAwait(false))
        {
            found.Add(messages.Render(HrMessages.AttendedWhileOnLeave, arguments));
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<AttendanceRecord>.Failure(found);
        }

        var record = existing ?? new AttendanceRecord
        {
            TenantId = tenancy.RequireTenantId(),
            CompanyId = tenancy.RequireCompanyId(),
            EmployeeId = employee.Id,
            EmployeeNo = no,
            OnDate = request.OnDate,
        };

        if (existing is null)
        {
            context.Set<AttendanceRecord>().Add(record);
        }

        record.ShiftCode = shift?.Code;
        record.ClockedInAt = request.ClockedInAt;
        record.ClockedOutAt = request.ClockedOutAt;
        record.Note = request.Note?.Trim();
        record.RecordedByUserName = user.DisplayName ?? user.UserName;

        Measure(record, shift);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<AttendanceRecord>.Success(record, found);
    }

    /// <summary>
    /// Working days in a period that nothing accounts for, by employee.
    /// </summary>
    /// <remarks>
    /// The figure payroll is told about. A day their shift ran, on which there is no attendance
    /// record and no approved leave, is a day nobody can explain — and it is deliberately reported
    /// rather than deducted, because a clock is not authority to dock anybody's pay.
    /// </remarks>
    /// <param name="from">The first day of the period.</param>
    /// <param name="to">The last day.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Days unaccounted for, by employee id.</returns>
    public async Task<Dictionary<Guid, int>> UnexplainedAbsenceAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var employees = await context.Set<Employee>()
            .AsNoTracking()
            .Where(e => e.HiredOn <= to && (e.LeftOn == null || e.LeftOn >= from))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (employees.Count == 0)
        {
            return [];
        }

        var shifts = (await context.Set<Shift>().AsNoTracking().ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(static s => s.Code, StringComparer.OrdinalIgnoreCase);

        var assignments = await context.Set<ShiftAssignment>()
            .AsNoTracking()
            .Where(a => a.FromDate <= to && (a.ToDate == null || a.ToDate >= from))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var attended = (await context.Set<AttendanceRecord>()
                .AsNoTracking()
                .Where(a => a.OnDate >= from && a.OnDate <= to && a.ClockedInAt != null)
                .Select(static a => new { a.EmployeeId, a.OnDate })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(static a => (a.EmployeeId, a.OnDate))
            .ToHashSet();

        var leave = (await context.Set<LeaveRequest>()
                .AsNoTracking()
                .Where(l => l.Status == LeaveStatus.Approved && l.FromDate <= to && l.ToDate >= from)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(static l => l.EmployeeId)
            .ToDictionary(static g => g.Key, static g => g.ToList());

        var absences = new Dictionary<Guid, int>();

        foreach (var employee in employees)
        {
            var count = 0;

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day < employee.HiredOn || (employee.LeftOn is { } left && day > left))
                {
                    continue;
                }

                var code = assignments
                    .Find(a => a.EmployeeId == employee.Id && a.Covers(day))?.ShiftCode;

                // Somebody on no shift has no expected day, so nothing about their absence can be
                // said. That is reported separately, when their attendance is recorded.
                if (code is null || !shifts.TryGetValue(code, out var shift) || !shift.RunsOn(day))
                {
                    continue;
                }

                if (attended.Contains((employee.Id, day)))
                {
                    continue;
                }

                if (leave.GetValueOrDefault(employee.Id)?.Exists(l => day >= l.FromDate && day <= l.ToDate) == true)
                {
                    continue;
                }

                count++;
            }

            if (count > 0)
            {
                absences[employee.Id] = count;
            }
        }

        return absences;
    }

    /// <summary>The shift somebody was on for a day, if any.</summary>
    /// <param name="employeeNo">Whose.</param>
    /// <param name="on">The day.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The shift, or null where they were on none.</returns>
    public async Task<Shift?> ShiftOnAsync(
        string employeeNo,
        DateOnly on,
        CancellationToken cancellationToken = default)
    {
        var no = employeeNo?.Trim().ToUpperInvariant() ?? string.Empty;

        var code = await context.Set<ShiftAssignment>()
            .AsNoTracking()
            .Where(a => a.EmployeeNo == no && a.FromDate <= on && (a.ToDate == null || a.ToDate >= on))
            .OrderByDescending(a => a.FromDate)
            .Select(static a => a.ShiftCode)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (code is null)
        {
            return null;
        }

        return await context.Set<Shift>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == code, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Measure(AttendanceRecord record, Shift? shift)
    {
        if (record.ClockedInAt is null)
        {
            record.Status = shift is not null && !shift.RunsOn(record.OnDate)
                ? AttendanceStatus.RestDay
                : AttendanceStatus.Absent;

            record.WorkedMinutes = 0;
            record.LateMinutes = 0;
            record.EarlyLeaveMinutes = 0;
            record.OvertimeMinutes = 0;

            return;
        }

        if (shift is null)
        {
            // Recorded, not measured. Without a shift there is nothing for late or over to mean,
            // and inventing a nine-to-five to measure against would be inventing a fact.
            record.Status = AttendanceStatus.Present;
            record.WorkedMinutes = 0;
            record.LateMinutes = 0;
            record.EarlyLeaveMinutes = 0;
            record.OvertimeMinutes = 0;

            return;
        }

        if (!shift.RunsOn(record.OnDate))
        {
            // A day off worked is all overtime. There is no shift to be late for.
            var minutes = record.ClockedOutAt is { } outAt
                ? Math.Max(0, (int)(outAt - record.ClockedInAt.Value).TotalMinutes)
                : 0;

            record.Status = AttendanceStatus.Present;
            record.WorkedMinutes = minutes;
            record.LateMinutes = 0;
            record.EarlyLeaveMinutes = 0;
            record.OvertimeMinutes = minutes;

            return;
        }

        var worked = record.ClockedOutAt is { } finish
            ? ShiftMath.Worked(shift, record.ClockedInAt.Value, finish)
            : new WorkedDay(
                0,
                Math.Max(
                    0,
                    ShiftMath.Minutes(record.ClockedInAt.Value, shift.StartsAt, shift.CrossesMidnight)
                    - ShiftMath.Minutes(shift.StartsAt, shift.StartsAt, shift.CrossesMidnight)
                    - shift.GraceMinutes),
                0,
                0);

        record.WorkedMinutes = worked.WorkedMinutes;
        record.LateMinutes = worked.LateMinutes;
        record.EarlyLeaveMinutes = worked.EarlyLeaveMinutes;
        record.OvertimeMinutes = worked.OvertimeMinutes;
        record.Status = worked.LateMinutes > 0 ? AttendanceStatus.Late : AttendanceStatus.Present;
    }

    private async Task<bool> OnLeaveAsync(Guid employeeId, DateOnly on, CancellationToken cancellationToken)
        => await context.Set<LeaveRequest>()
            .AsNoTracking()
            .AnyAsync(
                l => l.EmployeeId == employeeId
                     && l.Status == LeaveStatus.Approved
                     && l.FromDate <= on
                     && l.ToDate >= on,
                cancellationToken)
            .ConfigureAwait(false);

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}
