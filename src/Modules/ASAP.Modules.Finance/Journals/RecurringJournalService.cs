using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Reporting;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Journals;

/// <summary>What one run of a recurring batch produced.</summary>
/// <param name="BatchCode">The batch.</param>
/// <param name="On">The day it was posted for.</param>
/// <param name="LinesPosted">How many lines were due and went.</param>
/// <param name="TransactionNo">The transaction the entries carry.</param>
/// <param name="ReversalTransactionNo">
/// The transaction the reversing lines were posted under, when any line reversed.
/// </param>
public readonly record struct RecurringRunSummary(
    string BatchCode,
    DateOnly On,
    int LinesPosted,
    long? TransactionNo,
    long? ReversalTransactionNo);

/// <summary>
/// Posts a recurring batch, then moves its own dates on.
/// </summary>
/// <remarks>
/// <para>
/// Everything it produces is an ordinary journal through the ordinary posting engine. That is
/// deliberate: a recurring journal that posted by its own route would be a second ledger with its
/// own rules, and the first time the two disagreed nobody would know which was right.
/// </para>
/// <para>
/// The dates move only after the posting succeeds. A batch refused for a closed period or a
/// missing dimension value is a batch still due, which is what somebody fixing the problem
/// expects — and the alternative, a batch that moved on without posting, is a month of missing
/// entries that nothing will ever ask about.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="documents">Posts the journal the batch produces.</param>
/// <param name="balances">Reads an account's balance, for a line that posts one.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records what was posted.</param>
public sealed class RecurringJournalService(
    AsapDbContext context,
    DocumentPostingService documents,
    LedgerBalances balances,
    IMessageCatalog messages,
    IClock clock,
    ILogger<RecurringJournalService> logger)
{
    /// <summary>
    /// Posts every line of a batch that is due, and moves those lines on.
    /// </summary>
    /// <param name="code">The batch.</param>
    /// <param name="on">The day to post for, or null for today.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was posted, or every reason nothing was.</returns>
    public async Task<Result<RecurringRunSummary>> PostAsync(
        string code,
        DateOnly? on = null,
        CancellationToken cancellationToken = default)
    {
        var normalised = code.Trim().ToUpperInvariant();
        var day = on ?? clock.Today;

        var batch = await context.Set<RecurringJournalBatch>()
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Code == normalised, cancellationToken)
            .ConfigureAwait(false);

        if (batch is null)
        {
            return Result<RecurringRunSummary>.Failure(messages.Render(
                FinanceMessages.RecurringBatchNotFound, Args(("Batch", normalised))));
        }

        var due = batch.Lines
            .Where(l => l.IsDue(day))
            .OrderBy(static l => l.Order)
            .ToList();

        if (due.Count == 0)
        {
            // Not a failure. A batch asked for before it is due has done exactly what it should,
            // and the caller wants to hear when the next one is rather than see an error.
            return Result<RecurringRunSummary>.Success(
                new RecurringRunSummary(batch.Code, day, 0, null, null),
                [
                    messages.Render(
                        FinanceMessages.RecurringNothingDue,
                        Args(
                            ("Batch", batch.Code),
                            ("Date", day),
                            ("NextDue", batch.NextDue))),
                ]);
        }

        var found = new List<AsapMessage>();
        var lines = new List<PostJournalLine>();
        var reversals = new List<PostJournalLine>();
        var amounts = new Dictionary<Guid, decimal>();

        // Balances are read once, for the whole batch, before anything posts. Reading them per
        // line would let the first line's posting change what the second line sees, which is a
        // dependency on the order somebody happened to put the lines in.
        var chart = due.Exists(static l => l.Method is RecurringMethod.Balance)
            ? await balances.MovementAsync(null, day, cancellationToken).ConfigureAwait(false)
            : [];

        foreach (var line in due)
        {
            var amount = line.Method is RecurringMethod.Balance
                ? -chart.GetValueOrDefault(line.AccountNo)
                : line.Amount;

            if (amount == 0m)
            {
                // Nothing to post is not a failure and not silence either: a variable line
                // nobody filled in, or a balance line on an account that is already empty, and
                // the two look identical from the outside until somebody says which it was.
                found.Add(messages.Render(
                    FinanceMessages.RecurringLineHasNoAmount,
                    Args(("Batch", batch.Code), ("AccountNo", line.AccountNo))));

                continue;
            }

            amounts[line.Id] = amount;

            lines.Add(new PostJournalLine(
                line.AccountNo,
                amount,
                line.Description,
                line.BalancingAccountNo,
                day,
                BranchId: line.BranchId,
                Dimensions: Analysis(line.Dimensions)));

            if (line.Reverses)
            {
                reversals.Add(new PostJournalLine(
                    line.AccountNo,
                    -amount,
                    $"{line.Description} — reversal",
                    line.BalancingAccountNo,
                    day.AddDays(1),
                    BranchId: line.BranchId,
                    Dimensions: Analysis(line.Dimensions)));
            }
        }

        if (lines.Count == 0)
        {
            // Every due line had nothing to post -- variable lines nobody filled in, or balance
            // lines on accounts already empty. Each has said so for itself above, and a run that
            // correctly posted nothing is not a run that failed.
            return Result<RecurringRunSummary>.Success(
                new RecurringRunSummary(batch.Code, day, 0, null, null),
                found);
        }

        var posted = await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: batch.Code,
                    Lines: lines,
                    SourceCode: "RECURJNL",

                    // Nobody keyed this today. Somebody keyed it once, and said to do it every
                    // month; the accounts it writes to are the ones they chose then.
                    IsManualEntry: false,
                    DocumentType: GlDocumentType.None,
                    DocumentNo: batch.Code,
                    Description: batch.Name,
                    PostingDate: day),
                cancellationToken)
            .ConfigureAwait(false);

        if (posted.Failed)
        {
            return Result<RecurringRunSummary>.FailureFrom(posted);
        }

        found.AddRange(posted.Messages);

        long? reversalNo = null;

        if (reversals.Count > 0)
        {
            // The day after, which is what makes an accrual an accrual: the cost belongs to the
            // month that is closing and the invoice will land in the one that is opening.
            var reversed = await documents
                .PostAsync(
                    new DocumentPosting(
                        BatchCode: batch.Code,
                        Lines: reversals,
                        SourceCode: "RECURJNL",
                        IsManualEntry: false,
                        DocumentType: GlDocumentType.None,
                        DocumentNo: batch.Code,
                        Description: $"{batch.Name} — reversal",
                        PostingDate: day.AddDays(1)),
                    cancellationToken)
                .ConfigureAwait(false);

            if (reversed.Failed)
            {
                // The accrual is posted and its reversal is not, which is a worse state than
                // neither. Said plainly rather than left for somebody to find at the next month
                // end, when the cost has been counted twice.
                found.Add(messages.Render(
                    FinanceMessages.RecurringReversalNotPosted,
                    Args(("Batch", batch.Code), ("Date", day.AddDays(1)))));

                found.AddRange(reversed.Messages);

                return Result<RecurringRunSummary>.Failure(found);
            }

            reversalNo = reversed.Value.TransactionNo;
            found.AddRange(reversed.Messages);
        }

        foreach (var line in due.Where(l => amounts.ContainsKey(l.Id)))
        {
            Advance(line, day, found, batch.Code);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Posted recurring batch {Batch} for {Date}: {Count} line(s) as transaction "
            + "{TransactionNo}{Reversal}.",
            batch.Code,
            day,
            lines.Count,
            posted.Value.TransactionNo,
            reversalNo is null ? string.Empty : $", reversed as {reversalNo}");

        return Result<RecurringRunSummary>.Success(
            new RecurringRunSummary(batch.Code, day, lines.Count, posted.Value.TransactionNo, reversalNo),
            found);
    }

    /// <summary>
    /// Moves a line on to its next due date, and clears its amount when the method says to.
    /// </summary>
    /// <remarks>
    /// Stepped from the date it was due rather than from today. A batch posted three days late
    /// is still a monthly batch, and stepping from today would walk its due date forward through
    /// the year every time somebody was on holiday.
    /// </remarks>
    private void Advance(
        RecurringJournalLine line,
        DateOnly day,
        List<AsapMessage> found,
        string batchCode)
    {
        if (line.ClearsAmount)
        {
            line.Amount = 0m;
        }

        if (!DateFormula.TryParse(line.RecurrenceFormula, out var formula))
        {
            // Left where it is rather than guessed at. A line that cannot say when it is next due
            // stays due, which somebody notices; a line advanced by a guess is wrong quietly.
            found.Add(messages.Render(
                FinanceMessages.RecurringFormulaUnreadable,
                Args(
                    ("Batch", batchCode),
                    ("AccountNo", line.AccountNo),
                    ("Formula", line.RecurrenceFormula))));

            return;
        }

        var next = formula.From(line.NextPostingDate ?? day);

        line.NextPostingDate = line.ExpiresOn is { } expires && next > expires ? null : next;
    }

    /// <summary>Reads the dimensions a line names, stored as <c>CODE=VALUE</c> pairs.</summary>
    private static Dictionary<string, string>? Analysis(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var analysis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in stored.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var at = pair.IndexOf('=', StringComparison.Ordinal);

            if (at > 0)
            {
                analysis[pair[..at].Trim()] = pair[(at + 1)..].Trim();
            }
        }

        return analysis.Count > 0 ? analysis : null;
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
