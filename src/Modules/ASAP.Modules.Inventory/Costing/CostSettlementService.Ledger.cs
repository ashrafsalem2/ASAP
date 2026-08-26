using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Kernel.Accounting;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Costing;

/// <summary>The half of settlement that asks for the corrected cost to reach a general ledger.</summary>
public sealed partial class CostSettlementService
{
    /// <summary>
    /// Asks the ledger to book the cost of sales that were estimated until now.
    /// </summary>
    /// <param name="corrections">Each item with the amount to post: the released estimate plus its correction.</param>
    /// <param name="transactionNo">The transaction the settlement entries belong to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <remarks>
    /// <para>
    /// What posts here is the whole true cost of those units, not only the difference. The
    /// estimate was deliberately withheld from the ledger while it was a guess, so this is the
    /// first time any of it reaches an account -- and posting only the correction would leave the
    /// inventory account short by the estimate for ever.
    /// </para>
    /// <para>
    /// It goes through the same kernel event as an ordinary movement, so an installation without
    /// Finance settles its costs and simply has nowhere to post them, exactly as before.
    /// </para>
    /// </remarks>
    private async Task RequestLedgerCorrectionAsync(
        IReadOnlyList<(Guid ItemId, string ItemNo, decimal Amount)> corrections,
        long transactionNo,
        CancellationToken cancellationToken)
    {
        if (corrections.Count == 0)
        {
            return;
        }

        var lines = new List<LedgerPostingLine>();

        foreach (var (itemId, itemNo, amount) in corrections)
        {
            if (amount == 0m)
            {
                continue;
            }

            var accounts = await AccountsForAsync(itemId, cancellationToken).ConfigureAwait(false);

            // Treated as a sale, because that is what it is: goods left stock and became a cost.
            // The only thing that was ever in doubt is how much they cost.
            lines.AddRange(InventoryAccounts.ForMovement(
                ItemLedgerEntryType.Sale,
                amount,
                accounts,
                $"Cost settled for {itemNo}"));
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
                    SourceCode = "COSTADJ",
                    PostingDate = clock.Today,
                    SourceTransactionNo = transactionNo,
                    Lines = lines,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

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
