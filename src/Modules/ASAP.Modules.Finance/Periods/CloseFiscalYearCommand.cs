using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Periods;

/// <summary>What the year-end transfer did.</summary>
/// <param name="YearCode">The year that was closed.</param>
/// <param name="PostingDate">The day the transfer was posted on, which is the year's last.</param>
/// <param name="TransactionNo">The transaction it was posted under, or null where there was nothing to transfer.</param>
/// <param name="Result">The year's result. Positive is a profit.</param>
/// <param name="RetainedEarningsAccountNo">Where it went.</param>
/// <param name="AccountsCleared">How many income statement accounts were brought back to zero.</param>
/// <param name="YearLocked">Whether the year was also locked against further posting.</param>
public sealed record YearEndReceipt(
    string YearCode,
    DateOnly PostingDate,
    long? TransactionNo,
    decimal Result,
    string RetainedEarningsAccountNo,
    int AccountsCleared,
    bool YearLocked);

/// <summary>
/// Transfers a year's result to retained earnings and, unless told otherwise, locks the year.
/// </summary>
/// <param name="YearCode">Which year.</param>
/// <param name="LockTheYear">
/// Whether to stop the year accepting further postings once the transfer has run. Nearly always
/// yes; the exception is transferring early to see the shape of next year's opening balance sheet
/// while the auditors are still asking questions about this one.
/// </param>
/// <param name="Reason">What the entries should say, beyond that they are the year-end transfer.</param>
[RequiresPermission("Finance", "Period", PermissionAction.Update)]
public sealed record CloseFiscalYearCommand(
    string YearCode,
    bool LockTheYear = true,
    string? Reason = null) : ICommand<YearEndReceipt>;

/// <summary>
/// Runs the year-end transfer.
/// </summary>
/// <remarks>
/// <para>
/// Income statement accounts measure a period and start each one at nothing. The transfer is what
/// makes that true: it posts, on the year's last day, the exact opposite of every balance those
/// accounts hold, and the other side of it — the year's result — goes to retained earnings, where
/// it belongs to the owners. Without it the new year opens with last year's revenue still on the
/// books and every income statement afterwards is the sum of every year so far.
/// </para>
/// <para>
/// Each account is cleared per branch rather than in one line. The balances went on carrying a
/// branch; taking them off without one would leave every shop's revenue account showing a balance
/// that the company total says is zero, which is the kind of discrepancy that gets found two
/// years later by somebody who cannot explain it.
/// </para>
/// <para>
/// Retained earnings takes the result whole, in one line. What the owners have is not divisible
/// between shops and a branch that closes does not take a share of the accumulated profit away
/// with it, so the line names no branch of its own — which means it inherits the branch of
/// whoever ran the routine, the ledger having no way to say "deliberately none". Nothing reads
/// it: branch performance covers the income statement, where every figure was cleared per branch
/// above. Worth knowing before anybody builds a balance sheet by branch, which would be the
/// first report to notice.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="documents">Posts the transfer.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="setup">Supplies the retained earnings account.</param>
/// <param name="userContext">Records who closed the year.</param>
/// <param name="clock">Supplies the time.</param>
/// <param name="logger">Records year ends.</param>
public sealed class CloseFiscalYearCommandHandler(
    AsapDbContext context,
    DocumentPostingService documents,
    IMessageCatalog messages,
    ISetupService setup,
    IUserContext userContext,
    IClock clock,
    ILogger<CloseFiscalYearCommandHandler> logger)
    : IRequestHandler<CloseFiscalYearCommand, Result<YearEndReceipt>>
{
    /// <summary>The categories that measure a period rather than a position.</summary>
    private static readonly GlAccountCategory[] IncomeStatement =
    [
        GlAccountCategory.Income,
        GlAccountCategory.CostOfGoodsSold,
        GlAccountCategory.Expense,
    ];

    /// <inheritdoc />
    public async Task<Result<YearEndReceipt>> HandleAsync(
        CloseFiscalYearCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var year = await context.Set<FiscalYear>()
            .Include(y => y.Periods)
            .FirstOrDefaultAsync(y => y.Code == request.YearCode, cancellationToken)
            .ConfigureAwait(false);

        if (year is null)
        {
            return Result<YearEndReceipt>.Failure(messages.Render(
                FinanceMessages.FiscalYearNotFound,
                Args(("YearCode", request.YearCode))));
        }

        if (year.IncomeTransferred)
        {
            return Result<YearEndReceipt>.Failure(messages.Render(
                FinanceMessages.YearAlreadyTransferred,
                Args(("YearCode", year.Code), ("EndDate", year.EndDate))));
        }

        if (year.IsClosed)
        {
            return Result<YearEndReceipt>.Failure(messages.Render(
                FinanceMessages.YearLockedBeforeTransfer,
                Args(("YearCode", year.Code))));
        }

        // An earlier year whose result was never moved is still sitting in the income statement
        // accounts. Sweeping it up now would report it as part of this year's result, which is
        // wrong twice: this year is overstated and the year it belongs to stays understated for
        // ever, with nothing on either statement to say so.
        var earlier = await context.Set<FiscalYear>()
            .AsNoTracking()
            .Where(y => y.EndDate < year.StartDate && !y.IncomeTransferred)
            .OrderBy(static y => y.StartDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (earlier is not null)
        {
            return Result<YearEndReceipt>.Failure(messages.Render(
                FinanceMessages.EarlierYearNotTransferred,
                Args(("YearCode", year.Code), ("EarlierYearCode", earlier.Code))));
        }

        var retained = await setup
            .GetAsync<string>($"{FinanceModule.Id}.General.RetainedEarningsAccount", cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(retained))
        {
            return Result<YearEndReceipt>.Failure(
                messages.Render(FinanceMessages.NoRetainedEarningsAccount, Args(("YearCode", year.Code))));
        }

        var categories = await context.Set<GlAccount>()
            .AsNoTracking()
            .Where(a => IncomeStatement.Contains(a.Category))
            .Select(static a => a.No)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var balances = await context.Set<GlEntry>()
            .AsNoTracking()
            .Where(e => e.PostingDate >= year.StartDate
                        && e.PostingDate <= year.EndDate
                        && categories.Contains(e.AccountNo))
            .GroupBy(static e => new { e.AccountNo, e.BranchId })
            .Select(static g => new
            {
                g.Key.AccountNo,
                g.Key.BranchId,
                Amount = g.Sum(static e => e.Amount),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var description = request.Reason is { Length: > 0 } reason
            ? $"Year end {year.Code}: {reason}"
            : $"Year end {year.Code}";

        var (lines, result) = YearEndLines.For(
            balances.Select(static b => new YearEndBalance(b.AccountNo, b.BranchId, b.Amount)),
            retained,
            description,
            year.EndDate);

        long? transactionNo = null;

        if (lines.Count > 0)
        {
            var posted = await documents
                .PostAsync(
                    new DocumentPosting(
                        BatchCode: year.Code,
                        Lines: lines,
                        SourceCode: "YEAREND",

                        // Nobody keyed this. Every account it touches is one a person may not post
                        // to by hand, which is exactly why the routine that owns the job does it.
                        IsManualEntry: false,
                        DocumentType: GlDocumentType.YearEndClose,
                        DocumentNo: year.Code,
                        Description: description),
                    cancellationToken)
                .ConfigureAwait(false);

            if (posted.Failed)
            {
                return Result<YearEndReceipt>.FailureFrom(posted);
            }

            transactionNo = posted.Value.TransactionNo;
        }

        year.IncomeTransferred = true;

        if (request.LockTheYear)
        {
            year.IsClosed = true;
            year.ClosedAtUtc = clock.UtcNow;
            year.ClosedBy = userContext.UserId;

            // A locked year with open periods inside it says two different things about whether
            // anything may still be posted to it.
            foreach (var period in year.Periods)
            {
                period.IsClosed = true;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Year {YearCode} transferred {Result} to {Account} as transaction {TransactionNo}; locked: {Locked}.",
            year.Code,
            result,
            retained,
            transactionNo,
            request.LockTheYear);

        var found = new List<AsapMessage>();

        if (lines.Count == 0)
        {
            found.Add(messages.Render(
                FinanceMessages.NothingToTransfer,
                Args(("YearCode", year.Code))));
        }

        return Result<YearEndReceipt>.Success(
            new YearEndReceipt(
                year.Code,
                year.EndDate,
                transactionNo,
                result,
                retained,
                balances.Count(static b => b.Amount != 0m),
                request.LockTheYear),
            found);
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
