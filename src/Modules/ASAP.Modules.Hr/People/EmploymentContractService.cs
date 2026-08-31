using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Hr.People;

/// <summary>A contract as somebody asks for it to be recorded.</summary>
/// <param name="EmployeeNo">Whose it is.</param>
/// <param name="StartsOn">The first day it covers.</param>
/// <param name="BasicWage">The basic wage for one pay period.</param>
/// <param name="Allowances">Housing, transport and the rest.</param>
/// <param name="Kind">What kind of engagement it is.</param>
/// <param name="EndsOn">The last day it covers, on a fixed term.</param>
/// <param name="PayFrequency">How often it pays.</param>
/// <param name="PositionId">The position held under it.</param>
/// <param name="Reference">The paper contract's own reference.</param>
/// <param name="SignedOn">When it was signed.</param>
/// <param name="Reason">Why it was raised: a raise, a promotion, a renewal.</param>
public sealed record EmploymentContractRequest(
    string EmployeeNo,
    DateOnly StartsOn,
    decimal BasicWage,
    decimal Allowances = 0m,
    ContractKind Kind = ContractKind.Permanent,
    DateOnly? EndsOn = null,
    PayFrequency PayFrequency = PayFrequency.Monthly,
    Guid? PositionId = null,
    string? Reference = null,
    DateOnly? SignedOn = null,
    string? Reason = null);

/// <summary>
/// Records what somebody is engaged on, and when it changed.
/// </summary>
/// <remarks>
/// <para>
/// Everything here exists to keep one invariant: for any one person and any one day there is at
/// most one contract. The moment there are two, payroll has two wages for that day and pays
/// whichever row it read first — and the difference does not show up in any total, only in one
/// person's pay, which is where nobody is looking.
/// </para>
/// <para>
/// <see cref="SupersedeAsync"/> exists because doing a raise by hand is exactly where the
/// invariant breaks. Somebody enters the new contract from the first, forgets to close the old
/// one, and the overlap is refused — or worse, closes the old one a day too early and leaves a
/// day nobody is paid for.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="user">Says who is recording it.</param>
/// <param name="clock">Says what today is.</param>
/// <param name="logger">Records what changed.</param>
public sealed class EmploymentContractService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenancy,
    IUserContext user,
    IClock clock,
    ILogger<EmploymentContractService> logger)
{
    /// <summary>Somebody's contracts, earliest first.</summary>
    /// <param name="employeeNo">Whose, or null for everybody's.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The contracts.</returns>
    public async Task<IReadOnlyList<EmploymentContract>> ListAsync(
        string? employeeNo = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<EmploymentContract>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(employeeNo))
        {
            var no = employeeNo.Trim().ToUpperInvariant();
            query = query.Where(c => c.EmployeeNo == no);
        }

        return await query
            .OrderBy(c => c.EmployeeNo)
            .ThenBy(c => c.StartsOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The contract in force for somebody on a day, if any.</summary>
    /// <param name="employeeNo">Whose.</param>
    /// <param name="on">The day.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The contract, or null where none covers that day.</returns>
    public async Task<EmploymentContract?> InForceAsync(
        string employeeNo,
        DateOnly on,
        CancellationToken cancellationToken = default)
    {
        var no = employeeNo?.Trim().ToUpperInvariant() ?? string.Empty;

        return await context.Set<EmploymentContract>()
            .AsNoTracking()
            .Where(c => c.EmployeeNo == no && c.StartsOn <= on && (c.EndsOn == null || c.EndsOn >= on))
            .OrderByDescending(c => c.StartsOn)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records a contract, refusing one that would overlap another.
    /// </summary>
    /// <param name="request">The contract.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The contract, or every reason it was refused.</returns>
    public async Task<Result<EmploymentContract>> RecordAsync(
        EmploymentContractRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var no = request.EmployeeNo?.Trim().ToUpperInvariant() ?? string.Empty;

        var employee = await context.Set<Employee>()
            .FirstOrDefaultAsync(e => e.No == no, cancellationToken)
            .ConfigureAwait(false);

        if (employee is null)
        {
            return Result<EmploymentContract>.Failure(
                messages.Render(HrMessages.EmployeeNotFound, Args(("EmployeeNo", no))));
        }

        var found = await CheckAsync(request, employee, no, existingId: null, cancellationToken)
            .ConfigureAwait(false);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<EmploymentContract>.Failure(found);
        }

        var contract = new EmploymentContract
        {
            TenantId = tenancy.RequireTenantId(),
            CompanyId = tenancy.RequireCompanyId(),
            EmployeeId = employee.Id,
            EmployeeNo = no,
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn,
            Kind = request.Kind,
            BasicWage = request.BasicWage,
            Allowances = request.Allowances,
            PayFrequency = request.PayFrequency,
            PositionId = request.PositionId ?? employee.PositionId,
            Reference = request.Reference?.Trim(),
            SignedOn = request.SignedOn,
            Reason = request.Reason?.Trim(),
            RecordedByUserName = user.DisplayName ?? user.UserName,
        };

        context.Set<EmploymentContract>().Add(contract);

        SyncEmployee(employee, contract);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Contract for {EmployeeNo} recorded from {StartsOn} at {BasicWage}.",
            no,
            request.StartsOn,
            request.BasicWage);

        return Result<EmploymentContract>.Success(contract, found);
    }

    /// <summary>
    /// Puts somebody on a new contract, closing the one before it the day before it starts.
    /// </summary>
    /// <remarks>
    /// This is what a raise, a promotion or a renewal actually is, and it is worth being one
    /// operation. Doing it by hand means two saves, and the state between them is either an
    /// overlap the second save is refused for or a gap nobody is paid for.
    /// </remarks>
    /// <param name="request">The new contract.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The new contract, or every reason it was refused.</returns>
    public async Task<Result<EmploymentContract>> SupersedeAsync(
        EmploymentContractRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var no = request.EmployeeNo?.Trim().ToUpperInvariant() ?? string.Empty;

        var current = await context.Set<EmploymentContract>()
            .Where(c => c.EmployeeNo == no && c.StartsOn < request.StartsOn)
            .Where(c => c.EndsOn == null || c.EndsOn >= request.StartsOn)
            .OrderByDescending(c => c.StartsOn)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current is not null)
        {
            current.EndsOn = request.StartsOn.AddDays(-1);
        }

        var result = await RecordAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.Failed && current is not null)
        {
            // The close was only ever in aid of the new contract. Leaving it applied after a
            // refusal would end somebody's contract on the strength of a save that did not happen.
            context.Entry(current).State = EntityState.Detached;
        }

        return result;
    }

    /// <summary>Changes a contract already recorded.</summary>
    /// <param name="id">Which one.</param>
    /// <param name="request">What it should say.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The contract, or every reason it was refused.</returns>
    public async Task<Result<EmploymentContract>> AmendAsync(
        Guid id,
        EmploymentContractRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contract = await context.Set<EmploymentContract>()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (contract is null)
        {
            return Result<EmploymentContract>.Failure(
                messages.Render(HrMessages.ContractNotFound, Args()));
        }

        var employee = await context.Set<Employee>()
            .FirstOrDefaultAsync(e => e.Id == contract.EmployeeId, cancellationToken)
            .ConfigureAwait(false);

        if (employee is null)
        {
            return Result<EmploymentContract>.Failure(
                messages.Render(HrMessages.EmployeeNotFound, Args(("EmployeeNo", contract.EmployeeNo))));
        }

        var found = await CheckAsync(request, employee, contract.EmployeeNo, contract.Id, cancellationToken)
            .ConfigureAwait(false);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<EmploymentContract>.Failure(found);
        }

        contract.StartsOn = request.StartsOn;
        contract.EndsOn = request.EndsOn;
        contract.Kind = request.Kind;
        contract.BasicWage = request.BasicWage;
        contract.Allowances = request.Allowances;
        contract.PayFrequency = request.PayFrequency;
        contract.PositionId = request.PositionId ?? contract.PositionId;
        contract.Reference = request.Reference?.Trim();
        contract.SignedOn = request.SignedOn;
        contract.Reason = request.Reason?.Trim();

        SyncEmployee(employee, contract);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<EmploymentContract>.Success(contract, found);
    }

    /// <summary>
    /// Keeps the employee record showing what they are on today.
    /// </summary>
    /// <remarks>
    /// The figures on the employee are a copy, and this is the only place that writes them. They
    /// exist because every screen and report that ever asked what somebody earns reads them, and
    /// because an employee with no contract yet has nothing else to be paid on. Payroll reads the
    /// contracts, so a copy that fell out of step would be wrong on a card rather than wrong in
    /// somebody's pay.
    /// </remarks>
    private void SyncEmployee(Employee employee, EmploymentContract contract)
    {
        if (!contract.Covers(clock.Today))
        {
            return;
        }

        employee.BasicWage = contract.BasicWage;
        employee.Allowances = contract.Allowances;
        employee.PayFrequency = contract.PayFrequency;
    }

    private async Task<List<AsapMessage>> CheckAsync(
        EmploymentContractRequest request,
        Employee employee,
        string employeeNo,
        Guid? existingId,
        CancellationToken cancellationToken)
    {
        var found = new List<AsapMessage>();

        var arguments = Args(
            ("EmployeeNo", employeeNo),
            ("FromDate", request.StartsOn),
            ("ToDate", request.EndsOn),
            ("HiredOn", employee.HiredOn),
            ("Kind", request.Kind.ToString()));

        if (request.StartsOn < employee.HiredOn)
        {
            found.Add(messages.Render(HrMessages.ContractBeforeHiring, arguments));
        }

        if (request.EndsOn is { } ends && ends < request.StartsOn)
        {
            found.Add(messages.Render(HrMessages.ContractEndsBeforeItStarts, arguments));
        }

        if (request.Kind is not ContractKind.Permanent && request.EndsOn is null)
        {
            found.Add(messages.Render(HrMessages.ContractHasNoEnd, arguments));
        }

        if (request.Kind is ContractKind.Permanent && request.EndsOn is not null)
        {
            found.Add(messages.Render(HrMessages.ContractShouldNotEnd, arguments));
        }

        if (request.BasicWage <= 0m)
        {
            found.Add(messages.Render(HrMessages.ContractPaysNothing, arguments));
        }

        // Tracked, deliberately. Superseding closes the previous contract before recording the
        // new one, and that close is not saved yet — an untracked read would still see the old
        // end date and refuse the new contract for overlapping a contract that is being closed
        // for it.
        var others = await context.Set<EmploymentContract>()
            .Where(c => c.EmployeeNo == employeeNo)
            .Where(c => existingId == null || c.Id != existingId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var clash = others.Find(c =>
            c.StartsOn <= (request.EndsOn ?? DateOnly.MaxValue)
            && (c.EndsOn ?? DateOnly.MaxValue) >= request.StartsOn);

        if (clash is not null)
        {
            arguments["ExistingFrom"] = clash.StartsOn;

            found.Add(messages.Render(HrMessages.ContractOverlaps, arguments));
        }

        return found;
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
