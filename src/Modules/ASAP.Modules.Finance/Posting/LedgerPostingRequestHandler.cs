using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Periods;
using ASAP.Platform.Kernel.Accounting;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Posting;

/// <summary>
/// Posts to the general ledger on behalf of another module.
/// </summary>
/// <remarks>
/// <para>
/// Finance's side of the arrangement that lets modules trade without referencing each other.
/// Inventory raises <see cref="LedgerPostingRequested"/>, this answers it, and neither has heard
/// of the other -- the only thing they share is the kernel contract between them.
/// </para>
/// <para>
/// The request arrives with account numbers rather than account keys, because the asking module
/// reads them from its own setup and has no way to know Finance's identifiers. Resolving them
/// here also means a module naming an account that does not exist gets a proper refusal instead of
/// a foreign key violation.
/// </para>
/// <para>
/// This runs as a domain event, inside the caller's transaction, so a failure here rolls the stock
/// movement back with it. That is the intended behaviour: an item ledger entry that survived while
/// its ledger posting failed would put the inventory account permanently out of step with the
/// valuation, with nothing on the face of either to say when it happened.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="posting">Writes the entries.</param>
/// <param name="setup">Supplies the posting window.</param>
/// <param name="logger">Records what was posted on whose behalf.</param>
public sealed class LedgerPostingRequestHandler(
    AsapDbContext context,
    JournalPostingService posting,
    ISetupService setup,
    ILogger<LedgerPostingRequestHandler> logger) : IEventHandler<LedgerPostingRequested>
{
    /// <inheritdoc />
    public async Task HandleAsync(
        LedgerPostingRequested asapEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asapEvent);

        if (asapEvent.Lines.Count == 0)
        {
            asapEvent.WasHandled = true;
            return;
        }

        var accountNumbers = asapEvent.Lines.Select(static l => l.AccountNo).Distinct().ToList();

        var accounts = await context.Set<GlAccount>()
            .AsNoTracking()
            .Where(a => accountNumbers.Contains(a.No))
            .ToDictionaryAsync(static a => a.No, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var calendar = await FiscalCalendar.LoadAsync(context, cancellationToken).ConfigureAwait(false);

        var lines = asapEvent.Lines
            .Select((line, index) => new PostingLineView(
                LineNo: index + 1,
                PostingDate: asapEvent.PostingDate,
                Amount: line.Amount,
                Account: accounts.TryGetValue(line.AccountNo, out var account)
                    ? PostingAccountView.From(account)
                    : null,
                BalancingAccount: null,
                Dimensions: default,
                DocumentNo: asapEvent.DocumentNo,
                Description: line.Description,
                BranchId: line.BranchId))
            .ToList();

        var environment = new PostingEnvironment(
            BatchCode: asapEvent.SourceModule,
            CurrencyCode: await BaseCurrencyAsync(cancellationToken).ConfigureAwait(false),
            ResolvePeriod: calendar.Resolve,
            CurrencyDecimals: 2,
            PostingWindowFrom: await setup
                .GetAtScopeAsync<DateOnly?>(
                    $"{FinanceModule.Id}.Posting.AllowFrom",
                    SetupScope.Company,
                    null,
                    cancellationToken)
                .ConfigureAwait(false),
            PostingWindowTo: await setup
                .GetAtScopeAsync<DateOnly?>(
                    $"{FinanceModule.Id}.Posting.AllowTo",
                    SetupScope.Company,
                    null,
                    cancellationToken)
                .ConfigureAwait(false),
            MandatoryDimensions: null,
            HeldOverridePermissions: null,

            // Not a manual entry, so the control accounts a person may not touch by hand are open
            // to the module that owns them. That distinction is the entire reason the flag exists:
            // Inventory posting to the inventory account is the account doing its job.
            IsManualEntry: false);

        var request = new PostingRequest(
            SourceCode: asapEvent.SourceCode,
            DocumentType: GlDocumentType.InventoryAdjustment,
            DocumentNo: asapEvent.DocumentNo,
            Description: $"{asapEvent.SourceModule} transaction {asapEvent.SourceTransactionNo}",
            DimensionSetId: asapEvent.DimensionSetId,

            // The asking module already opened a transaction, and its entries carry that number.
            // Reusing it is what makes "show me this whole transaction" one query rather than a
            // reconstruction across two numbers that only happen to be adjacent.
            TransactionNo: asapEvent.SourceTransactionNo);

        var result = await posting
            .PostAsync(lines, environment, request, cancellationToken)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            // Thrown rather than returned. This runs inside the caller's transaction, and a
            // returned failure would be quietly dropped by a module that has no way to interpret
            // a Finance message -- leaving stock moved and its value never booked.
            throw new InvalidOperationException(
                $"{asapEvent.SourceModule} asked for a ledger posting that was refused: "
                + string.Join("; ", result.Failures.Select(static m => $"{m.Code} {m.Title}")));
        }

        asapEvent.WasHandled = true;

        logger.LogInformation(
            "Posted {LineCount} ledger line(s) for {Module} transaction {SourceTransaction} as {TransactionNo}.",
            lines.Count,
            asapEvent.SourceModule,
            asapEvent.SourceTransactionNo,
            result.Value.TransactionNo);
    }

    private async Task<string> BaseCurrencyAsync(CancellationToken cancellationToken)
        => await context.Companies
               .AsNoTracking()
               .Select(static c => c.BaseCurrencyCode)
               .FirstOrDefaultAsync(cancellationToken)
               .ConfigureAwait(false)
           ?? "SAR";
}
