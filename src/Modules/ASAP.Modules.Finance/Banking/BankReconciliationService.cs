using ASAP.Modules.Finance.Ledger;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Banking;

/// <summary>One ledger entry the bank has not seen yet.</summary>
/// <param name="EntryId">The entry.</param>
/// <param name="PostingDate">When it was posted.</param>
/// <param name="DocumentNo">What document it came from.</param>
/// <param name="Description">What it says.</param>
/// <param name="Amount">How much, on the ledger's convention.</param>
public readonly record struct OutstandingItem(
    Guid EntryId,
    DateOnly PostingDate,
    string? DocumentNo,
    string Description,
    decimal Amount);

/// <summary>
/// Where a reconciliation stands, in the form an accountant would write it out.
/// </summary>
/// <param name="StatementNo">The statement.</param>
/// <param name="StatementDate">The day it reconciles to.</param>
/// <param name="ClosingBalance">What the bank says the balance is.</param>
/// <param name="LedgerBalance">What the books say it is, on the same day.</param>
/// <param name="Outstanding">
/// Entries in the books the bank has not seen. Cheques written and not presented, deposits
/// banked too late in the day to appear.
/// </param>
/// <param name="OutstandingTotal">What those come to.</param>
/// <param name="UnmatchedLines">Statement lines with nothing in the books behind them yet.</param>
/// <param name="Difference">
/// What is left when the outstanding items are taken off the gap between the two balances. Nought
/// is the only value that proves anything.
/// </param>
public readonly record struct ReconciliationPosition(
    string StatementNo,
    DateOnly StatementDate,
    decimal ClosingBalance,
    decimal LedgerBalance,
    IReadOnlyList<OutstandingItem> Outstanding,
    decimal OutstandingTotal,
    int UnmatchedLines,
    decimal Difference)
{
    /// <summary>Whether the reconciliation proves, and so whether it may be closed.</summary>
    public bool Balances => Difference == 0m && UnmatchedLines == 0;
}

/// <summary>
/// Agrees a bank statement with the ledger, and refuses to say it agrees when it does not.
/// </summary>
/// <remarks>
/// <para>
/// The identity the whole thing rests on: every entry on the bank's ledger account is either one
/// the bank has seen — matched to a statement line, here or on an earlier statement — or one it
/// has not. So the books must be ahead of the bank by exactly the unseen ones:
/// </para>
/// <para>
/// <c>ledger balance − bank closing balance = outstanding items</c>
/// </para>
/// <para>
/// That is a cumulative check, not a check of this month. An entry matched to the wrong line last
/// March throws this month's figure out by the difference, which is a feature: the error surfaces
/// at the next reconciliation rather than sitting in the books until somebody happens to look.
/// </para>
/// <para>
/// Matching a line to an entry of a different amount is allowed and warned about, because a
/// single bank line covering two payments is real and somebody has to be able to record it. The
/// arithmetic above is what actually protects the result, and it is checked at the moment of
/// closing rather than at every keystroke — so the work can be done in any order and only the
/// claim at the end has to be true.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="userContext">Records who agreed a statement.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records reconciliations.</param>
public sealed class BankReconciliationService(
    AsapDbContext context,
    IMessageCatalog messages,
    IUserContext userContext,
    IClock clock,
    ILogger<BankReconciliationService> logger)
{
    /// <summary>
    /// Works out where a reconciliation stands.
    /// </summary>
    /// <param name="statementId">The statement.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The position, or why it could not be worked out.</returns>
    public async Task<Result<ReconciliationPosition>> PositionAsync(
        Guid statementId,
        CancellationToken cancellationToken = default)
    {
        var statement = await LoadAsync(statementId, cancellationToken).ConfigureAwait(false);

        if (statement is null)
        {
            return Result<ReconciliationPosition>.Failure(
                messages.Render(FinanceMessages.BankStatementNotFound, Args(("Statement", statementId))));
        }

        return Result<ReconciliationPosition>.Success(
            await ComputeAsync(statement, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Says which ledger entry each unmatched line looks like, where that is not a guess.
    /// </summary>
    /// <param name="statementId">The statement.</param>
    /// <param name="withinDays">How far either side of the bank's date to look.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One suggestion per line that has exactly one candidate.</returns>
    /// <remarks>
    /// Exactly one, deliberately. Two entries of the same amount within the window is the
    /// commonest case in a real ledger — a shop banking the same float twice in a week — and
    /// picking either would be a coin toss that looks like a decision. Those lines are left for a
    /// person, which is the honest answer.
    /// </remarks>
    public async Task<IReadOnlyList<(Guid LineId, Guid EntryId)>> SuggestAsync(
        Guid statementId,
        int withinDays = 5,
        CancellationToken cancellationToken = default)
    {
        var statement = await LoadAsync(statementId, cancellationToken).ConfigureAwait(false);

        if (statement?.BankAccount is null)
        {
            return [];
        }

        var candidates = await UnmatchedEntriesAsync(
                statement.BankAccount.GlAccountNo,
                statement.StatementDate.AddDays(withinDays),
                cancellationToken)
            .ConfigureAwait(false);

        var suggestions = new List<(Guid, Guid)>();
        var taken = new HashSet<Guid>();

        foreach (var line in statement.Lines.Where(static l => !l.IsMatched).OrderBy(static l => l.TransactionDate))
        {
            var matches = candidates
                .Where(e => !taken.Contains(e.Id))
                .Where(e => e.Amount == line.Amount)
                .Where(e => Math.Abs(e.PostingDate.DayNumber - line.TransactionDate.DayNumber) <= withinDays)
                .Take(2)
                .ToList();

            if (matches.Count != 1)
            {
                continue;
            }

            taken.Add(matches[0].Id);
            suggestions.Add((line.Id, matches[0].Id));
        }

        return suggestions;
    }

    /// <summary>
    /// Records that a statement line is a particular ledger entry.
    /// </summary>
    /// <param name="lineId">The statement line.</param>
    /// <param name="entryId">The ledger entry it turned out to be.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whatever was worth saying, or every reason it was refused.</returns>
    public async Task<Result> MatchAsync(
        Guid lineId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var line = await context.Set<BankStatementLine>()
            .Include(l => l.BankStatement!)
            .ThenInclude(s => s.BankAccount)
            .FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken)
            .ConfigureAwait(false);

        if (line?.BankStatement?.BankAccount is null)
        {
            return Result.Failure(
                messages.Render(FinanceMessages.BankStatementNotFound, Args(("Statement", lineId))));
        }

        if (!line.BankStatement.IsEditable)
        {
            return Result.Failure(messages.Render(
                FinanceMessages.StatementAlreadyReconciled,
                Args(("Statement", line.BankStatement.No))));
        }

        var entry = await context.Set<GlEntry>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return Result.Failure(
                messages.Render(FinanceMessages.BankEntryNotFound, Args(("Entry", entryId))));
        }

        // An entry on another account cannot be what this line was, whatever the amount says.
        if (!string.Equals(
                entry.AccountNo,
                line.BankStatement.BankAccount.GlAccountNo,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(messages.Render(
                FinanceMessages.BankEntryOnAnotherAccount,
                Args(
                    ("Entry", entry.DocumentNo ?? $"#{entry.TransactionNo}"),
                    ("AccountNo", entry.AccountNo),
                    ("BankAccountNo", line.BankStatement.BankAccount.GlAccountNo))));
        }

        var alreadyTaken = await context.Set<BankStatementLine>()
            .AnyAsync(l => l.MatchedEntryId == entryId && l.Id != lineId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyTaken)
        {
            return Result.Failure(messages.Render(
                FinanceMessages.BankEntryAlreadyMatched,
                Args(("Entry", entry.DocumentNo ?? $"#{entry.TransactionNo}"))));
        }

        var found = new List<AsapMessage>();

        // Warned rather than refused. One bank line covering two payments is real, and somebody
        // has to be able to say so. What protects the result is the arithmetic at closing, which
        // this cannot slip past.
        if (entry.Amount != line.Amount)
        {
            found.Add(messages.Render(
                FinanceMessages.BankMatchAmountDiffers,
                Args(
                    ("LineAmount", line.Amount),
                    ("EntryAmount", entry.Amount),
                    ("Difference", line.Amount - entry.Amount))));
        }

        line.MatchedEntryId = entryId;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(found);
    }

    /// <summary>
    /// Takes a match back off a line.
    /// </summary>
    /// <param name="lineId">The statement line.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Nothing, or why it was refused.</returns>
    public async Task<Result> UnmatchAsync(Guid lineId, CancellationToken cancellationToken = default)
    {
        var line = await context.Set<BankStatementLine>()
            .Include(l => l.BankStatement)
            .FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken)
            .ConfigureAwait(false);

        if (line?.BankStatement is null)
        {
            return Result.Failure(
                messages.Render(FinanceMessages.BankStatementNotFound, Args(("Statement", lineId))));
        }

        if (!line.BankStatement.IsEditable)
        {
            return Result.Failure(messages.Render(
                FinanceMessages.StatementAlreadyReconciled,
                Args(("Statement", line.BankStatement.No))));
        }

        line.MatchedEntryId = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Closes a statement, if and only if it proves.
    /// </summary>
    /// <param name="statementId">The statement.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Where it ended up, or every reason it does not agree.</returns>
    public async Task<Result<ReconciliationPosition>> ReconcileAsync(
        Guid statementId,
        CancellationToken cancellationToken = default)
    {
        var statement = await LoadAsync(statementId, cancellationToken).ConfigureAwait(false);

        if (statement is null)
        {
            return Result<ReconciliationPosition>.Failure(
                messages.Render(FinanceMessages.BankStatementNotFound, Args(("Statement", statementId))));
        }

        if (!statement.IsEditable)
        {
            return Result<ReconciliationPosition>.Failure(messages.Render(
                FinanceMessages.StatementAlreadyReconciled, Args(("Statement", statement.No))));
        }

        var found = new List<AsapMessage>();

        // Checked before anything else, and worth its own message: when the lines do not add up
        // to the movement the statement itself claims, the statement was keyed or imported
        // wrong. No amount of matching will ever close it, and saying so here saves an afternoon
        // spent looking for the difference in the ledger.
        if (statement.LineTotal != statement.StatementMovement)
        {
            found.Add(messages.Render(
                FinanceMessages.StatementLinesDoNotAddUp,
                Args(
                    ("Statement", statement.No),
                    ("LineTotal", statement.LineTotal),
                    ("Movement", statement.StatementMovement),
                    ("Difference", statement.LineTotal - statement.StatementMovement))));
        }

        var position = await ComputeAsync(statement, cancellationToken).ConfigureAwait(false);

        if (position.UnmatchedLines > 0)
        {
            found.Add(messages.Render(
                FinanceMessages.StatementLinesUnmatched,
                Args(("Statement", statement.No), ("Count", position.UnmatchedLines))));
        }

        if (position.Difference != 0m)
        {
            found.Add(messages.Render(
                FinanceMessages.ReconciliationDoesNotBalance,
                Args(
                    ("Statement", statement.No),
                    ("LedgerBalance", position.LedgerBalance),
                    ("ClosingBalance", position.ClosingBalance),
                    ("OutstandingTotal", position.OutstandingTotal),
                    ("Difference", position.Difference))));
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<ReconciliationPosition>.Failure(found);
        }

        statement.Status = BankStatementStatus.Reconciled;
        statement.ReconciledOn = clock.Today;
        statement.ReconciledBy = userContext.UserId;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Reconciled bank statement {Statement} to {Date}: ledger {Ledger}, bank {Bank}, "
            + "{Count} item(s) outstanding worth {Outstanding}.",
            statement.No,
            statement.StatementDate,
            position.LedgerBalance,
            position.ClosingBalance,
            position.Outstanding.Count,
            position.OutstandingTotal);

        return Result<ReconciliationPosition>.Success(position, found);
    }

    /// <summary>
    /// Works the position out from the ledger and the statement together.
    /// </summary>
    private async Task<ReconciliationPosition> ComputeAsync(
        BankStatement statement,
        CancellationToken cancellationToken)
    {
        var glAccountNo = statement.BankAccount?.GlAccountNo ?? string.Empty;

        var ledgerBalance = await context.Set<GlEntry>()
            .Where(e => e.AccountNo == glAccountNo && e.PostingDate <= statement.StatementDate)
            .SumAsync(static e => e.Amount, cancellationToken)
            .ConfigureAwait(false);

        var outstanding = await UnmatchedEntriesAsync(
                glAccountNo,
                statement.StatementDate,
                cancellationToken)
            .ConfigureAwait(false);

        var outstandingTotal = outstanding.Sum(static e => e.Amount);

        return new ReconciliationPosition(
            statement.No,
            statement.StatementDate,
            statement.ClosingBalance,
            ledgerBalance,
            [
                .. outstanding.Select(static e => new OutstandingItem(
                    e.Id, e.PostingDate, e.DocumentNo, e.Description, e.Amount)),
            ],
            outstandingTotal,
            statement.Lines.Count(static l => !l.IsMatched),

            // The identity the whole thing rests on. Anything left over is a real disagreement
            // between the bank and the books, and the number itself is usually the clue: twice a
            // figure means a sign, a round hundred means a transposition.
            ledgerBalance - statement.ClosingBalance - outstandingTotal);
    }

    /// <summary>
    /// Entries on the bank's ledger account that no statement line anywhere points at.
    /// </summary>
    /// <remarks>
    /// Every statement, not merely this one. An entry matched last month is one the bank has seen,
    /// and counting it as outstanding again would overstate the gap by its amount every month
    /// thereafter.
    /// </remarks>
    private async Task<List<GlEntry>> UnmatchedEntriesAsync(
        string glAccountNo,
        DateOnly upTo,
        CancellationToken cancellationToken)
    {
        var matched = context.Set<BankStatementLine>()
            .Where(static l => l.MatchedEntryId != null)
            .Select(static l => l.MatchedEntryId!.Value);

        return await context.Set<GlEntry>()
            .AsNoTracking()
            .Where(e => e.AccountNo == glAccountNo && e.PostingDate <= upTo)
            .Where(e => !matched.Contains(e.Id))
            .OrderBy(static e => e.PostingDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<BankStatement?> LoadAsync(Guid statementId, CancellationToken cancellationToken)
        => context.Set<BankStatement>()
            .Include(s => s.BankAccount)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == statementId, cancellationToken);

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
