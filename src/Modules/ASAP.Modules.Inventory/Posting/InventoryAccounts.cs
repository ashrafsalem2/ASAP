using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Kernel.Accounting;

namespace ASAP.Modules.Inventory.Posting;

/// <summary>
/// Works out which accounts a stock movement affects, and by how much.
/// </summary>
/// <remarks>
/// <para>
/// Pure logic, so the rules can be checked without a ledger or a database. The rules themselves
/// are the ordinary double entry of stock keeping, and each one is short enough to state:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Goods arriving are worth something, so inventory goes up and whatever brought them in goes
///     down. Where that credit lands depends on why they arrived.
///   </description></item>
///   <item><description>
///     Goods sold stop being an asset and become a cost, so inventory goes down and cost of goods
///     sold goes up by the same figure.
///   </description></item>
///   <item><description>
///     A cost settled later moves the same two accounts by the difference alone, because the
///     original figure is already booked.
///   </description></item>
/// </list>
/// <para>
/// Accounts come from the item's category rather than from the item, so a company with twelve
/// thousand items maintains six sets of accounts rather than twelve thousand.
/// </para>
/// </remarks>
public static class InventoryAccounts
{
    /// <summary>The accounts a category posts to.</summary>
    /// <param name="InventoryAccountNo">Where the value of stock is held.</param>
    /// <param name="CostOfGoodsSoldAccountNo">Where the cost of what was sold is charged.</param>
    /// <param name="VarianceAccountNo">
    /// Where an adjustment lands: a stock count difference, a write-off, or the correction posted
    /// when an estimate is settled.
    /// </param>
    public readonly record struct CategoryAccounts(
        string? InventoryAccountNo,
        string? CostOfGoodsSoldAccountNo,
        string? VarianceAccountNo);

    /// <summary>
    /// Builds the ledger lines for one stock movement.
    /// </summary>
    /// <param name="entryType">What caused the movement.</param>
    /// <param name="costAmount">
    /// The change in the value of stock. Positive when goods arrive, negative when they leave.
    /// </param>
    /// <param name="accounts">The accounts the item's category posts to.</param>
    /// <param name="description">What the entries should say.</param>
    /// <returns>
    /// A balanced pair of lines, or none at all when the movement is worth nothing or the category
    /// has not been given the accounts it needs.
    /// </returns>
    public static IReadOnlyList<LedgerPostingLine> ForMovement(
        ItemLedgerEntryType entryType,
        decimal costAmount,
        CategoryAccounts accounts,
        string description)
    {
        // A movement worth nothing produces no entries. A zero-value posting balances perfectly
        // and tells a reader nothing, which is a poor trade for two more rows on every account.
        if (costAmount == 0m)
        {
            return [];
        }

        if (accounts.InventoryAccountNo is not { Length: > 0 } inventory)
        {
            return [];
        }

        var contra = ContraAccountFor(entryType, accounts);

        if (contra is not { Length: > 0 })
        {
            return [];
        }

        // Inventory takes the value, the other side takes its opposite, and the two sum to zero.
        // The ledger refuses anything that does not, so getting this wrong is caught rather than
        // absorbed.
        return
        [
            new LedgerPostingLine(inventory, costAmount, description),
            new LedgerPostingLine(contra, -costAmount, description),
        ];
    }

    /// <summary>
    /// Builds the lines for a cost settled after the fact.
    /// </summary>
    /// <param name="correction">
    /// The difference between what was estimated and what the goods really cost, signed so that
    /// adding it to what is already booked leaves the truth.
    /// </param>
    /// <param name="accounts">The accounts the item's category posts to.</param>
    /// <param name="description">What the entries should say.</param>
    /// <remarks>
    /// The correction moves inventory against cost of goods sold, not against the variance
    /// account. The goods were sold; the only thing that was wrong is how much they cost, and the
    /// figure that needs fixing is the cost of the sale.
    /// </remarks>
    public static IReadOnlyList<LedgerPostingLine> ForSettlement(
        decimal correction,
        CategoryAccounts accounts,
        string description)
    {
        if (correction == 0m
            || accounts.InventoryAccountNo is not { Length: > 0 } inventory
            || accounts.CostOfGoodsSoldAccountNo is not { Length: > 0 } cogs)
        {
            return [];
        }

        return
        [
            new LedgerPostingLine(inventory, correction, description),
            new LedgerPostingLine(cogs, -correction, description),
        ];
    }

    /// <summary>
    /// Decides what inventory posts against, which depends on why the stock moved.
    /// </summary>
    private static string? ContraAccountFor(ItemLedgerEntryType entryType, CategoryAccounts accounts)
        => entryType switch
        {
            // Goods sold or returned move between stock and the cost of what was sold.
            ItemLedgerEntryType.Sale or ItemLedgerEntryType.SalesReturn
                => accounts.CostOfGoodsSoldAccountNo,

            // A purchase and its return are settled against the vendor by Purchasing, which owns
            // that side of the transaction. Until that module exists the variance account holds
            // the balance, so the books stay square rather than waiting for a module to arrive.
            ItemLedgerEntryType.Purchase or ItemLedgerEntryType.PurchaseReturn
                => accounts.VarianceAccountNo,

            // A count difference or a write-off has no counterparty at all. Something simply is
            // not there, and the loss belongs in variance.
            ItemLedgerEntryType.PositiveAdjustment or ItemLedgerEntryType.NegativeAdjustment
                => accounts.VarianceAccountNo,

            // A transfer moves goods between locations without changing what the company owns, so
            // the two halves cancel and neither needs a contra account of its own.
            ItemLedgerEntryType.TransferOut or ItemLedgerEntryType.TransferIn
                => accounts.InventoryAccountNo,

            _ => accounts.VarianceAccountNo,
        };
}
