using ASAP.Api.Infrastructure;
using ASAP.Modules.Hr.Payroll;
using ASAP.Modules.Hr.People;
using ASAP.Platform.Kernel.Security;
using Microsoft.AspNetCore.Mvc;

namespace ASAP.Api.Endpoints;

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

/// <summary>Employees, where they work, and what they are owed.</summary>
public static class HrEndpoints
{
    private const string ReadPermission = "Hr.Employee.Read";
    private const string HirePermission = "Hr.Employee.Create";
    private const string UpdatePermission = "Hr.Employee.Update";
    private const string WageReadPermission = "Hr.Wage.Read";
    private const string WageUpdatePermission = "Hr.Wage.Update";
    private const string ReportPermission = "Hr.Report.Read";

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

        group.MapPost("/employees/{employeeNo}/leaving", LeavingAsync)
             .WithName("RecordLeaving")
             .WithSummary("Records that somebody has left, and works out what they are owed.");

        group.MapGet("/entitlements", EntitlementsAsync)
             .WithName("HrEntitlements")
             .WithSummary("What the company owes its staff in unused leave and end of service.");

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
