using ASAP.Api.Infrastructure;
using ASAP.Modules.Hr.Attendance;
using ASAP.Modules.Hr.Entitlements;
using ASAP.Modules.Hr.Leave;
using ASAP.Platform.Kernel.Results;
using ASAP.Modules.Hr.Payroll;
using ASAP.Modules.Hr.People;
using ASAP.Modules.Hr.Reporting;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Time;
using Microsoft.AspNetCore.Mvc;

namespace ASAP.Api.Endpoints;

/// <summary>What a provision run posted.</summary>
/// <param name="AsOf">The day it was measured at.</param>
/// <param name="LeaveTotal">What the company owes in unused leave, in total.</param>
/// <param name="LeaveMovement">How much that moved, and so how much was posted.</param>
/// <param name="EndOfServiceTotal">
/// What the company owes in end-of-service. Reported here, posted by the payroll run.
/// </param>
/// <param name="TransactionNo">What it was posted under, or null when nothing had moved.</param>
public sealed record ProvisionPostingView(
    DateOnly AsOf,
    decimal LeaveTotal,
    decimal LeaveMovement,
    decimal EndOfServiceTotal,
    long? TransactionNo);

/// <summary>How many people are at one branch, as it is reported back.</summary>
public sealed record HeadcountView(Guid? BranchId, string? BranchCode, string? BranchName, int Count);

/// <summary>What one branch's staff cost, as it is reported back.</summary>
public sealed record BranchCostView(
    Guid? BranchId, string? BranchCode, string? BranchName, int Count, decimal MonthlyWageCost);

/// <summary>How many people came and went over a period, as it is reported back.</summary>
/// <param name="FromDate">The first day of the period.</param>
/// <param name="ToDate">The last day of it.</param>
/// <param name="OpeningHeadcount">How many people there were at the start.</param>
/// <param name="Hired">How many started during it.</param>
/// <param name="Left">How many went during it.</param>
/// <param name="ClosingHeadcount">How many there were at the end.</param>
/// <param name="TurnoverRate">Leavers as a share of the average headcount.</param>
public sealed record TurnoverView(
    DateOnly FromDate,
    DateOnly ToDate,
    int OpeningHeadcount,
    int Hired,
    int Left,
    int ClosingHeadcount,
    decimal TurnoverRate);

/// <summary>What a client sends to hire somebody.</summary>
/// <param name="Name">Their name.</param>
/// <param name="HiredOn">The day they start.</param>
/// <param name="NameArabic">Their name in Arabic.</param>
/// <param name="No">Their number, or null to take the next from the series.</param>
/// <param name="NationalId">The national or residence identity number.</param>
/// <param name="Nationality">Nationality.</param>
/// <param name="BasicWage">The basic wage for one pay period.</param>
/// <param name="Allowances">Housing, transport and the rest.</param>
/// <param name="BranchId">Where they will work from their first day.</param>
/// <param name="PositionId">The job they hold.</param>
public sealed record HireRequest(
    string Name,
    DateOnly HiredOn,
    string? NameArabic = null,
    string? No = null,
    string? NationalId = null,
    string? Nationality = null,
    decimal BasicWage = 0m,
    decimal Allowances = 0m,
    Guid? BranchId = null,
    Guid? PositionId = null);

/// <summary>What a client sends to move somebody to another branch.</summary>
/// <param name="BranchId">Where to.</param>
/// <param name="FromDate">Their first day there.</param>
/// <param name="Reason">Why, for the record.</param>
public sealed record TransferRequest(Guid BranchId, DateOnly FromDate, string? Reason = null);

/// <summary>What a client sends to record that somebody has left.</summary>
/// <param name="LeftOn">Their last day.</param>
/// <param name="Reason">
/// Why. Required, and an input to the award rather than a note about it: a resignation is worth a
/// fraction of a termination.
/// </param>
public sealed record LeavingRequest(DateOnly LeftOn, LeavingReason Reason);

/// <summary>What a client sends to ask for leave.</summary>
/// <param name="EmployeeNo">Who is asking.</param>
/// <param name="Kind">What kind of leave.</param>
/// <param name="FromDate">First day away.</param>
/// <param name="ToDate">Last day away.</param>
/// <param name="Reason">Why, in their words.</param>
/// <param name="Submit">Whether to ask straight away rather than keep it as a draft.</param>
public sealed record LeaveRequestInput(
    string EmployeeNo,
    LeaveKind Kind,
    DateOnly FromDate,
    DateOnly ToDate,
    string? Reason = null,
    bool Submit = true);

/// <summary>What a client sends when deciding on leave.</summary>
/// <param name="Note">What the decider wants recorded.</param>
public sealed record LeaveDecisionInput(string? Note = null);

/// <summary>What a client sends to post a payroll run.</summary>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record PostPayrollRequest(string? OverrideReason = null);

/// <summary>What a client sends to work out a payroll run.</summary>
/// <param name="From">The first day of the period.</param>
/// <param name="To">The last day.</param>
/// <param name="PostingDate">The date to post under, or null for the last day.</param>
/// <param name="Description">What to call it.</param>
public sealed record CalculatePayrollRequest(
    DateOnly From,
    DateOnly To,
    DateOnly? PostingDate = null,
    string? Description = null);

/// <summary>A working pattern: when it starts, when it ends, which days it runs.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="StartsAt">When it starts.</param>
/// <param name="EndsAt">When it ends.</param>
/// <param name="BreakMinutes">Unpaid break in the middle of it.</param>
/// <param name="DaysOfWeek">Which days it runs, as a bit per day with Sunday as 1.</param>
/// <param name="GraceMinutes">Minutes after the start that are not counted as late.</param>
/// <param name="PaidMinutes">How long it is, less the break.</param>
/// <param name="CrossesMidnight">Whether it finishes the day after it starts.</param>
/// <param name="IsActive">Whether anybody may still be put on it.</param>
public sealed record ShiftView(
    string Code,
    string Name,
    string? NameArabic,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    int BreakMinutes,
    int DaysOfWeek,
    int GraceMinutes,
    int PaidMinutes,
    bool CrossesMidnight,
    bool IsActive);

/// <summary>Which shift somebody is on, from a date.</summary>
/// <param name="EmployeeNo">Whose it is.</param>
/// <param name="ShiftCode">The shift.</param>
/// <param name="FromDate">The first day it applies.</param>
/// <param name="ToDate">The last day it applies, or null where it still stands.</param>
public sealed record ShiftAssignmentView(
    string EmployeeNo,
    string ShiftCode,
    DateOnly FromDate,
    DateOnly? ToDate);

/// <summary>What one person did on one day.</summary>
/// <param name="EmployeeNo">Whose day.</param>
/// <param name="OnDate">The day.</param>
/// <param name="ShiftCode">The shift they were on.</param>
/// <param name="ClockedInAt">When they clocked in.</param>
/// <param name="ClockedOutAt">When they clocked out.</param>
/// <param name="Status">How the day turned out.</param>
/// <param name="WorkedMinutes">Minutes actually worked, less the break.</param>
/// <param name="LateMinutes">Minutes late past the grace.</param>
/// <param name="EarlyLeaveMinutes">Minutes left before the shift ended.</param>
/// <param name="OvertimeMinutes">Minutes worked beyond the shift.</param>
/// <param name="Note">Why it reads as it does.</param>
public sealed record AttendanceView(
    string EmployeeNo,
    DateOnly OnDate,
    string? ShiftCode,
    TimeOnly? ClockedInAt,
    TimeOnly? ClockedOutAt,
    AttendanceStatus Status,
    int WorkedMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    int OvertimeMinutes,
    string? Note);

/// <summary>Putting somebody on a shift from a date.</summary>
/// <param name="ShiftCode">The shift.</param>
/// <param name="FromDate">The first day it applies.</param>
public sealed record AssignShiftRequest(string ShiftCode, DateOnly FromDate);

/// <summary>A day of attendance as somebody sends it in.</summary>
/// <param name="OnDate">The day.</param>
/// <param name="ClockedInAt">When they clocked in.</param>
/// <param name="ClockedOutAt">When they clocked out.</param>
/// <param name="Note">Why it reads as it does.</param>
/// <param name="Amend">Whether to replace a record already there rather than refuse.</param>
public sealed record RecordAttendanceRequest(
    DateOnly OnDate,
    TimeOnly? ClockedInAt = null,
    TimeOnly? ClockedOutAt = null,
    string? Note = null,
    bool Amend = false);

/// <summary>What somebody was engaged on, over a stretch of time.</summary>
/// <param name="Id">The contract's key, for amending it.</param>
/// <param name="EmployeeNo">Whose it is.</param>
/// <param name="StartsOn">The first day it covers.</param>
/// <param name="EndsOn">The last day it covers, or null where it is open-ended.</param>
/// <param name="Kind">What kind of engagement it is.</param>
/// <param name="BasicWage">The basic wage for one pay period.</param>
/// <param name="Allowances">Housing, transport and the rest.</param>
/// <param name="PayFrequency">How often it pays.</param>
/// <param name="Reference">The paper contract's own reference.</param>
/// <param name="SignedOn">When it was signed.</param>
/// <param name="RecordedByUserName">Who recorded it.</param>
/// <param name="Reason">Why it was raised.</param>
public sealed record EmploymentContractView(
    Guid Id,
    string EmployeeNo,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    ContractKind Kind,
    decimal BasicWage,
    decimal Allowances,
    PayFrequency PayFrequency,
    string? Reference,
    DateOnly? SignedOn,
    string? RecordedByUserName,
    string? Reason);

/// <summary>A contract as somebody sends it in.</summary>
/// <param name="StartsOn">The first day it covers.</param>
/// <param name="BasicWage">The basic wage for one pay period.</param>
/// <param name="Allowances">Housing, transport and the rest.</param>
/// <param name="Kind">What kind of engagement it is.</param>
/// <param name="EndsOn">The last day it covers, on a fixed term.</param>
/// <param name="PayFrequency">How often it pays.</param>
/// <param name="Reference">The paper contract's own reference.</param>
/// <param name="SignedOn">When it was signed.</param>
/// <param name="Reason">Why it was raised.</param>
/// <param name="Supersede">
/// Whether to close the contract before it the day before this one starts. What a raise, a
/// promotion or a renewal actually is.
/// </param>
public sealed record RecordContractRequest(
    DateOnly StartsOn,
    decimal BasicWage,
    decimal Allowances = 0m,
    ContractKind Kind = ContractKind.Permanent,
    DateOnly? EndsOn = null,
    PayFrequency PayFrequency = PayFrequency.Monthly,
    string? Reference = null,
    DateOnly? SignedOn = null,
    string? Reason = null,
    bool Supersede = false);

/// <summary>Employees, where they work, and what they are owed.</summary>
public static class HrEndpoints
{
    private const string ReadPermission = "Hr.Employee.Read";
    private const string HirePermission = "Hr.Employee.Create";
    private const string UpdatePermission = "Hr.Employee.Update";
    private const string WageReadPermission = "Hr.Wage.Read";
    private const string WageUpdatePermission = "Hr.Wage.Update";
    private const string LeaveReadPermission = "Hr.Leave.Read";
    private const string LeaveCreatePermission = "Hr.Leave.Create";
    private const string LeaveApprovePermission = "Hr.Leave.Approve";
    private const string ReportPermission = "Hr.Report.Read";
    private const string ProvisionPermission = "Hr.Provision.Post";

    /// <summary>Maps the human resources endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHrEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/hr").RequireAuthorization().WithTags("Human resources");

        group.MapGet("/employees", ListAsync)
             .WithName("Employees")
             .WithSummary("Lists employees, most recently hired first.");

        group.MapGet("/employees/{employeeNo}", GetAsync)
             .WithName("Employee")
             .WithSummary("Reads one employee and where they have worked.");

        group.MapPost("/employees", HireAsync)
             .WithName("HireEmployee")
             .WithSummary("Hires somebody from a date.");

        group.MapPost("/employees/{employeeNo}/transfer", TransferAsync)
             .WithName("TransferEmployee")
             .WithSummary("Moves somebody to another branch from a date, closing the previous assignment.");

        group.MapGet("/shifts", ShiftsAsync)
             .WithName("Shifts")
             .WithSummary("The working patterns and which days each runs.");

        group.MapPut("/shifts/{code}", SaveShiftAsync)
             .WithName("SaveShift")
             .WithSummary("Writes a shift.");

        group.MapGet("/shift-assignments", ShiftAssignmentsAsync)
             .WithName("ShiftAssignments")
             .WithSummary("Who is on which shift, and since when.");

        group.MapPost("/employees/{employeeNo}/shift", AssignShiftAsync)
             .WithName("AssignShift")
             .WithSummary("Puts somebody on a shift, closing the one before it the day before.");

        group.MapGet("/attendance", AttendanceListAsync)
             .WithName("Attendance")
             .WithSummary("What people did over a stretch of days.");

        group.MapPost("/employees/{employeeNo}/attendance", RecordAttendanceAsync)
             .WithName("RecordAttendance")
             .WithSummary("Records one day, measured against the shift they were on.");

        group.MapGet("/contracts", ContractsAsync)
             .WithName("EmploymentContracts")
             .WithSummary("What people have been engaged on, and when it changed.");

        group.MapGet("/employees/{employeeNo}/contracts", ContractsAsync)
             .WithName("EmployeeContracts")
             .WithSummary("One person's contracts, earliest first.");

        group.MapPost("/employees/{employeeNo}/contracts", RecordContractAsync)
             .WithName("RecordContract")
             .WithSummary("Records a contract, closing the one before it when asked to.");

        group.MapPut("/contracts/{id:guid}", AmendContractAsync)
             .WithName("AmendContract")
             .WithSummary("Changes a contract already recorded.");

        group.MapPost("/employees/{employeeNo}/leaving", LeavingAsync)
             .WithName("RecordLeaving")
             .WithSummary("Records that somebody has left, and works out what they are owed.");

        group.MapGet("/leave", LeaveListAsync)
             .WithName("LeaveRequests")
             .WithSummary("Lists leave requests, most recent first.");

        group.MapGet("/leave/balance/{employeeNo}", LeaveBalanceAsync)
             .WithName("LeaveBalance")
             .WithSummary("What one employee has earned, taken and has left.");

        group.MapPost("/leave", LeaveRequestAsync)
             .WithName("RequestLeave")
             .WithSummary("Asks for leave, checking it against the balance and what else is booked.");

        group.MapPost("/leave/{requestNo}/approve", LeaveApproveAsync)
             .WithName("ApproveLeave")
             .WithSummary("Grants a request, which is what makes it count.");

        group.MapPost("/leave/{requestNo}/reject", LeaveRejectAsync)
             .WithName("RejectLeave")
             .WithSummary("Refuses a request, keeping the record of having been asked.");

        group.MapPost("/leave/{requestNo}/cancel", LeaveCancelAsync)
             .WithName("CancelLeave")
             .WithSummary("Withdraws a request, granted or not.");

        group.MapGet("/entitlements", EntitlementsAsync)
             .WithName("HrEntitlements")
             .WithSummary("What the company owes its staff in unused leave and end of service.");

        group.MapPost("/entitlements/post", PostProvisionsAsync)
             .WithName("PostHrProvisions")
             .WithSummary(
                 "Moves however much unused leave has changed since the last run into the "
                 + "general ledger. End of service is charged by the payroll run instead.");

        group.MapGet("/reports/headcount", HeadcountAsync)
             .WithName("HrHeadcount")
             .WithSummary("How many people are at each branch, on a day.");

        group.MapGet("/reports/cost-by-branch", CostByBranchAsync)
             .WithName("HrCostByBranch")
             .WithSummary("What each branch's staff cost, on a day.");

        group.MapGet("/reports/turnover", TurnoverAsync)
             .WithName("HrTurnover")
             .WithSummary("How many people came and went over a period, and the rate it comes to.");

        group.MapGet("/payroll", PayrollListAsync)
             .WithName("PayrollRuns")
             .WithSummary("Lists payroll runs, most recent first.");

        group.MapGet("/payroll/{runNo}", PayrollGetAsync)
             .WithName("PayrollRun")
             .WithSummary("Reads one run, its lines and how each divides between branches.");

        group.MapPost("/payroll", CalculateAsync)
             .WithName("CalculatePayroll")
             .WithSummary("Works out what everybody is owed for a period, without committing to it.");

        group.MapDelete("/payroll/{runNo}", DiscardPayrollAsync)
             .WithName("DiscardPayroll")
             .WithSummary("Throws away a draft run. A posted run is reversed instead.");

        group.MapPost("/payroll/{runNo}/post", PostPayrollAsync)
             .WithName("PostPayroll")
             .WithSummary("Commits a run to the ledger, charging each branch what it actually cost.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        EmployeeService employees,
        IUserContext user,
        HttpContext http,
        [FromQuery] bool? includeLeavers,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view employees", http);
        }

        var found = await employees
            .ListAsync(includeLeavers ?? false, cancellationToken)
            .ConfigureAwait(false);

        // Wages are left out unless the caller may see them. A supervisor needs to know who works
        // for them; far fewer people need to know what each of them earns.
        var showWages = Can(user, WageReadPermission);

        return Results.Ok(found.Select(e => View(e, showWages)));
    }

    private static async Task<IResult> GetAsync(
        string employeeNo,
        EmployeeService employees,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view employees", http);
        }

        var employee = await employees.LoadAsync(employeeNo, cancellationToken).ConfigureAwait(false);

        return employee is null
            ? Results.NotFound()
            : Results.Ok(View(employee, Can(user, WageReadPermission)));
    }

    private static async Task<IResult> HireAsync(
        HireRequest request,
        EmployeeService employees,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, HirePermission))
        {
            return Forbidden(HirePermission, "hire people", http);
        }

        // Setting somebody's wage is a separate permission from hiring them, and the request
        // carrying both does not change that.
        if ((request.BasicWage != 0m || request.Allowances != 0m) && !Can(user, WageUpdatePermission))
        {
            return Forbidden(WageUpdatePermission, "set what somebody is paid", http);
        }

        var result = await employees
            .HireAsync(
                new Employee
                {
                    No = request.No ?? string.Empty,
                    Name = request.Name,
                    NameArabic = request.NameArabic,
                    NationalId = request.NationalId,
                    Nationality = request.Nationality,
                    HiredOn = request.HiredOn,
                    BasicWage = request.BasicWage,
                    Allowances = request.Allowances,
                    PositionId = request.PositionId,
                },
                request.BranchId,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                employee = View(result.Value, showWages: true),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> TransferAsync(
        string employeeNo,
        TransferRequest request,
        EmployeeService employees,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "transfer people", http);
        }

        var result = await employees
            .TransferAsync(employeeNo, request.BranchId, request.FromDate, request.Reason, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                employee = View(result.Value, Can(user, WageReadPermission)),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> LeavingAsync(
        string employeeNo,
        LeavingRequest request,
        EmployeeService employees,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "record that somebody has left", http);
        }

        var result = await employees
            .RecordLeavingAsync(employeeNo, request.LeftOn, request.Reason, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                serviceYears = result.Value.ServiceYears,
                monthlyWage = result.Value.MonthlyWage,
                fullAward = result.Value.FullAward,
                retainedFraction = result.Value.RetainedFraction,
                award = result.Value.Award,
                forfeitedByResigning = result.Value.ForfeitedByResigning,
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> PostProvisionsAsync(
        ProvisionPostingService provisions,
        IUserContext user,
        HttpContext http,
        [FromQuery] DateOnly? on,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ProvisionPermission))
        {
            return Forbidden(ProvisionPermission, "post entitlement provisions", http);
        }

        var result = await provisions.PostAsync(on, cancellationToken).ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                posting = new ProvisionPostingView(
                    result.Value.AsOf,
                    result.Value.LeaveTotal,
                    result.Value.LeaveMovement,
                    result.Value.EndOfServiceTotal,
                    result.Value.TransactionNo),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> HeadcountAsync(
        HrReportingService reporting,
        IUserContext user,
        HttpContext http,
        [FromQuery] DateOnly? on,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReportPermission))
        {
            return Forbidden(ReportPermission, "run staff reports", http);
        }

        var rows = await reporting.HeadcountByBranchAsync(on, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static r => new HeadcountView(
            r.BranchId, r.BranchCode, r.BranchName, r.Count)));
    }

    private static async Task<IResult> CostByBranchAsync(
        HrReportingService reporting,
        IUserContext user,
        HttpContext http,
        [FromQuery] DateOnly? on,
        CancellationToken cancellationToken)
    {
        // Aggregated by branch rather than by person, but it is still a statement of what people
        // are paid, so it sits behind the same permission the individual figures do.
        if (!Can(user, ReportPermission) || !Can(user, WageReadPermission))
        {
            return Forbidden(WageReadPermission, "see what branches cost in wages", http);
        }

        var rows = await reporting.CostByBranchAsync(on, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static r => new BranchCostView(
            r.BranchId, r.BranchCode, r.BranchName, r.Count, r.MonthlyWageCost)));
    }

    private static async Task<IResult> TurnoverAsync(
        HrReportingService reporting,
        IUserContext user,
        HttpContext http,
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReportPermission))
        {
            return Forbidden(ReportPermission, "run staff reports", http);
        }

        var summary = await reporting.TurnoverAsync(fromDate, toDate, cancellationToken).ConfigureAwait(false);

        return Results.Ok(new TurnoverView(
            summary.FromDate,
            summary.ToDate,
            summary.OpeningHeadcount,
            summary.Hired,
            summary.Left,
            summary.ClosingHeadcount,
            summary.TurnoverRate));
    }

    private static async Task<IResult> EntitlementsAsync(
        EmployeeService employees,
        IUserContext user,
        HttpContext http,
        [FromQuery] DateOnly? on,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReportPermission))
        {
            return Forbidden(ReportPermission, "run staff reports", http);
        }

        var rows = await employees.EntitlementsAsync(on, cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            totalOwed = rows.Sum(static r => r.TotalOwed),
            leaveLiability = rows.Sum(static r => r.LeaveLiability),
            endOfService = rows.Sum(static r => r.EndOfService),
            employees = rows,
        });
    }

    private static async Task<IResult> LeaveListAsync(
        LeaveService leave,
        IUserContext user,
        HttpContext http,
        [FromQuery] string? employeeNo,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (!Can(user, LeaveReadPermission))
        {
            return Forbidden(LeaveReadPermission, "see leave", http);
        }

        var requests = await leave
            .ListAsync(employeeNo, from, to, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(requests.Select(View));
    }

    private static async Task<IResult> LeaveBalanceAsync(
        string employeeNo,
        LeaveService leave,
        IUserContext user,
        HttpContext http,
        [FromQuery] DateOnly? on,
        CancellationToken cancellationToken)
    {
        if (!Can(user, LeaveReadPermission))
        {
            return Forbidden(LeaveReadPermission, "see leave", http);
        }

        var result = await leave.EntitlementAsync(employeeNo, on, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> LeaveRequestAsync(
        LeaveRequestInput request,
        LeaveService leave,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, LeaveCreatePermission))
        {
            return Forbidden(LeaveCreatePermission, "ask for leave", http);
        }

        var result = await leave
            .RequestAsync(
                request.EmployeeNo,
                request.Kind,
                request.FromDate,
                request.ToDate,
                request.Reason,
                request.Submit,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new { request = View(result.Value), messages = MessagePayload.FromAll(result.Messages) });
    }

    private static Task<IResult> LeaveApproveAsync(
        string requestNo,
        LeaveDecisionInput? decision,
        LeaveService leave,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
        => DecideLeaveAsync(
            user,
            http,
            LeaveApprovePermission,
            "decide on leave",
            () => leave.ApproveAsync(requestNo, decision?.Note, cancellationToken));

    private static Task<IResult> LeaveRejectAsync(
        string requestNo,
        LeaveDecisionInput? decision,
        LeaveService leave,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
        => DecideLeaveAsync(
            user,
            http,
            LeaveApprovePermission,
            "decide on leave",
            () => leave.RejectAsync(requestNo, decision?.Note, cancellationToken));

    private static Task<IResult> LeaveCancelAsync(
        string requestNo,
        LeaveDecisionInput? decision,
        LeaveService leave,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
        => DecideLeaveAsync(
            user,
            http,
            LeaveCreatePermission,
            "withdraw leave",
            () => leave.CancelAsync(requestNo, decision?.Note, cancellationToken));

    private static async Task<IResult> DecideLeaveAsync(
        IUserContext user,
        HttpContext http,
        string permission,
        string doing,
        Func<Task<Result<LeaveRequest>>> decide)
    {
        if (!Can(user, permission))
        {
            return Forbidden(permission, doing, http);
        }

        var result = await decide().ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(result.Value));
    }

    private static object View(LeaveRequest request)
        => new
        {
            no = request.No,
            employeeNo = request.EmployeeNo,
            employeeName = request.EmployeeName,
            kind = request.Kind.ToString(),
            fromDate = request.FromDate,
            toDate = request.ToDate,
            days = request.Days,
            status = request.Status.ToString(),
            reason = request.Reason,
            decisionNote = request.DecisionNote,
            decidedAtUtc = request.DecidedAtUtc,
        };

    private static async Task<IResult> PayrollListAsync(
        PayrollService payroll,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, WageReadPermission))
        {
            return Forbidden(WageReadPermission, "view payroll", http);
        }

        var runs = await payroll.ListAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(runs.Select(static r => new
        {
            no = r.No,
            fromDate = r.FromDate,
            toDate = r.ToDate,
            status = r.Status.ToString(),
            people = r.Lines.Count,
            grossPay = r.GrossPay,
            netPay = r.NetPay,
            endOfServiceCharge = r.EndOfServiceCharge,
            transactionNo = r.TransactionNo,
        }));
    }

    private static async Task<IResult> PayrollGetAsync(
        string runNo,
        PayrollService payroll,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, WageReadPermission))
        {
            return Forbidden(WageReadPermission, "view payroll", http);
        }

        var run = await payroll.LoadAsync(runNo, cancellationToken).ConfigureAwait(false);

        return run is null ? Results.NotFound() : Results.Ok(View(run));
    }

    private static async Task<IResult> CalculateAsync(
        CalculatePayrollRequest request,
        PayrollService payroll,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, WageUpdatePermission))
        {
            return Forbidden(WageUpdatePermission, "work out a payroll run", http);
        }

        var result = await payroll
            .CalculateAsync(request.From, request.To, request.PostingDate, request.Description, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                run = View(result.Value),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> DiscardPayrollAsync(
        string runNo,
        PayrollService payroll,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, WageUpdatePermission))
        {
            return Forbidden(WageUpdatePermission, "discard a payroll run", http);
        }

        var result = await payroll.DiscardAsync(runNo, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.NoContent();
    }

    private static async Task<IResult> PostPayrollAsync(
        string runNo,
        PostPayrollRequest? request,
        PayrollService payroll,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, WageUpdatePermission))
        {
            return Forbidden(WageUpdatePermission, "post a payroll run", http);
        }

        var result = await payroll
            .PostAsync(runNo, Overrides(user), request?.OverrideReason, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                run = View(result.Value),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static object View(Employee employee, bool showWages)
        => new
        {
            no = employee.No,
            name = employee.Name,
            nameArabic = employee.NameArabic,
            nationality = employee.Nationality,
            hiredOn = employee.HiredOn,
            leftOn = employee.LeftOn,
            leavingReason = employee.LeavingReason.ToString(),
            status = employee.Status.ToString(),
            position = employee.Position?.Title,

            // Null rather than zero when the caller may not see them. Zero would read as
            // "unpaid", which is a different and defamatory claim.
            basicWage = showWages ? employee.BasicWage : (decimal?)null,
            allowances = showWages ? employee.Allowances : (decimal?)null,
            totalWage = showWages ? employee.TotalWage : (decimal?)null,
            branchAssignments = employee.BranchAssignments
                .OrderBy(static a => a.FromDate)
                .Select(static a => new { a.BranchId, a.FromDate, a.ToDate, a.Reason }),
        };

    private static object View(PayrollRun run)
        => new
        {
            no = run.No,
            fromDate = run.FromDate,
            toDate = run.ToDate,
            postingDate = run.PostingDate,
            description = run.Description,
            status = run.Status.ToString(),
            daysInPeriod = run.DaysInPeriod,
            grossPay = run.GrossPay,
            deductions = run.Deductions,
            netPay = run.NetPay,
            endOfServiceCharge = run.EndOfServiceCharge,
            transactionNo = run.TransactionNo,
            lines = run.Lines
                .OrderBy(static l => l.EmployeeNo)
                .Select(static l => new
                {
                    employeeNo = l.EmployeeNo,
                    employeeName = l.EmployeeName,
                    daysWorked = l.DaysWorked,
                    basicPay = l.BasicPay,
                    allowances = l.Allowances,
                    otherEarnings = l.OtherEarnings,
                    deductions = l.Deductions,

                    // What the deduction was for. A figure on a payslip with nothing beside it
                    // is the thing somebody comes to ask about.
                    note = l.Note,
                    grossPay = l.GrossPay,
                    netPay = l.NetPay,
                    endOfServiceCharge = l.EndOfServiceCharge,
                    branchShares = l.BranchShares.Select(static s => new
                    {
                        s.BranchId,
                        s.Days,
                        s.Amount,
                    }),
                }),
        };

    /// <summary>
    /// Which of the protections a payroll posting can meet the caller may push past.
    /// </summary>
    /// <remarks>
    /// A run reaches the ledger through Finance's posting engine, so a payroll can be stopped by
    /// a rule belonging to a module that has never heard of an employee.
    /// </remarks>
    private static IReadOnlySet<string> Overrides(IUserContext user)
        => new[]
           {
               "Hr.Payroll.Override",
               "Finance.Account.Override",
           }
            .Where(permission => Can(user, permission))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static async Task<IResult> ShiftsAsync(
        AttendanceService attendance,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        bool activeOnly = false)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view shifts", http);
        }

        var shifts = await attendance.ShiftsAsync(activeOnly, cancellationToken).ConfigureAwait(false);

        return Results.Ok(shifts.Select(ViewOf));
    }

    private static async Task<IResult> SaveShiftAsync(
        string code,
        ShiftRequest request,
        AttendanceService attendance,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "maintain shifts", http);
        }

        var result = await attendance
            .SaveShiftAsync(request with { Code = code }, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(ViewOf(result.Value));
    }

    private static async Task<IResult> ShiftAssignmentsAsync(
        AttendanceService attendance,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        string? employeeNo = null)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view shift assignments", http);
        }

        var rows = await attendance.AssignmentsAsync(employeeNo, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static a => new ShiftAssignmentView(
            a.EmployeeNo,
            a.ShiftCode,
            a.FromDate,
            a.ToDate)));
    }

    private static async Task<IResult> AssignShiftAsync(
        string employeeNo,
        AssignShiftRequest request,
        AttendanceService attendance,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "put people on shifts", http);
        }

        var result = await attendance
            .AssignAsync(employeeNo, request.ShiftCode, request.FromDate, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new ShiftAssignmentView(
                result.Value.EmployeeNo,
                result.Value.ShiftCode,
                result.Value.FromDate,
                result.Value.ToDate));
    }

    private static async Task<IResult> AttendanceListAsync(
        AttendanceService attendance,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        string? employeeNo = null)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view attendance", http);
        }

        var last = to ?? clock.Today;
        var first = from ?? last.AddDays(-13);

        var rows = await attendance
            .ListAsync(first, last, employeeNo, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(rows.Select(static a => new AttendanceView(
            a.EmployeeNo,
            a.OnDate,
            a.ShiftCode,
            a.ClockedInAt,
            a.ClockedOutAt,
            a.Status,
            a.WorkedMinutes,
            a.LateMinutes,
            a.EarlyLeaveMinutes,
            a.OvertimeMinutes,
            a.Note)));
    }

    private static async Task<IResult> RecordAttendanceAsync(
        string employeeNo,
        RecordAttendanceRequest request,
        AttendanceService attendance,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "record attendance", http);
        }

        var result = await attendance
            .RecordAsync(
                new AttendanceRequest(
                    employeeNo,
                    request.OnDate,
                    request.ClockedInAt,
                    request.ClockedOutAt,
                    request.Note),
                request.Amend,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return Refused(result, http);
        }

        var value = result.Value;

        return Results.Ok(new
        {
            attendance = new AttendanceView(
                value.EmployeeNo,
                value.OnDate,
                value.ShiftCode,
                value.ClockedInAt,
                value.ClockedOutAt,
                value.Status,
                value.WorkedMinutes,
                value.LateMinutes,
                value.EarlyLeaveMinutes,
                value.OvertimeMinutes,
                value.Note),
            messages = MessagePayload.FromAll(result.Messages),
        });
    }

    private static ShiftView ViewOf(Shift shift)
        => new(
            shift.Code,
            shift.Name,
            shift.NameArabic,
            shift.StartsAt,
            shift.EndsAt,
            shift.BreakMinutes,
            shift.DaysOfWeek,
            shift.GraceMinutes,
            shift.PaidMinutes,
            shift.CrossesMidnight,
            shift.IsActive);

    private static async Task<IResult> ContractsAsync(
        EmploymentContractService contracts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        string? employeeNo = null)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view employment contracts", http);
        }

        var rows = await contracts.ListAsync(employeeNo, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(ViewOf));
    }

    private static async Task<IResult> RecordContractAsync(
        string employeeNo,
        RecordContractRequest request,
        EmploymentContractService contracts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "record employment contracts", http);
        }

        var contract = new EmploymentContractRequest(
            employeeNo,
            request.StartsOn,
            request.BasicWage,
            request.Allowances,
            request.Kind,
            request.EndsOn,
            request.PayFrequency,
            PositionId: null,
            request.Reference,
            request.SignedOn,
            request.Reason);

        var result = request.Supersede
            ? await contracts.SupersedeAsync(contract, cancellationToken).ConfigureAwait(false)
            : await contracts.RecordAsync(contract, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(ViewOf(result.Value));
    }

    private static async Task<IResult> AmendContractAsync(
        Guid id,
        RecordContractRequest request,
        EmploymentContractService contracts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "amend employment contracts", http);
        }

        var existing = (await contracts.ListAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault(c => c.Id == id);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var result = await contracts
            .AmendAsync(
                id,
                new EmploymentContractRequest(
                    existing.EmployeeNo,
                    request.StartsOn,
                    request.BasicWage,
                    request.Allowances,
                    request.Kind,
                    request.EndsOn,
                    request.PayFrequency,
                    PositionId: null,
                    request.Reference,
                    request.SignedOn,
                    request.Reason),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(ViewOf(result.Value));
    }

    private static EmploymentContractView ViewOf(EmploymentContract contract)
        => new(
            contract.Id,
            contract.EmployeeNo,
            contract.StartsOn,
            contract.EndsOn,
            contract.Kind,
            contract.BasicWage,
            contract.Allowances,
            contract.PayFrequency,
            contract.Reference,
            contract.SignedOn,
            contract.RecordedByUserName,
            contract.Reason);

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult Refused(Platform.Kernel.Results.Result result, HttpContext http)
        => Results.Json(
            AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
            statusCode: AsapProblem.StatusFor(result.Messages));
}
