using ASAP.Modules.Hr.Entitlements;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Hr.People;

/// <summary>What one employee has earned and not yet been given.</summary>
/// <param name="EmployeeNo">Who.</param>
/// <param name="Name">Their name.</param>
/// <param name="ServiceYears">How long they have been here.</param>
/// <param name="LeaveDays">Days of leave earned and not taken.</param>
/// <param name="LeaveLiability">What those days are worth.</param>
/// <param name="EndOfService">What they would be owed if the company let them go today.</param>
/// <param name="TotalOwed">The two together, which is what the company carries for them.</param>
public readonly record struct EmployeeEntitlement(
    string EmployeeNo,
    string Name,
    decimal ServiceYears,
    decimal LeaveDays,
    decimal LeaveLiability,
    decimal EndOfService,
    decimal TotalOwed);

/// <summary>
/// Employees, where they have worked, and what they have earned.
/// </summary>
/// <remarks>
/// The branch history is the part that needs guarding. Everything else here is a form; an
/// assignment that overlaps another charges a day of somebody's wage to two branches, and one
/// that leaves a gap charges it to none — and neither shows up until somebody asks why a branch
/// looks cheaper than it is.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="numbers">Issues the employee number.</param>
/// <param name="setup">Supplies the number series.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records hirings, transfers and leavers.</param>
public sealed class EmployeeService(
    AsapDbContext context,
    IMessageCatalog messages,
    INumberSeriesService numbers,
    ISetupService setup,
    IClock clock,
    ILogger<EmployeeService> logger)
{
    /// <summary>Everybody, most recently hired first.</summary>
    /// <param name="includeLeavers">Whether to include people who have gone.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The employees, with their positions and branch history.</returns>
    public async Task<IReadOnlyList<Employee>> ListAsync(
        bool includeLeavers = false,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<Employee>()
            .AsNoTracking()
            .Include(e => e.Position)
            .Include(e => e.BranchAssignments)
            .AsQueryable();

        if (!includeLeavers)
        {
            query = query.Where(static e => e.Status != EmploymentStatus.Left);
        }

        return await query
            .OrderByDescending(static e => e.HiredOn)
            .ThenBy(static e => e.No)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads one employee.</summary>
    /// <param name="employeeNo">Their number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The employee, or null when nobody is numbered that.</returns>
    public Task<Employee?> LoadAsync(string employeeNo, CancellationToken cancellationToken = default)
        => context.Set<Employee>()
            .Include(e => e.Position)
            .Include(e => e.BranchAssignments)
            .FirstOrDefaultAsync(e => e.No == employeeNo, cancellationToken);

    /// <summary>
    /// Hires somebody.
    /// </summary>
    /// <param name="employee">Who, and on what terms.</param>
    /// <param name="branchId">Where they will work from their first day.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The employee, or every reason they could not be hired.</returns>
    public async Task<Result<Employee>> HireAsync(
        Employee employee,
        Guid? branchId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);

        var found = new List<AsapMessage>();

        if (employee.BasicWage < 0m || employee.Allowances < 0m)
        {
            found.Add(messages.Render(
                HrMessages.WageNegative,
                Args(
                    ("EmployeeNo", employee.No),
                    ("BasicWage", employee.BasicWage),
                    ("Allowances", employee.Allowances))));
        }

        if (employee.LeftOn is { } left && left < employee.HiredOn)
        {
            found.Add(messages.Render(
                HrMessages.LeftBeforeHired,
                Args(
                    ("EmployeeNo", employee.No),
                    ("HiredOn", employee.HiredOn),
                    ("LeftOn", left))));
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<Employee>.Failure(found);
        }

        if (string.IsNullOrWhiteSpace(employee.No))
        {
            var series = await setup
                .GetAsync<string>($"{HrModule.Id}.Employees.NumberSeries", cancellationToken)
                .ConfigureAwait(false) ?? "EMP";

            var numbered = await numbers
                .NextAsync(series, employee.HiredOn, cancellationToken)
                .ConfigureAwait(false);

            if (numbered.Failed)
            {
                return Result<Employee>.FailureFrom(numbered);
            }

            employee.No = numbered.Value;
        }
        else
        {
            var taken = await context.Set<Employee>()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.No == employee.No, cancellationToken)
                .ConfigureAwait(false);

            if (taken is not null)
            {
                return Result<Employee>.Failure(messages.Render(
                    HrMessages.EmployeeNumberTaken,
                    Args(("EmployeeNo", employee.No), ("ExistingName", taken.Name))));
            }
        }

        // Somebody hired with a start date in the past is already working, and one hired for next
        // month is not. Reading the date rather than making the caller say so means a screen
        // cannot get the two out of step.
        employee.Status = employee.HiredOn <= clock.Today
            ? EmploymentStatus.Active
            : EmploymentStatus.Pending;

        if (branchId is { } branch)
        {
            employee.BranchAssignments.Add(new BranchAssignment
            {
                TenantId = employee.TenantId,
                CompanyId = employee.CompanyId,
                BranchId = branch,
                FromDate = employee.HiredOn,
            });
        }

        context.Set<Employee>().Add(employee);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Hired {EmployeeNo} {Name} from {HiredOn}.",
            employee.No,
            employee.Name,
            employee.HiredOn);

        return Result<Employee>.Success(employee, found);
    }

    /// <summary>
    /// Moves somebody to another branch from a date.
    /// </summary>
    /// <remarks>
    /// Ends the assignment they are on the day before, so the two meet exactly. Payroll splits a
    /// month on this boundary, and a day on both sides or neither is a day charged twice or not
    /// at all.
    /// </remarks>
    /// <param name="employeeNo">Who is moving.</param>
    /// <param name="branchId">Where to.</param>
    /// <param name="fromDate">Their first day there.</param>
    /// <param name="reason">Why, for the record.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The employee, or every reason the transfer was refused.</returns>
    public async Task<Result<Employee>> TransferAsync(
        string employeeNo,
        Guid branchId,
        DateOnly fromDate,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var employee = await LoadAsync(employeeNo, cancellationToken).ConfigureAwait(false);

        if (employee is null)
        {
            return Result<Employee>.Failure(NotFound(employeeNo));
        }

        var found = new List<AsapMessage>();

        var arguments = Args(
            ("EmployeeNo", employee.No),
            ("FromDate", fromDate),
            ("HiredOn", employee.HiredOn));

        if (fromDate < employee.HiredOn)
        {
            return Result<Employee>.Failure(
                messages.Render(HrMessages.AssignmentBeforeHiring, arguments));
        }

        var ordered = employee.BranchAssignments.OrderBy(static a => a.FromDate).ToList();
        var current = ordered.LastOrDefault();

        if (current is not null)
        {
            if (current.FromDate >= fromDate)
            {
                arguments["ExistingFrom"] = current.FromDate;

                return Result<Employee>.Failure(
                    messages.Render(HrMessages.AssignmentOverlaps, arguments));
            }

            // The previous assignment ends the day before this one starts, whatever it said
            // before. A transfer is the thing that closes it.
            var lastDay = fromDate.AddDays(-1);

            if (current.ToDate is { } existingEnd && existingEnd < lastDay)
            {
                arguments["GapFrom"] = existingEnd.AddDays(1);
                arguments["GapTo"] = lastDay;

                found.Add(messages.Render(HrMessages.AssignmentLeavesGap, arguments));
            }

            current.ToDate = lastDay;
        }

        employee.BranchAssignments.Add(new BranchAssignment
        {
            TenantId = employee.TenantId,
            CompanyId = employee.CompanyId,
            BranchId = branchId,
            FromDate = fromDate,
            Reason = reason,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Transferred {EmployeeNo} to branch {BranchId} from {FromDate}.",
            employee.No,
            branchId,
            fromDate);

        return Result<Employee>.Success(employee, found);
    }

    /// <summary>
    /// Records that somebody has left.
    /// </summary>
    /// <remarks>
    /// The reason is required, and not as bookkeeping: a resignation is worth a fraction of a
    /// termination under the law, and by a great deal. Letting it default would quietly decide
    /// somebody's award.
    /// </remarks>
    /// <param name="employeeNo">Who.</param>
    /// <param name="leftOn">Their last day.</param>
    /// <param name="reason">Why.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What they are owed, or every reason it could not be recorded.</returns>
    public async Task<Result<EndOfServiceAward>> RecordLeavingAsync(
        string employeeNo,
        DateOnly leftOn,
        LeavingReason reason,
        CancellationToken cancellationToken = default)
    {
        var employee = await LoadAsync(employeeNo, cancellationToken).ConfigureAwait(false);

        if (employee is null)
        {
            return Result<EndOfServiceAward>.Failure(NotFound(employeeNo));
        }

        var arguments = Args(
            ("EmployeeNo", employee.No),
            ("HiredOn", employee.HiredOn),
            ("LeftOn", leftOn));

        if (leftOn < employee.HiredOn)
        {
            return Result<EndOfServiceAward>.Failure(
                messages.Render(HrMessages.LeftBeforeHired, arguments));
        }

        if (reason is LeavingReason.None)
        {
            return Result<EndOfServiceAward>.Failure(
                messages.Render(HrMessages.LeavingReasonMissing, arguments));
        }

        employee.LeftOn = leftOn;
        employee.LeavingReason = reason;
        employee.Status = EmploymentStatus.Left;

        // The last branch assignment closes with them. Left open it would say somebody who has
        // gone is still costing a branch money.
        var current = employee.BranchAssignments
            .OrderBy(static a => a.FromDate)
            .LastOrDefault(static a => a.ToDate is null);

        if (current is not null)
        {
            current.ToDate = leftOn;
        }

        var award = EndOfServiceCalculator.For(employee, leftOn, reason: reason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "{EmployeeNo} left on {LeftOn} ({Reason}); award {Award}.",
            employee.No,
            leftOn,
            reason,
            award.Award);

        return Result<EndOfServiceAward>.Success(award);
    }

    /// <summary>
    /// Which branch somebody was at on a day.
    /// </summary>
    /// <remarks>
    /// What payroll asks, once per day of the month rather than once per month, which is the
    /// whole reason the assignments are a history.
    /// </remarks>
    /// <param name="employee">The employee, with their assignments loaded.</param>
    /// <param name="on">The day.</param>
    /// <returns>The branch, or null when nobody was responsible for them that day.</returns>
    public static Guid? BranchOn(Employee employee, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(employee);

        return employee.BranchAssignments.FirstOrDefault(a => a.Covers(on))?.BranchId;
    }

    /// <summary>
    /// What the company owes everybody who works for it, today.
    /// </summary>
    /// <remarks>
    /// Computed for current staff rather than only for leavers, because that is what a liability
    /// is. A company that recognises end-of-service on the day somebody resigns has overstated
    /// its profit every year until then.
    /// <para>
    /// Measured as though the company let them go, which is the larger figure. A provision built
    /// on the hope that everybody resigns early is not a provision.
    /// </para>
    /// </remarks>
    /// <param name="on">The day to measure at, or null for today.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per employee, most owed first.</returns>
    public async Task<IReadOnlyList<EmployeeEntitlement>> EntitlementsAsync(
        DateOnly? on = null,
        CancellationToken cancellationToken = default)
    {
        var day = on ?? clock.Today;

        var employees = await context.Set<Employee>()
            .AsNoTracking()
            .Where(static e => e.Status != EmploymentStatus.Left)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Read once for everybody rather than once per employee inside the loop below, the same
        // reason the branch history is loaded eagerly: a report over the whole staff list is
        // exactly the query an N+1 read hides in until the list is long enough to notice.
        //
        // Grouped and summed after the fact rather than in the query: Days is a computed property
        // over two DateOnly columns, and EF Core cannot always turn that into SQL. Reading the raw
        // rows and folding them in memory is what every other day-count report here does -- see
        // AgedAnalysisQuery.
        var takenByEmployee = (await context.Set<Entitlements.LeaveRecord>()
                .AsNoTracking()
                .Where(r => r.FromDate <= day)
                .Select(static r => new { r.EmployeeId, r.FromDate, r.ToDate })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(static r => r.EmployeeId)
            .ToDictionary(
                static g => g.Key,
                static g => g.Sum(static r => (decimal)(r.ToDate.DayNumber - r.FromDate.DayNumber + 1)));

        var rows = new List<EmployeeEntitlement>();

        foreach (var employee in employees)
        {
            var earned = LeaveAccrual.EarnedBetween(employee, employee.HiredOn, day);
            var taken = takenByEmployee.GetValueOrDefault(employee.Id);

            var balance = LeaveAccrual.Balance(earned, taken);
            var leaveWorth = LeaveAccrual.Liability(employee, balance.BalanceDays);
            var award = EndOfServiceCalculator.For(employee, day);

            rows.Add(new EmployeeEntitlement(
                employee.No,
                employee.Name,
                Math.Round(employee.ServiceYearsOn(day), 2, MidpointRounding.AwayFromZero),
                balance.BalanceDays,
                leaveWorth,
                award.Award,
                Math.Round(leaveWorth + award.Award, 2, MidpointRounding.AwayFromZero)));
        }

        return [.. rows.OrderByDescending(static r => r.TotalOwed)];
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
