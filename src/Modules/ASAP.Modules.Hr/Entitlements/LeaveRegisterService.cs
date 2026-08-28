using ASAP.Modules.Hr.People;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Hr.Entitlements;

/// <summary>
/// Records leave as it is actually taken.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="People.EmployeeService.EntitlementsAsync"/> already worked out what everybody earns;
/// this is what it was missing to say what they have left. The two never overlap in what they own:
/// earning is a calculation from service length and a policy, taking is a fact somebody reports,
/// and a balance is only ever the two of them read together.
/// </para>
/// <para>
/// A record is written once and stands. There is no approval step here -- this is a register of
/// what happened, not a request for what might. A workflow that asks somebody's manager before the
/// day is taken is a real feature and a different one; conflating the two would mean a company that
/// only wants the record cannot have it without the process.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="employees">Resolves the employee a record belongs to.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="logger">Records what was written.</param>
public sealed class LeaveRegisterService(
    AsapDbContext context,
    EmployeeService employees,
    IMessageCatalog messages,
    ILogger<LeaveRegisterService> logger)
{
    /// <summary>Everybody's leave, most recent first.</summary>
    /// <param name="employeeNo">Who.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The records, or every reason they could not be read.</returns>
    public async Task<Result<IReadOnlyList<LeaveRecord>>> ListAsync(
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        var employee = await employees.LoadAsync(employeeNo, cancellationToken).ConfigureAwait(false);

        if (employee is null)
        {
            return Result<IReadOnlyList<LeaveRecord>>.Failure(NotFound(employeeNo));
        }

        var records = await context.Set<LeaveRecord>()
            .AsNoTracking()
            .Where(r => r.EmployeeId == employee.Id)
            .OrderByDescending(static r => r.FromDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<LeaveRecord>>.Success(records);
    }

    /// <summary>
    /// Records that somebody was away.
    /// </summary>
    /// <param name="employeeNo">Who.</param>
    /// <param name="fromDate">Their first day away.</param>
    /// <param name="toDate">Their last day away.</param>
    /// <param name="note">What it was for, when that is worth recording.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The record, or every reason it could not be written.</returns>
    public async Task<Result<LeaveRecord>> RecordAsync(
        string employeeNo,
        DateOnly fromDate,
        DateOnly toDate,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var employee = await employees.LoadAsync(employeeNo, cancellationToken).ConfigureAwait(false);

        if (employee is null)
        {
            return Result<LeaveRecord>.Failure(NotFound(employeeNo));
        }

        var arguments = Args(
            ("EmployeeNo", employee.No),
            ("FromDate", fromDate),
            ("ToDate", toDate),
            ("HiredOn", employee.HiredOn),
            ("LeftOn", employee.LeftOn));

        if (toDate < fromDate)
        {
            return Result<LeaveRecord>.Failure(
                messages.Render(HrMessages.LeaveDatesBackwards, arguments));
        }

        if (fromDate < employee.HiredOn || (employee.LeftOn is { } left && toDate > left))
        {
            return Result<LeaveRecord>.Failure(
                messages.Render(HrMessages.LeaveOutsideEmployment, arguments));
        }

        var existing = await context.Set<LeaveRecord>()
            .Where(r => r.EmployeeId == employee.Id)
            .OrderBy(static r => r.FromDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var overlap = existing.FirstOrDefault(r => r.FromDate <= toDate && r.ToDate >= fromDate);

        if (overlap is not null)
        {
            arguments["ExistingFrom"] = overlap.FromDate;
            arguments["ExistingTo"] = overlap.ToDate;

            return Result<LeaveRecord>.Failure(
                messages.Render(HrMessages.LeaveOverlaps, arguments));
        }

        var record = new LeaveRecord
        {
            EmployeeId = employee.Id,
            FromDate = fromDate,
            ToDate = toDate,
            Note = note,
        };

        var found = new List<AsapMessage>();

        // Measured against everything on the books up to and including this record, so a run of
        // three separate requests that together outrun the balance is caught on the one that
        // actually crosses it rather than let through because each looked fine taken alone.
        var earned = LeaveAccrual.EarnedBetween(employee, employee.HiredOn, toDate);
        var takenToDate = existing.Where(r => r.FromDate <= toDate).Sum(static r => r.Days) + record.Days;
        var shortfall = takenToDate - earned;

        if (shortfall > 0m)
        {
            arguments["Earned"] = earned;
            arguments["Taken"] = takenToDate;
            arguments["Shortfall"] = shortfall;

            found.Add(messages.Render(HrMessages.LeaveExceedsBalance, arguments));
        }

        context.Set<LeaveRecord>().Add(record);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Recorded {Days} day(s) of leave for {EmployeeNo}, from {FromDate} to {ToDate}.",
            record.Days,
            employee.No,
            fromDate,
            toDate);

        return Result<LeaveRecord>.Success(record, found);
    }

    private AsapMessage NotFound(string employeeNo)
        => messages.Render(HrMessages.EmployeeNotFound, Args(("EmployeeNo", employeeNo)));

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
