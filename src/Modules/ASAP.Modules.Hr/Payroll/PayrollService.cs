using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
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

namespace ASAP.Modules.Hr.Payroll;

/// <summary>
/// Works out and posts a payroll run.
/// </summary>
/// <remarks>
/// <para>
/// Calculating and posting are two calls on purpose. A payroll that posted as it calculated could
/// not be checked before it committed, and this is the one document in the system that most needs
/// checking: it decides what everybody is paid, and a mistake in it is discovered by four hundred
/// people at once.
/// </para>
/// <para>
/// Posting records what people are owed. It does not pay them — the money leaves when the bank
/// transfer is made, against the same liability. Conflating the two is how a company comes to
/// believe it has paid staff it has not.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="documents">Posts the journal.</param>
/// <param name="leave">Says who was away and on what terms.</param>
/// <param name="numbers">Issues the run number.</param>
/// <param name="setup">Supplies the accounts and the number series.</param>
/// <param name="overrides">Records every protection somebody pushed past.</param>
/// <param name="userContext">Records who posted it.</param>
/// <param name="clock">Supplies the time.</param>
/// <param name="logger">Records runs calculated and posted.</param>
public sealed class PayrollService(
    AsapDbContext context,
    IMessageCatalog messages,
    DocumentPostingService documents,
    Leave.LeaveService leave,
    INumberSeriesService numbers,
    ISetupService setup,
    OverrideAuditor overrides,
    IUserContext userContext,
    IClock clock,
    ILogger<PayrollService> logger)
{
    /// <summary>
    /// Works out what everybody is owed for a period, without committing to it.
    /// </summary>
    /// <param name="from">The first day of the period.</param>
    /// <param name="to">The last day.</param>
    /// <param name="postingDate">The date to post under, or null for the last day of the period.</param>
    /// <param name="description">What to call it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The draft run, and anything worth saying about it.</returns>
    public async Task<Result<PayrollRun>> CalculateAsync(
        DateOnly from,
        DateOnly to,
        DateOnly? postingDate = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var found = new List<AsapMessage>();

        var series = await setup
            .GetAsync<string>($"{HrModule.Id}.Payroll.NumberSeries", cancellationToken)
            .ConfigureAwait(false) ?? "PAYROLL";

        var numbered = await numbers.NextAsync(series, to, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<PayrollRun>.FailureFrom(numbered);
        }

        var run = new PayrollRun
        {
            No = numbered.Value,
            FromDate = from,
            ToDate = to,
            PostingDate = postingDate ?? to,
            Description = description,
            Status = PayrollStatus.Draft,
        };

        // Everybody who worked any part of the period, including leavers: somebody who left on
        // the tenth is owed ten days, and a run that only read current staff would not pay them.
        var employees = await context.Set<Employee>()
            .AsNoTracking()
            .Include(e => e.BranchAssignments)
            .Where(e => e.HiredOn <= to && (e.LeftOn == null || e.LeftOn >= from))
            .OrderBy(static e => e.No)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // One query for the whole run. Unpaid leave is not a rare case that can afford a round
        // trip each: a shop with fifty staff has somebody on unpaid or long-term sick most months.
        var awayByEmployee = await leave
            .UnpaidByEmployeeAsync(from, to, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var employee in employees)
        {
            var line = LineFor(run, employee, from, to, awayByEmployee, found);

            if (line is not null)
            {
                run.Lines.Add(line);
            }
        }

        context.Set<PayrollRun>().Add(run);

        // Said now rather than at posting time, because now is when somebody can still decide
        // they meant to reopen the other one. A draft is not wrong by itself -- only one of two
        // drafts for the same days can ever be posted, and that refusal comes later.
        var draft = await OverlappingAsync(run, PayrollStatus.Draft, cancellationToken)
            .ConfigureAwait(false);

        if (draft is not null)
        {
            found.Add(messages.Render(
                HrMessages.PeriodAlreadyRun,
                Args(
                    ("RunNo", draft.No),
                    ("ExistingFrom", draft.FromDate),
                    ("ExistingTo", draft.ToDate))));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Calculated payroll {RunNo} for {From} to {To}: {LineCount} people, {NetPay} net.",
            run.No,
            from,
            to,
            run.Lines.Count,
            run.NetPay);

        return Result<PayrollRun>.Success(run, found);
    }

    /// <summary>What one person is owed, and where it should be charged.</summary>
    private static decimal Round(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private PayrollLine? LineFor(
        PayrollRun run,
        Employee employee,
        DateOnly from,
        DateOnly to,
        IReadOnlyDictionary<Guid, Leave.LeavePay> awayByEmployee,
        List<AsapMessage> found)
    {
        var byBranch = WageApportionment.DaysByBranch(employee, from, to);
        var unassigned = WageApportionment.UnassignedDays(employee, from, to);

        if (unassigned > 0)
        {
            // Said rather than absorbed. The arithmetic drops a day with no branch, and the cost
            // would otherwise be spread across the branches that do have days -- charging them
            // for a day they had nothing to do with.
            found.Add(messages.Render(
                HrMessages.NoBranchOnDate,
                Args(
                    ("EmployeeNo", employee.No),
                    ("OnDate", from),
                    ("UnassignedDays", unassigned))));
        }

        var daysWorked = byBranch.Values.Sum();

        if (daysWorked == 0)
        {
            return null;
        }

        var days = run.DaysInPeriod;
        var basic = WageApportionment.ForPartMonth(employee.BasicWage, daysWorked, days);
        var allowances = WageApportionment.ForPartMonth(employee.Allowances, daysWorked, days);

        var line = new PayrollLine
        {
            TenantId = run.TenantId,
            CompanyId = run.CompanyId,
            EmployeeId = employee.Id,
            EmployeeNo = employee.No,
            EmployeeName = employee.Name,
            DaysWorked = daysWorked,
            BasicPay = basic,
            Allowances = allowances,
            EndOfServiceCharge = EndOfServiceChargeFor(employee, from, to),
        };

        // Days away that carry no pay come off as a deduction rather than off the days worked.
        // Both arrive at the same net figure, and only one of them leaves a payslip that says
        // what happened -- somebody looking at twenty-two days instead of thirty-one has no way
        // to tell unpaid leave from having joined mid-month.
        if (awayByEmployee.TryGetValue(employee.Id, out var away) && away.UnpaidDays > 0m)
        {
            line.Deductions = Round(employee.TotalWage / 30m * away.UnpaidDays);
            line.Note = $"{away.UnpaidDays:N1} days of leave carrying no pay";
        }

        foreach (var share in WageApportionment.Split(employee, from, to, line.GrossPay))
        {
            line.BranchShares.Add(new PayrollBranchShare
            {
                TenantId = run.TenantId,
                CompanyId = run.CompanyId,
                BranchId = share.BranchId,
                Days = share.Days,
                Amount = share.Amount,
            });
        }

        return line;
    }

    /// <summary>
    /// What the end-of-service provision grew by over the period.
    /// </summary>
    /// <remarks>
    /// The difference between what somebody would have been owed at the start and at the end,
    /// which is what the month actually cost. Charging the whole accrued award every month would
    /// carry a liability many times the real one; charging nothing until they leave would
    /// overstate profit until the day they do.
    /// </remarks>
    private static decimal EndOfServiceChargeFor(Employee employee, DateOnly from, DateOnly to)
    {
        var atStart = EndOfServiceCalculator.For(employee, from.AddDays(-1)).Award;
        var atEnd = EndOfServiceCalculator.For(employee, to).Award;

        return Math.Round(atEnd - atStart, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Commits a run to the ledger.
    /// </summary>
    /// <remarks>
    /// Wages are charged to the branch that had the person, not to whoever is signed in. That is
    /// the whole reason a journal line may name a branch and the whole reason the assignments are
    /// a history.
    /// </remarks>
    /// <param name="runNo">The run to post.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The posted run, or every reason it could not be.</returns>
    public async Task<Result<PayrollRun>> PostAsync(
        string runNo,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        var run = await context.Set<PayrollRun>()
            .Include(r => r.Lines)
            .ThenInclude(l => l.BranchShares)
            .FirstOrDefaultAsync(r => r.No == runNo, cancellationToken)
            .ConfigureAwait(false);

        if (run is null)
        {
            return Result<PayrollRun>.Failure(messages.Render(
                HrMessages.PayrollRunNotFound,
                Args(("RunNo", runNo))));
        }

        if (!run.IsEditable)
        {
            return Result<PayrollRun>.Failure(messages.Render(
                HrMessages.PayrollAlreadyPosted,
                Args(("RunNo", runNo), ("TransactionNo", run.TransactionNo))));
        }

        var found = new List<AsapMessage>();

        // Re-posting this run is caught above. Posting a *different* run over the same days is
        // not, and it is the same mistake with none of the same evidence: two runs, both with
        // their own number, both apparently fine, and everybody paid twice for the overlap.
        var paid = await OverlappingAsync(run, PayrollStatus.Posted, cancellationToken)
            .ConfigureAwait(false);

        if (paid is not null)
        {
            var refusal = Raise(
                HrMessages.PeriodAlreadyPaid,
                Args(
                    ("RunNo", paid.No),
                    ("TransactionNo", paid.TransactionNo),
                    ("ExistingFrom", paid.FromDate),
                    ("ExistingTo", paid.ToDate)),
                heldOverridePermissions);

            if (refusal.Severity is MessageSeverity.Blocked)
            {
                return Result<PayrollRun>.Failure(refusal);
            }

            found.Add(refusal);
        }

        overrides.Record(found, "Hr.Payroll", run.No, overrideReason);

        var wageAccount = await AccountAsync("Posting.WageAccount", "6100", cancellationToken)
            .ConfigureAwait(false);

        var payableAccount = await AccountAsync("Posting.PayableAccount", "2400", cancellationToken)
            .ConfigureAwait(false);

        var provisionAccount = await setup
            .GetAsync<string>($"{HrModule.Id}.Posting.EndOfServiceAccount", cancellationToken)
            .ConfigureAwait(false);

        // Kept off the wage account on purpose. Somebody reading "salaries and wages" is asking
        // what was paid to people this month; what was added to what they will be owed when they
        // go is a real cost of the same month, but it was not paid to anybody.
        var provisionExpenseAccount = await AccountAsync(
                "Posting.EndOfServiceExpenseAccount", "6110", cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(provisionAccount))
        {
            return Result<PayrollRun>.Failure(messages.Render(
                HrMessages.NoProvisionAccount,
                Args(("Amount", run.EndOfServiceCharge))));
        }

        var journal = new List<PostJournalLine>();

        foreach (var line in run.Lines)
        {
            // One debit per branch, so the cost lands where the work was done. A single debit for
            // the whole run would be arithmetically correct and would make cost per branch
            // unanswerable, which is the question the whole design exists to answer.
            foreach (var share in line.BranchShares)
            {
                journal.Add(new PostJournalLine(
                    wageAccount,
                    share.Amount,
                    $"{line.EmployeeNo} {line.EmployeeName}",
                    BranchId: share.BranchId));
            }

            var days = line.BranchShares.Select(static s => (s.BranchId, s.Days)).ToList();

            if (line.Deductions != 0m)
            {
                // A deduction reduces what employing somebody cost, and it cost the branches
                // that had them. Crediting it centrally would leave every branch overstated by
                // its share and head office understated by the whole.
                foreach (var share in WageApportionment.Reapportion(days, line.Deductions))
                {
                    journal.Add(new PostJournalLine(
                        wageAccount,
                        -share.Amount,
                        $"{line.EmployeeNo} — deductions",
                        BranchId: share.BranchId));
                }
            }

            if (line.EndOfServiceCharge != 0m)
            {
                // What employing somebody this month added to what the company will owe them
                // when they go. A cost of the month, not of the day somebody resigns -- and a
                // cost of the branches the month was worked at, for the same reason the wage is.
                foreach (var share in WageApportionment.Reapportion(days, line.EndOfServiceCharge))
                {
                    journal.Add(new PostJournalLine(
                        provisionExpenseAccount,
                        share.Amount,
                        $"{line.EmployeeNo} — end of service earned",
                        BranchId: share.BranchId));
                }
            }
        }

        if (run.NetPay != 0m)
        {
            journal.Add(new PostJournalLine(
                payableAccount,
                -run.NetPay,
                $"{run.No} — net pay owed"));
        }

        if (run.EndOfServiceCharge != 0m)
        {
            // The cost was charged per branch above; the liability is not divided. What the
            // company will owe when people leave is owed by the company, and a branch that
            // closes does not take a share of it with it.
            journal.Add(new PostJournalLine(
                provisionAccount,
                -run.EndOfServiceCharge,
                $"{run.No} — end of service provision"));
        }

        if (journal.Count == 0)
        {
            return Result<PayrollRun>.Success(run, found);
        }

        var posted = await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: run.No,
                    Lines: journal,
                    SourceCode: "PAYROLL",

                    // Nobody keyed this. HR owns the payable and provision accounts it writes to,
                    // and the restriction on them exists to leave room for exactly this.
                    IsManualEntry: false,
                    DocumentType: GlDocumentType.Payroll,
                    DocumentNo: run.No,
                    Description: run.Description ?? $"Payroll {run.FromDate:yyyy-MM}"),
                cancellationToken)
            .ConfigureAwait(false);

        if (posted.Failed)
        {
            return Result<PayrollRun>.FailureFrom(posted);
        }

        run.Status = PayrollStatus.Posted;
        run.TransactionNo = posted.Value.TransactionNo;
        run.PostedAtUtc = clock.UtcNow;
        run.PostedBy = userContext.UserId;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Posted payroll {RunNo} as transaction {TransactionNo}: {NetPay} owed to {Count} people.",
            run.No,
            run.TransactionNo,
            run.NetPay,
            run.Lines.Count);

        return Result<PayrollRun>.Success(
            run,
            [.. found, .. posted.Messages.Where(static m => m.Severity is not MessageSeverity.Success)]);
    }

    /// <summary>
    /// Discards a draft run.
    /// </summary>
    /// <remarks>
    /// A draft that cannot be got rid of is not harmless. It sits in the list looking like work
    /// somebody still has to do, and the day it is finally posted it pays a month that was paid
    /// months ago. A posted run is a different matter and is not touched here: it is reversed,
    /// so the ledger shows both what was done and that it was undone.
    /// </remarks>
    /// <param name="runNo">The run to discard.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The discarded run, or why it could not be.</returns>
    public async Task<Result<PayrollRun>> DiscardAsync(
        string runNo,
        CancellationToken cancellationToken = default)
    {
        // The lines and their branch shares are loaded so they go with it. A delete cascades
        // only as far as what is tracked, and a hidden run whose lines are still visible is
        // worse than either: nothing shows the run, and any report reading lines still counts
        // what it cost.
        var run = await context.Set<PayrollRun>()
            .Include(r => r.Lines)
            .ThenInclude(l => l.BranchShares)
            .FirstOrDefaultAsync(r => r.No == runNo, cancellationToken)
            .ConfigureAwait(false);

        if (run is null)
        {
            return Result<PayrollRun>.Failure(messages.Render(
                HrMessages.PayrollRunNotFound,
                Args(("RunNo", runNo))));
        }

        if (!run.IsEditable)
        {
            return Result<PayrollRun>.Failure(messages.Render(
                HrMessages.PayrollPostedCannotDiscard,
                Args(("RunNo", runNo), ("TransactionNo", run.TransactionNo))));
        }

        context.Set<PayrollRun>().Remove(run);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Discarded draft payroll {RunNo}.", run.No);

        return Result<PayrollRun>.Success(run);
    }

    /// <summary>Reads one run.</summary>
    /// <param name="runNo">Its number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The run, or null when nothing is numbered that.</returns>
    public Task<PayrollRun?> LoadAsync(string runNo, CancellationToken cancellationToken = default)
        => context.Set<PayrollRun>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .ThenInclude(l => l.BranchShares)
            .FirstOrDefaultAsync(r => r.No == runNo, cancellationToken);

    /// <summary>Every run, most recent first.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The runs, with their lines.</returns>
    public async Task<IReadOnlyList<PayrollRun>> ListAsync(CancellationToken cancellationToken = default)
        => await context.Set<PayrollRun>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .OrderByDescending(static r => r.FromDate)
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<string> AccountAsync(
        string key,
        string fallback,
        CancellationToken cancellationToken)
        => await setup
               .GetAsync<string>($"{HrModule.Id}.{key}", cancellationToken)
               .ConfigureAwait(false)
           ?? fallback;

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }
    /// <summary>
    /// Finds a run in the given state whose days overlap this one's.
    /// </summary>
    /// <remarks>
    /// Two periods overlap unless one ends before the other begins, which is shorter to write
    /// than the four cases people reach for and gets them all right.
    /// </remarks>
    private Task<PayrollRun?> OverlappingAsync(
        PayrollRun run,
        PayrollStatus status,
        CancellationToken cancellationToken)
        => context.Set<PayrollRun>()
            .AsNoTracking()
            .Where(r => r.Id != run.Id
                        && r.Status == status
                        && r.FromDate <= run.ToDate
                        && r.ToDate >= run.FromDate)
            .OrderBy(static r => r.FromDate)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Renders a refusal, downgraded to a warning where the caller may push past it.</summary>
    private AsapMessage Raise(
        MessageCode code,
        Dictionary<string, object?> arguments,
        IReadOnlySet<string>? held)
    {
        var rendered = messages.Render(code, arguments);

        return rendered.OverridePermission is { } permission && held?.Contains(permission) == true
            ? messages.AsOverridden(rendered)
            : rendered;
    }

}
