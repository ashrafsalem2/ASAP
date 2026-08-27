using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Kernel.Accounting;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Posting;

/// <summary>
/// The half of stock posting that asks for its value to reach a general ledger.
/// </summary>
/// <remarks>
/// Kept in its own file because it is a different concern from moving stock, and because it is the
/// only place Inventory says anything about accounting at all.
/// </remarks>
public sealed partial class StockPostingService
{
    /// <summary>
    /// Asks whichever module owns the general ledger to post the value of these movements.
    /// </summary>
    /// <param name="movements">
    /// Each movement with the part of its cost that is settled. Passed in rather than read back
    /// from the database, because at this point the value entries are still unsaved in the change
    /// tracker: a query would find nothing, the posting would be for zero, and nothing would
    /// complain. It is the kind of defect that shows up as an inventory account that never moves.
    /// </param>
    /// <param name="postingDate">The date the entries should be reported in.</param>
    /// <param name="sourceCode">Where the movements came from.</param>
    /// <param name="documentNo">The document behind them.</param>
    /// <param name="transactionNo">The transaction the movements belong to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <remarks>
    /// <para>
    /// Raised as a kernel event, so Inventory neither knows nor cares which module answers, or
    /// whether one does. On an installation without Finance nothing subscribes, nothing is posted,
    /// and stock still moves -- the value entries remain the truth, they simply have nowhere to be
    /// summarised to yet.
    /// </para>
    /// <para>
    /// Estimated cost never reaches the ledger. A figure nobody has confirmed would put the
    /// inventory account out of step with the stock valuation by exactly the amount still in
    /// doubt; it posts later, when the settlement routine turns the estimate into a fact.
    /// </para>
    /// </remarks>
    private async Task RequestLedgerPostingAsync(
        IReadOnlyList<(ItemLedgerEntry Entry, decimal SettledCost, string? ContraAccountNo)> movements,
        DateOnly postingDate,
        string sourceCode,
        string? documentNo,
        long transactionNo,
        CancellationToken cancellationToken)
    {
        var lines = new List<LedgerPostingLine>();

        foreach (var (entry, settledCost, contraAccountNo) in movements)
        {
            if (settledCost == 0m)
            {
                continue;
            }

            var accounts = await AccountsForAsync(entry.ItemId, cancellationToken).ConfigureAwait(false);

            // The shop the stock moved in or out of, taken per movement rather than per
            // posting: a transfer is one transaction with a side in each of two branches, and a
            // single branch for the whole of it would be wrong for one of them by construction.
            var branchId = await branches
                .BranchOfAsync(entry.LocationCode, cancellationToken)
                .ConfigureAwait(false);

            lines.AddRange(InventoryAccounts.ForMovement(
                entry.EntryType,
                settledCost,
                accounts,
                $"{entry.EntryType} {entry.ItemNo} at {entry.LocationCode}",
                contraAccountNo,
                branchId));
        }

        if (lines.Count == 0)
        {
            return;
        }

        await events
            .PublishAsync(
                new LedgerPostingRequested
                {
                    SourceModule = InventoryModule.Id,
                    SourceCode = sourceCode,
                    PostingDate = postingDate,
                    DocumentNo = documentNo,
                    SourceTransactionNo = transactionNo,
                    Lines = lines,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the posting accounts for an item from its category.
    /// </summary>
    /// <remarks>
    /// An item with no category, or a category with no accounts set, produces no ledger lines at
    /// all rather than an error. Stock still moves and is still valued; the value simply waits for
    /// someone to say where it belongs. Refusing the movement instead would stop a shop trading
    /// over a setup step nobody has reached yet.
    /// </remarks>
    private async Task<InventoryAccounts.CategoryAccounts> AccountsForAsync(
        Guid itemId,
        CancellationToken cancellationToken)
        => await context.Set<Item>()
            .AsNoTracking()
            .Where(i => i.Id == itemId && i.Category != null)
            .Select(static i => new InventoryAccounts.CategoryAccounts(
                i.Category!.InventoryAccountNo,
                i.Category.CostOfGoodsSoldAccountNo,
                i.Category.VarianceAccountNo))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}
