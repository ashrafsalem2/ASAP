using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Parties;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Currencies;

/// <summary>One open balance, and what it is worth at the closing rate.</summary>
/// <param name="PartyNo">Whose it is.</param>
/// <param name="PartyName">What they are called.</param>
/// <param name="DocumentNo">The document still open.</param>
/// <param name="PostingDate">When it was raised.</param>
/// <param name="CurrencyCode">What it is owed in.</param>
/// <param name="RemainingInCurrency">What is still owed, in that currency.</param>
/// <param name="CarryingAmount">What that is carried at in the company's own currency.</param>
/// <param name="ClosingRate">The rate on the day being closed.</param>
/// <param name="RevaluedAmount">What it is worth at that rate.</param>
/// <param name="Difference">What the revaluation would post: positive is a loss.</param>
/// <param name="ControlAccountNo">The account the balance sits on.</param>
public readonly record struct RevaluationRow(
    string PartyNo,
    string PartyName,
    string DocumentNo,
    DateOnly PostingDate,
    string CurrencyCode,
    decimal RemainingInCurrency,
    decimal CarryingAmount,
    decimal ClosingRate,
    decimal RevaluedAmount,
    decimal Difference,
    string ControlAccountNo);

/// <summary>What a revaluation run did.</summary>
/// <param name="AsAt">The day closed.</param>
/// <param name="Rows">What was revalued.</param>
/// <param name="TotalDifference">The net effect, positive being a loss.</param>
/// <param name="TransactionNo">The transaction it posted under, on a run that posted.</param>
public readonly record struct RevaluationRun(
    DateOnly AsAt,
    IReadOnlyList<RevaluationRow> Rows,
    decimal TotalDifference,
    long? TransactionNo);

/// <summary>
/// Restates open foreign balances at the rate on the day being closed.
/// </summary>
/// <remarks>
/// <para>
/// An invoice for a thousand dollars is carried in the books at what a thousand dollars was worth
/// on the day it was raised. The customer still owes a thousand dollars — that never changes —
/// but what the company will get for them does, and a balance sheet that still shows the old
/// figure is claiming an amount nobody will receive.
/// </para>
/// <para>
/// The run measures against what each balance is <em>carried at</em>, not against what it was
/// worth when raised. That is what makes it safe to run twice: after the first run the carrying
/// amount already is the closing valuation, so the second posts nothing. No reversal entry is
/// needed at the start of the next period and none is written — a reversal that somebody forgets
/// to make, or makes twice, is a whole class of error this design does not have.
/// </para>
/// <para>
/// What is never touched is the amount in the foreign currency. The customer owes the same
/// thousand dollars after the run as before, and a revaluation that moved that figure would put a
/// chaser in front of somebody for money they do not owe.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="rates">Says what a currency was worth on the day.</param>
/// <param name="documents">Posts the journal.</param>
/// <param name="setup">Supplies the accounts a difference lands on.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="logger">Records what was restated.</param>
public sealed class CurrencyRevaluationService(
    AsapDbContext context,
    ExchangeRateService rates,
    DocumentPostingService documents,
    ISetupService setup,
    IMessageCatalog messages,
    ILogger<CurrencyRevaluationService> logger)
{
    /// <summary>
    /// What a run on this date would restate, without restating it.
    /// </summary>
    /// <param name="asAt">The day being closed.</param>
    /// <param name="kind">Customers or vendors.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The rows, or why the run cannot be made.</returns>
    public Task<Result<RevaluationRun>> PreviewAsync(
        DateOnly asAt,
        PartyKind kind,
        CancellationToken cancellationToken = default)
        => RunAsync(asAt, kind, post: false, cancellationToken);

    /// <summary>
    /// Restates the open balances and posts the difference.
    /// </summary>
    /// <param name="asAt">The day being closed.</param>
    /// <param name="kind">Customers or vendors.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was restated, or why it could not be.</returns>
    public Task<Result<RevaluationRun>> PostAsync(
        DateOnly asAt,
        PartyKind kind,
        CancellationToken cancellationToken = default)
        => RunAsync(asAt, kind, post: true, cancellationToken);

    private async Task<Result<RevaluationRun>> RunAsync(
        DateOnly asAt,
        PartyKind kind,
        bool post,
        CancellationToken cancellationToken)
        => kind is PartyKind.Customer
            ? await RunAsync<CustomerLedgerEntry>(asAt, post, cancellationToken).ConfigureAwait(false)
            : await RunAsync<VendorLedgerEntry>(asAt, post, cancellationToken).ConfigureAwait(false);

    private async Task<Result<RevaluationRun>> RunAsync<TEntry>(
        DateOnly asAt,
        bool post,
        CancellationToken cancellationToken)
        where TEntry : PartyLedgerEntry
    {
        var open = await context.Set<TEntry>()
            .Where(e => e.IsOpen
                        && e.PostingDate <= asAt
                        && e.CurrencyCode != null
                        && e.RemainingAmountInCurrency != null
                        && e.RemainingAmountInCurrency != 0m)
            .OrderBy(e => e.PartyNo)
            .ThenBy(e => e.PostingDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (open.Count == 0)
        {
            return Result<RevaluationRun>.Success(new RevaluationRun(asAt, [], 0m, null));
        }

        var found = new List<AsapMessage>();
        var multipliers = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        // One rate lookup per currency, and a refusal per currency rather than per entry. Forty
        // invoices in a currency with no closing rate is one thing wrong, not forty.
        foreach (var currency in open.Select(static e => e.CurrencyCode!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var rate = await rates.RateOnAsync(currency, asAt, cancellationToken).ConfigureAwait(false);

            if (rate.Failed)
            {
                found.AddRange(rate.Messages);
                continue;
            }

            multipliers[currency] = rate.Value.Multiplier;
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<RevaluationRun>.Failure(found);
        }

        var rows = new List<RevaluationRow>();

        foreach (var entry in open)
        {
            var multiplier = multipliers[entry.CurrencyCode!];
            var remaining = entry.RemainingAmountInCurrency!.Value;

            var difference = CurrencyRevaluation.Difference(remaining, entry.RemainingAmount, multiplier);

            if (difference == 0m)
            {
                // Already carried at the closing rate. Reporting it would bury the handful of
                // balances that did move under every one that did not.
                continue;
            }

            rows.Add(new RevaluationRow(
                entry.PartyNo,
                entry.PartyName,
                entry.DocumentNo ?? entry.TransactionNo.ToString(),
                entry.PostingDate,
                entry.CurrencyCode!,
                remaining,
                entry.RemainingAmount,
                multiplier,
                CurrencyRevaluation.Revalued(remaining, multiplier),
                difference,
                entry.ControlAccountNo));
        }

        var total = rows.Sum(static r => r.Difference);

        if (!post || rows.Count == 0)
        {
            return Result<RevaluationRun>.Success(new RevaluationRun(asAt, rows, total, null), found);
        }

        var posted = await PostDifferenceAsync(asAt, rows, cancellationToken).ConfigureAwait(false);

        if (posted.Failed)
        {
            return Result<RevaluationRun>.FailureFrom(posted);
        }

        // The sub-ledger is brought to the same figure the ledger now carries. Leaving it behind
        // would make the control account and the sum of the open entries disagree, which is the
        // one reconciliation anybody actually runs.
        foreach (var entry in open)
        {
            var row = rows.Find(r =>
                r.PartyNo == entry.PartyNo
                && r.DocumentNo == (entry.DocumentNo ?? entry.TransactionNo.ToString()));

            if (row.DocumentNo is { Length: > 0 })
            {
                entry.RemainingAmount = row.RevaluedAmount;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Revalued {Count} open balances as at {AsAt}, net {Total}.",
            rows.Count,
            asAt,
            total);

        return Result<RevaluationRun>.Success(
            new RevaluationRun(asAt, rows, total, posted.Value),
            found);
    }

    /// <summary>
    /// Posts one journal for the run, a line per control account and one for the difference.
    /// </summary>
    /// <remarks>
    /// Dated at the day being closed, not at today. The settlement poster deliberately dates its
    /// differences at today, because a realised difference did not exist until the two entries
    /// met — but a revaluation is a statement about what a balance was worth <em>on a date</em>,
    /// and dating it anywhere else leaves the balance sheet at that date saying the old figure.
    /// </remarks>
    private async Task<Result<long>> PostDifferenceAsync(
        DateOnly asAt,
        IReadOnlyList<RevaluationRow> rows,
        CancellationToken cancellationToken)
    {
        var total = rows.Sum(static r => r.Difference);

        var settingKey = total > 0m
            ? $"{FinanceModule.Id}.Currency.LossAccount"
            : $"{FinanceModule.Id}.Currency.GainAccount";

        var account = await setup.GetAsync<string>(settingKey, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(account))
        {
            return Result<long>.Failure(messages.Render(
                FinanceMessages.NoExchangeDifferenceAccount,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SettingKey"] = settingKey,
                    ["Amount"] = Math.Abs(total),
                }));
        }

        // Written the unambiguous way round. A journal description is read by whoever is reconciling
        // months later, and 6/30 is a different day in half the world.
        var description = $"Revaluation of open foreign balances as at {asAt:yyyy-MM-dd}";

        var lines = new List<PostJournalLine>();

        // A line per control account, because a company with separate receivable accounts for
        // trade and for staff wants the movement on the one it belongs to, not on whichever came
        // first.
        foreach (var group in rows.GroupBy(static r => r.ControlAccountNo, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(new PostJournalLine(group.Key, -group.Sum(static r => r.Difference), description));
        }

        lines.Add(new PostJournalLine(account, total, description));

        var posted = await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: "FXREVAL",
                    Lines: lines,
                    SourceCode: "FXREVAL",

                    // Nobody keyed this. The control accounts refuse hand-keyed entries, and a
                    // period-end revaluation is exactly what that restriction leaves room for.
                    IsManualEntry: false,
                    DocumentType: GlDocumentType.None,
                    DocumentNo: null,
                    Description: description,
                    BranchId: null,
                    PostingDate: asAt),
                cancellationToken)
            .ConfigureAwait(false);

        return posted.Failed
            ? Result<long>.FailureFrom(posted)
            : Result<long>.Success(posted.Value.TransactionNo, posted.Messages);
    }
}
