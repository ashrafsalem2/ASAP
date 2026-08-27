using ASAP.Modules.Hr.Entitlements;
using ASAP.Modules.Hr.People;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Hr.Leave;

/// <summary>What one employee has earned, taken and has left.</summary>
/// <param name="EmployeeNo">Who.</param>
/// <param name="Name">Their name.</param>
/// <param name="AsAt">The day it was measured.</param>
/// <param name="EarnedDays">Days of annual leave accrued since they were hired.</param>
/// <param name="TakenDays">Days of annual leave approved.</param>
/// <param name="BalanceDays">What is left. Negative where leave was granted in advance.</param>
/// <param name="Liability">What the balance is worth in money.</param>
public readonly record struct LeaveEntitlement(
    string EmployeeNo,
    string Name,
    DateOnly AsAt,
    decimal EarnedDays,
    decimal TakenDays,
    decimal BalanceDays,
    decimal Liability);

/// <summary>
/// The leave register: who asked to be away, what was decided, and what is left.
/// </summary>
/// <remarks>
/// <para>
/// The half the entitlement calculation was missing. Leave earned is easy and was already right;
/// leave <em>remaining</em> needs a record of what was taken, and without one the liability report
/// showed everything anybody had ever accrued — an upper bound presented as a total, wrong in the
/// company's favour every year until somebody leaves and asks for what they are owed.
/// </para>
/// <para>
/// Only approved leave counts. A submitted request is a question, not an absence, and deducting
/// it from a balance would mean somebody could exhaust their leave by asking for it.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="numbers">Issues request numbers.</param>
/// <param name="setup">Supplies the number series.</param>
/// <param name="userContext">Records who decided.</param>
/// <param name="clock">Supplies the time.</param>
/// <param name="logger">Records what was decided.</param>
public sealed class LeaveService(
    AsapDbContext context,
    IMessageCatalog messages,
    INumberSeriesService numbers,
    ISetupService setup,
    IUserContext userContext,
    IClock clock,
    ILogger<LeaveService> logger)
{
    /// <summary>Lists leave requests, most recent first.</summary>
    /// <param name="employeeNo">One employee, or null for everybody.</param>
    /// <param name="from">Only requests touching this day or later.</param>
    /// <param name="to">Only requests touching this day or earlier.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requests.</returns>
    public async Task<IReadOnlyList<LeaveRequest>> ListAsync(
        string? employeeNo = null,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<LeaveRequest>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(employeeNo))
        {
            query = query.Where(r => r.EmployeeNo == employeeNo);
        }

        if (from is { } start)
        {
            query = query.Where(r => r.ToDate >= start);
        }

        if (to is { } end)
        {
            query = query.Where(r => r.FromDate <= end);
        }

        return await query
            .OrderByDescending(static r => r.FromDate)
            .ThenBy(static r => r.EmployeeNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads one request.</summary>
    /// <param name="requestNo">Its number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or null when nothing is numbered that.</returns>
    public Task<LeaveRequest?> LoadAsync(
        string requestNo,
        CancellationToken cancellationToken = default)
        => context.Set<LeaveRequest>().FirstOrDefaultAsync(r => r.No == requestNo, cancellationToken);

    /// <summary>
    /// Asks for leave.
    /// </summary>
    /// <param name="employeeNo">Who is asking.</param>
    /// <param name="kind">What kind of leave.</param>
    /// <param name="from">First day away.</param>
    /// <param name="to">Last day away.</param>
    /// <param name="reason">Why, in their words.</param>
    /// <param name="submit">Whether to ask straight away rather than leave it as a draft.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or every reason it could not be made.</returns>
    public async Task<Result<LeaveRequest>> RequestAsync(
        string employeeNo,
        LeaveKind kind,
        DateOnly from,
        DateOnly to,
        string? reason = null,
        bool submit = true,
        CancellationToken cancellationToken = default)
    {
        var employee = await context.Set<Employee>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.No == employeeNo, cancellationToken)
            .ConfigureAwait(false);

        if (employee is null)
        {
            return Result<LeaveRequest>.Failure(messages.Render(
                HrMessages.EmployeeNotFound,
                Args(("EmployeeNo", employeeNo))));
        }

        var arguments = Args(
            ("EmployeeNo", employee.No),
            ("FromDate", from),
            ("ToDate", to),
            ("HiredOn", employee.HiredOn));

        if (to < from)
        {
            return Result<LeaveRequest>.Failure(
                messages.Render(HrMessages.LeaveEndsBeforeItStarts, arguments));
        }

        if (from < employee.HiredOn || (employee.LeftOn is { } left && to > left))
        {
            // Written as a clause rather than a second message, so the sentence reads naturally
            // whether or not they have left.
            arguments["LeftClause"] = employee.LeftOn is { } leftOn
                ? $" and left on {leftOn:yyyy-MM-dd}"
                : string.Empty;

            return Result<LeaveRequest>.Failure(
                messages.Render(HrMessages.LeaveOutsideEmployment, arguments));
        }

        var clash = await context.Set<LeaveRequest>()
            .AsNoTracking()
            .Where(r => r.EmployeeId == employee.Id
                        && r.Status != LeaveStatus.Rejected
                        && r.Status != LeaveStatus.Cancelled
                        && r.FromDate <= to
                        && r.ToDate >= from)
            .OrderBy(static r => r.FromDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (clash is not null)
        {
            arguments["ExistingNo"] = clash.No;
            arguments["ExistingFrom"] = clash.FromDate;
            arguments["ExistingTo"] = clash.ToDate;

            return Result<LeaveRequest>.Failure(
                messages.Render(HrMessages.LeaveOverlaps, arguments));
        }

        var series = await setup
            .GetAsync<string>($"{HrModule.Id}.Leave.NumberSeries", cancellationToken)
            .ConfigureAwait(false) ?? "LEAVE";

        var numbered = await numbers.NextAsync(series, clock.Today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<LeaveRequest>.FailureFrom(numbered);
        }

        var request = new LeaveRequest
        {
            TenantId = employee.TenantId,
            CompanyId = employee.CompanyId,
            No = numbered.Value,
            EmployeeId = employee.Id,
            EmployeeNo = employee.No,
            EmployeeName = employee.Name,
            Kind = kind,
            FromDate = from,
            ToDate = to,
            Reason = reason,
            Status = submit ? LeaveStatus.Submitted : LeaveStatus.Draft,
        };

        var found = new List<AsapMessage>();

        // Said when the request is made rather than when it is granted, so whoever decides sees it
        // in the same breath as the request. Annual leave only: nothing else draws on a balance.
        if (LeaveKindPolicy.For(kind).DrawsOnAnnualBalance)
        {
            var balance = await BalanceAsync(employee, from.AddDays(-1), cancellationToken)
                .ConfigureAwait(false);

            if (request.Days > balance.BalanceDays)
            {
                arguments["Days"] = (decimal)request.Days;
                arguments["BalanceDays"] = balance.BalanceDays;

                found.Add(messages.Render(HrMessages.LeaveExceedsBalance, arguments));
            }
        }

        context.Set<LeaveRequest>().Add(request);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Leave {RequestNo}: {EmployeeNo} asked for {Days} days of {Kind} from {FromDate}.",
            request.No,
            request.EmployeeNo,
            request.Days,
            request.Kind,
            request.FromDate);

        return Result<LeaveRequest>.Success(request, found);
    }

    /// <summary>Grants a request.</summary>
    /// <param name="requestNo">Which one.</param>
    /// <param name="note">What the decider wants recorded.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or why it could not be granted.</returns>
    public Task<Result<LeaveRequest>> ApproveAsync(
        string requestNo,
        string? note = null,
        CancellationToken cancellationToken = default)
        => DecideAsync(requestNo, LeaveStatus.Approved, note, cancellationToken);

    /// <summary>Refuses a request.</summary>
    /// <param name="requestNo">Which one.</param>
    /// <param name="note">What the decider wants recorded.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or why it could not be refused.</returns>
    public Task<Result<LeaveRequest>> RejectAsync(
        string requestNo,
        string? note = null,
        CancellationToken cancellationToken = default)
        => DecideAsync(requestNo, LeaveStatus.Rejected, note, cancellationToken);

    /// <summary>
    /// Withdraws a request.
    /// </summary>
    /// <remarks>
    /// Allowed even once granted. Somebody who was given the week off and came in anyway should
    /// not be recorded as having taken it, and the alternative — editing the dates of a decision
    /// somebody was told about — loses the fact that it was ever granted.
    /// </remarks>
    /// <param name="requestNo">Which one.</param>
    /// <param name="note">Why it was withdrawn.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or why it could not be withdrawn.</returns>
    public Task<Result<LeaveRequest>> CancelAsync(
        string requestNo,
        string? note = null,
        CancellationToken cancellationToken = default)
        => DecideAsync(requestNo, LeaveStatus.Cancelled, note, cancellationToken);

    /// <summary>
    /// What one employee has earned, taken and has left.
    /// </summary>
    /// <param name="employeeNo">Who.</param>
    /// <param name="on">The day to measure at, or null for today.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Their balance, or why it could not be worked out.</returns>
    public async Task<Result<LeaveEntitlement>> EntitlementAsync(
        string employeeNo,
        DateOnly? on = null,
        CancellationToken cancellationToken = default)
    {
        var employee = await context.Set<Employee>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.No == employeeNo, cancellationToken)
            .ConfigureAwait(false);

        if (employee is null)
        {
            return Result<LeaveEntitlement>.Failure(messages.Render(
                HrMessages.EmployeeNotFound,
                Args(("EmployeeNo", employeeNo))));
        }

        var day = on ?? clock.Today;
        var balance = await BalanceAsync(employee, day, cancellationToken).ConfigureAwait(false);

        return Result<LeaveEntitlement>.Success(new LeaveEntitlement(
            employee.No,
            employee.Name,
            day,
            balance.EarnedDays,
            balance.TakenDays,
            balance.BalanceDays,
            LeaveAccrual.Liability(employee, balance.BalanceDays)));
    }

    /// <summary>
    /// How many days of annual leave everybody has taken up to a day.
    /// </summary>
    /// <remarks>
    /// One query for the whole company rather than one per person, because the liability report
    /// asks about every employee at once and would otherwise be a round trip each.
    /// </remarks>
    /// <param name="on">The last day to count.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Days taken per employee. Somebody who has taken none is absent.</returns>
    public async Task<Dictionary<Guid, decimal>> AnnualTakenByEmployeeAsync(
        DateOnly on,
        CancellationToken cancellationToken = default)
    {
        var approved = await context.Set<LeaveRequest>()
            .AsNoTracking()
            .Where(r => r.Status == LeaveStatus.Approved
                        && r.Kind == LeaveKind.Annual
                        && r.FromDate <= on)
            .Select(static r => new { r.EmployeeId, r.FromDate, r.ToDate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var taken = new Dictionary<Guid, decimal>();

        foreach (var request in approved)
        {
            // A request running past the day being measured counts only up to it. Leave somebody
            // is on right now is half taken, and reporting the whole of it would overstate what
            // they have used and understate what they are owed.
            var end = request.ToDate > on ? on : request.ToDate;
            var days = end.DayNumber - request.FromDate.DayNumber + 1;

            if (days <= 0)
            {
                continue;
            }

            taken[request.EmployeeId] = taken.GetValueOrDefault(request.EmployeeId) + days;
        }

        return taken;
    }

    /// <summary>
    /// What leave costs somebody over a period: the days, and how many of them are unpaid.
    /// </summary>
    /// <remarks>
    /// Payroll's side of the register. Sick leave past its first thirty days is paid at three
    /// quarters and past ninety at nothing, and a payroll that ignored that would pay in full for
    /// an absence the law does not require to be paid at all.
    /// </remarks>
    /// <param name="employeeId">Who.</param>
    /// <param name="from">First day of the period.</param>
    /// <param name="to">Last day of the period.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Days away, and how many of them carry no pay.</returns>
    public async Task<LeavePay> UnpaidWithinAsync(
        Guid employeeId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var everybody = await UnpaidByEmployeeAsync(from, to, employeeId, cancellationToken)
            .ConfigureAwait(false);

        return everybody.GetValueOrDefault(employeeId);
    }

    /// <summary>
    /// The same, for everybody at once.
    /// </summary>
    /// <remarks>
    /// A payroll run asks this about every employee in the company. One query and a walk in
    /// memory rather than a round trip each, which is the difference between a run that takes a
    /// second and one that takes a minute on a few hundred people.
    /// </remarks>
    /// <param name="from">First day of the period.</param>
    /// <param name="to">Last day of the period.</param>
    /// <param name="employeeId">One employee, or null for everybody.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Days away and how many carry no pay, per employee. Nobody away is absent.</returns>
    public async Task<Dictionary<Guid, LeavePay>> UnpaidByEmployeeAsync(
        DateOnly from,
        DateOnly to,
        Guid? employeeId = null,
        CancellationToken cancellationToken = default)
    {
        // The leave year the period falls in, because the pay bands are cumulative across it.
        var yearStart = new DateOnly(to.Year, 1, 1);

        var query = context.Set<LeaveRequest>()
            .AsNoTracking()
            .Where(r => r.Status == LeaveStatus.Approved
                        && r.ToDate >= yearStart
                        && r.FromDate <= to);

        if (employeeId is { } one)
        {
            query = query.Where(r => r.EmployeeId == one);
        }

        var approved = await query
            .OrderBy(static r => r.EmployeeId)
            .ThenBy(static r => r.FromDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<Guid, LeavePay>();

        foreach (var group in approved.GroupBy(static r => r.EmployeeId))
        {
            var pay = Walk(group, yearStart, from, to);

            if (pay.Days > 0m)
            {
                result[group.Key] = pay;
            }
        }

        return result;
    }

    private static LeavePay Walk(
        IEnumerable<LeaveRequest> requests,
        DateOnly yearStart,
        DateOnly from,
        DateOnly to)
    {
        var consumed = new Dictionary<LeaveKind, decimal>();
        var days = 0m;
        var paid = 0m;

        foreach (var request in requests)
        {
            var policy = LeaveKindPolicy.For(request.Kind);
            var already = consumed.GetValueOrDefault(request.Kind);

            // Days of this request that fall before the period move the band along without being
            // charged to this payroll: they were paid, at their own rate, in an earlier run.
            var before = request.DaysWithin(yearStart, from.AddDays(-1));
            var inside = request.DaysWithin(from, to);

            if (before > 0)
            {
                already += before;
                consumed[request.Kind] = already;
            }

            if (inside <= 0)
            {
                continue;
            }

            var pay = LeavePayCalculator.For(policy, inside, already);

            days += pay.Days;
            paid += pay.PaidDays;
            consumed[request.Kind] = already + inside;
        }

        return new LeavePay(
            days,
            Math.Round(paid, 2, MidpointRounding.AwayFromZero),
            Math.Round(days - paid, 2, MidpointRounding.AwayFromZero));
    }

    private async Task<LeaveBalance> BalanceAsync(
        Employee employee,
        DateOnly on,
        CancellationToken cancellationToken)
    {
        var earned = LeaveAccrual.EarnedBetween(employee, employee.HiredOn, on);

        var taken = await context.Set<LeaveRequest>()
            .AsNoTracking()
            .Where(r => r.EmployeeId == employee.Id
                        && r.Status == LeaveStatus.Approved
                        && r.Kind == LeaveKind.Annual
                        && r.FromDate <= on)
            .Select(static r => new { r.FromDate, r.ToDate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var takenDays = 0m;

        foreach (var request in taken)
        {
            var end = request.ToDate > on ? on : request.ToDate;

            takenDays += end.DayNumber - request.FromDate.DayNumber + 1;
        }

        return LeaveAccrual.Balance(earned, takenDays);
    }

    private async Task<Result<LeaveRequest>> DecideAsync(
        string requestNo,
        LeaveStatus status,
        string? note,
        CancellationToken cancellationToken)
    {
        var request = await LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return Result<LeaveRequest>.Failure(messages.Render(
                HrMessages.LeaveNotFound,
                Args(("RequestNo", requestNo))));
        }

        // Cancelling is allowed from anywhere except a request already finished with. Granting
        // and refusing are decisions, and a decision is made once.
        var alreadyDecided = status is LeaveStatus.Cancelled
            ? request.Status is LeaveStatus.Cancelled or LeaveStatus.Rejected
            : !request.IsEditable;

        if (alreadyDecided)
        {
            return Result<LeaveRequest>.Failure(messages.Render(
                HrMessages.LeaveAlreadyDecided,
                Args(("RequestNo", request.No), ("Status", request.Status.ToString()))));
        }

        request.Status = status;
        request.DecisionNote = note;
        request.DecidedBy = userContext.UserId;
        request.DecidedAtUtc = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Leave {RequestNo} for {EmployeeNo} is now {Status}.",
            request.No,
            request.EmployeeNo,
            status);

        return Result<LeaveRequest>.Success(request);
    }

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
