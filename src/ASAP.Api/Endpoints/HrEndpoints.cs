using ASAP.Api.Infrastructure;
using ASAP.Modules.Hr;
using ASAP.Modules.Hr.Entitlements;
using ASAP.Modules.Hr.People;
using ASAP.Modules.Hr.Reporting;
using ASAP.Platform.Kernel.Security;
using Microsoft.AspNetCore.Mvc;

namespace ASAP.Api.Endpoints;

/// <summary>What a client sends to hire somebody.</summary>
/// <param name="Name">Their name.</param>
/// <param name="HiredOn">The day they start, or started.</param>
/// <param name="No">
/// An employee number to use instead of the next one from the series, for a migrated record.
/// </param>
/// <param name="NameArabic">Their name in Arabic.</param>
/// <param name="NationalId">The national or residence identity number.</param>
/// <param name="Nationality">Nationality.</param>
/// <param name="DateOfBirth">When they were born.</param>
/// <param name="Email">Where to reach them.</param>
/// <param name="Phone">A telephone number.</param>
/// <param name="PositionId">The position they hold.</param>
/// <param name="ManagerId">Who they report to.</param>
/// <param name="BasicWage">The basic wage for one pay period.</param>
/// <param name="Allowances">Housing, transport and the rest, for one pay period.</param>
/// <param name="PayFrequency">How often they are paid.</param>
/// <param name="BranchId">Where they will work from their first day.</param>
public sealed record HireEmployeeRequest(
    string Name,
    DateOnly HiredOn,
    string? No = null,
    string? NameArabic = null,
    string? NationalId = null,
    string? Nationality = null,
    DateOnly? DateOfBirth = null,
    string? Email = null,
    string? Phone = null,
    Guid? PositionId = null,
    Guid? ManagerId = null,
    decimal BasicWage = 0m,
    decimal Allowances = 0m,
    PayFrequency PayFrequency = PayFrequency.Monthly,
    Guid? BranchId = null);

/// <summary>What a client sends to move somebody to another branch.</summary>
/// <param name="BranchId">Where to.</param>
/// <param name="FromDate">Their first day there.</param>
/// <param name="Reason">Why, for the record.</param>
public sealed record TransferEmployeeRequest(Guid BranchId, DateOnly FromDate, string? Reason = null);

/// <summary>What a client sends to record that somebody has left.</summary>
/// <param name="LeftOn">Their last day.</param>
/// <param name="Reason">Why.</param>
public sealed record RecordLeavingRequest(DateOnly LeftOn, LeavingReason Reason);

/// <summary>An employee as it is reported back.</summary>
/// <param name="No">Their number.</param>
/// <param name="Name">Their name.</param>
/// <param name="NameArabic">Their name in Arabic.</param>
/// <param name="PositionTitle">The job they hold, when they have one.</param>
/// <param name="Status">Where they stand.</param>
/// <param name="HiredOn">The day they started.</param>
/// <param name="LeftOn">The day they left, when they have.</param>
/// <param name="LeavingReason">Why they left, when they have.</param>
/// <param name="BasicWage">
/// The basic wage for one pay period, or null when the caller may not see what people are paid.
/// </param>
/// <param name="Allowances">Allowances for one pay period, withheld on the same terms.</param>
public sealed record EmployeeView(
    string No,
    string Name,
    string? NameArabic,
    string? PositionTitle,
    string Status,
    DateOnly HiredOn,
    DateOnly? LeftOn,
    string? LeavingReason,
    decimal? BasicWage,
    decimal? Allowances);

/// <summary>What one employee has earned and not yet been given.</summary>
public sealed record EmployeeEntitlementView(
    string EmployeeNo,
    string Name,
    decimal ServiceYears,
    decimal LeaveDays,
    decimal LeaveLiability,
    decimal EndOfService,
    decimal TotalOwed);

/// <summary>What a provision run posted.</summary>
public sealed record ProvisionPostingView(
    DateOnly AsOf,
    decimal EndOfServiceTotal,
    decimal EndOfServiceMovement,
    decimal LeaveTotal,
    decimal LeaveMovement,
    long? TransactionNo);

/// <summary>What a client sends to record that somebody was away.</summary>
/// <param name="FromDate">Their first day away.</param>
/// <param name="ToDate">Their last day away.</param>
/// <param name="Note">What it was for, when that is worth recording.</param>
public sealed record RecordLeaveRequest(DateOnly FromDate, DateOnly ToDate, string? Note = null);

/// <summary>One period of leave as it is reported back.</summary>
/// <param name="FromDate">The first day away.</param>
/// <param name="ToDate">The last day away.</param>
/// <param name="Days">How many days it covers.</param>
/// <param name="Note">What it was for, when recorded.</param>
public sealed record LeaveRecordView(DateOnly FromDate, DateOnly ToDate, decimal Days, string? Note);

/// <summary>How many people are at one branch, as it is reported back.</summary>
public sealed record HeadcountView(Guid? BranchId, string? BranchCode, string? BranchName, int Count);

/// <summary>What one branch's staff cost, as it is reported back.</summary>
public sealed record BranchCostView(
    Guid? BranchId, string? BranchCode, string? BranchName, int Count, decimal MonthlyWageCost);

/// <summary>How many people came and went over a period, as it is reported back.</summary>
public sealed record TurnoverView(
    DateOnly FromDate,
    DateOnly ToDate,
    int OpeningHeadcount,
    int Hired,
    int Left,
    int ClosingHeadcount,
    decimal TurnoverRate);

/// <summary>Employees, where they have worked, what they have earned, and what the company owes them.</summary>
public static class HrEndpoints
{
    private const string ReadPermission = "Hr.Employee.Read";
    private const string CreatePermission = "Hr.Employee.Create";
    private const string UpdatePermission = "Hr.Employee.Update";
    private const string WagePermission = "Hr.Wage.Read";
    private const string ReportPermission = "Hr.Report.Read";
    private const string ProvisionPermission = "Hr.Provision.Post";
    private const string LeaveReadPermission = "Hr.Leave.Read";
    private const string LeaveCreatePermission = "Hr.Leave.Create";

    /// <summary>Maps the HR endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHrEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/hr").RequireAuthorization().WithTags("Human resources");

        group.MapGet("/employees", ListAsync)
             .WithName("HrEmployees")
             .WithSummary("Lists employees, most recently hired first.");

        group.MapGet("/employees/{employeeNo}", GetAsync)
             .WithName("HrEmployee")
             .WithSummary("Reads one employee.");

        group.MapPost("/employees", HireAsync)
             .WithName("HireEmployee")
             .WithSummary("Hires somebody.");

        group.MapPost("/employees/{employeeNo}/transfer", TransferAsync)
             .WithName("TransferEmployee")
             .WithSummary("Moves somebody to another branch from a date.");

        group.MapPost("/employees/{employeeNo}/leave", RecordLeavingAsync)
             .WithName("RecordEmployeeLeaving")
             .WithSummary("Records that somebody has left, and what they are owed.");

        // Not "/leave" -- that path already means somebody leaving the company for good, and the
        // two are not the same question.
        group.MapGet("/employees/{employeeNo}/leave-records", ListLeaveAsync)
             .WithName("HrLeaveRecords")
             .WithSummary("Lists leave somebody has taken, most recent first.");

        group.MapPost("/employees/{employeeNo}/leave-records", RecordLeaveAsync)
             .WithName("RecordLeaveTaken")
             .WithSummary("Records that somebody was away, so their balance reflects it.");

        group.MapGet("/entitlements", EntitlementsAsync)
             .WithName("HrEntitlements")
             .WithSummary("What the company owes everybody who works for it, today.");

        group.MapPost("/entitlements/post", PostProvisionsAsync)
             .WithName("PostHrProvisions")
             .WithSummary(
                 "Moves however much end-of-service and unused leave have changed since the last "
                 + "run into the general ledger.");

        group.MapGet("/reports/headcount", HeadcountAsync)
             .WithName("HrHeadcount")
             .WithSummary("How many people are at each branch, on a day.");

        group.MapGet("/reports/cost-by-branch", CostByBranchAsync)
             .WithName("HrCostByBranch")
             .WithSummary("What each branch's staff cost, on a day.");

        group.MapGet("/reports/turnover", TurnoverAsync)
             .WithName("HrTurnover")
             .WithSummary("How many people came and went over a period, and the rate it comes to.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        EmployeeService employees,
        IUserContext user,
        HttpContext http,
        [FromQuery] bool includeLeavers = false,
        CancellationToken cancellationToken = default)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view employees", http);
        }

        var list = await employees.ListAsync(includeLeavers, cancellationToken).ConfigureAwait(false);

        return Results.Ok(list.Select(e => View(e, user)));
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

        return employee is null ? Results.NotFound() : Results.Ok(View(employee, user));
    }

    private static async Task<IResult> HireAsync(
        HireEmployeeRequest request,
        EmployeeService employees,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, CreatePermission))
        {
            return Forbidden(CreatePermission, "hire people", http);
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
                    DateOfBirth = request.DateOfBirth,
                    Email = request.Email,
                    Phone = request.Phone,
                    PositionId = request.PositionId,
                    ManagerId = request.ManagerId,
                    HiredOn = request.HiredOn,
                    BasicWage = request.BasicWage,
                    Allowances = request.Allowances,
                    PayFrequency = request.PayFrequency,
                },
                request.BranchId,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                employee = View(result.Value, user),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> TransferAsync(
        string employeeNo,
        TransferEmployeeRequest request,
        EmployeeService employees,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "change employee records", http);
        }

        var result = await employees
            .TransferAsync(employeeNo, request.BranchId, request.FromDate, request.Reason, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                employee = View(result.Value, user),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> RecordLeavingAsync(
        string employeeNo,
        RecordLeavingRequest request,
        EmployeeService employees,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, UpdatePermission))
        {
            return Forbidden(UpdatePermission, "change employee records", http);
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

    private static async Task<IResult> ListLeaveAsync(
        string employeeNo,
        LeaveRegisterService leave,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, LeaveReadPermission))
        {
            return Forbidden(LeaveReadPermission, "view leave records", http);
        }

        var result = await leave.ListAsync(employeeNo, cancellationToken).ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(result.Value.Select(static r => new LeaveRecordView(
                r.FromDate, r.ToDate, r.Days, r.Note)));
    }

    private static async Task<IResult> RecordLeaveAsync(
        string employeeNo,
        RecordLeaveRequest request,
        LeaveRegisterService leave,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, LeaveCreatePermission))
        {
            return Forbidden(LeaveCreatePermission, "record leave taken", http);
        }

        var result = await leave
            .RecordAsync(employeeNo, request.FromDate, request.ToDate, request.Note, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                record = new LeaveRecordView(
                    result.Value.FromDate, result.Value.ToDate, result.Value.Days, result.Value.Note),
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
        if (!Can(user, ReportPermission) || !Can(user, WagePermission))
        {
            return Forbidden(WagePermission, "see what branches cost in wages", http);
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
        if (!Can(user, ReportPermission) || !Can(user, WagePermission))
        {
            return Forbidden(ReportPermission, "run staff reports", http);
        }

        var rows = await employees.EntitlementsAsync(on, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static r => new EmployeeEntitlementView(
            r.EmployeeNo, r.Name, r.ServiceYears, r.LeaveDays, r.LeaveLiability, r.EndOfService, r.TotalOwed)));
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
                    result.Value.EndOfServiceTotal,
                    result.Value.EndOfServiceMovement,
                    result.Value.LeaveTotal,
                    result.Value.LeaveMovement,
                    result.Value.TransactionNo),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    /// <summary>
    /// Renders an employee, withholding what they are paid from a caller who may not see it.
    /// </summary>
    /// <remarks>
    /// <see cref="HrModule"/> declares viewing the staff list and viewing wages as separate,
    /// sensitive permissions on purpose -- a supervisor needs to know who works for them, and far
    /// fewer people need to know what each of them earns. Redacting here, rather than trusting
    /// every future caller of <see cref="EmployeeService"/> to remember, is what keeps that true.
    /// </remarks>
    private static EmployeeView View(Employee employee, IUserContext user)
    {
        var canSeeWage = Can(user, WagePermission);

        return new EmployeeView(
            employee.No,
            employee.Name,
            employee.NameArabic,
            employee.Position?.Title,
            employee.Status.ToString(),
            employee.HiredOn,
            employee.LeftOn,
            employee.LeavingReason == LeavingReason.None ? null : employee.LeavingReason.ToString(),
            canSeeWage ? employee.BasicWage : null,
            canSeeWage ? employee.Allowances : null);
    }

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
