using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Kernel.Accounting;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Purchasing.Costing;

/// <summary>How a charge is spread across the goods it covered.</summary>
public enum LandedCostBasis
{
    /// <summary>
    /// By what each line was worth. The ordinary choice: an insurance premium or a customs duty
    /// follows value, and the expensive thing in the container carries more of it.
    /// </summary>
    ByValue = 0,

    /// <summary>
    /// By how many. Right where the charge follows bulk rather than worth -- a pallet fee does not
    /// care that one pallet holds jewellery and the next holds sand.
    /// </summary>
    ByQuantity = 1,
}

/// <summary>What a landed cost did to one receipt.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Share">What this receipt took of the charge.</param>
/// <param name="PerUnit">What that came to on each unit received.</param>
/// <param name="QuantityReceived">How many arrived on that receipt.</param>
/// <param name="StillOnHand">How many of them have not been sold.</param>
/// <param name="ToInventory">The part that raises the value of stock still held.</param>
/// <param name="ToCostOfSales">The part that corrects the cost of what has already gone.</param>
public readonly record struct LandedCostShare(
    string ItemNo,
    decimal Share,
    decimal PerUnit,
    decimal QuantityReceived,
    decimal StillOnHand,
    decimal ToInventory,
    decimal ToCostOfSales);

/// <summary>What a landed cost posting came to.</summary>
/// <param name="TransactionNo">The number the entries were written under.</param>
/// <param name="Amount">The charge that was spread.</param>
/// <param name="ToInventory">How much of it raised the value of stock still held.</param>
/// <param name="ToCostOfSales">How much of it corrected the cost of goods already sold.</param>
/// <param name="Shares">What each receipt took.</param>
public readonly record struct LandedCostPosted(
    long TransactionNo,
    decimal Amount,
    decimal ToInventory,
    decimal ToCostOfSales,
    IReadOnlyList<LandedCostShare> Shares);

/// <summary>
/// Adds freight, duty and clearance to the cost of the goods they were spent on.
/// </summary>
/// <remarks>
/// <para>
/// A shipping company invoices five thousand for a container. The container held three items at
/// three different prices, and the five thousand is part of what those goods cost. Leaving it in an
/// expense account makes every margin on those items overstated for as long as they sell.
/// </para>
/// <para>
/// The charge is spread across the receipts it covered and applied per unit received, so the cost
/// layers carry it and every subsequent sale is costed with it. That much is the same machinery a
/// revaluation uses.
/// </para>
/// <para>
/// What is different, and what makes this the harder of the two, is that a landed cost is not a
/// decision about future value: it is a correction to what the goods cost all along. Freight that
/// arrives after sixty of a hundred have been sold belongs on all hundred. So the part covering
/// what is still on hand raises inventory, and the part covering what has gone corrects cost of
/// sales -- against the very outbound entries that consumed the receipt, which is why the
/// application entries exist.
/// </para>
/// <para>
/// Putting the whole charge into inventory instead would be the easy version and it would be
/// wrong: the inventory account would carry freight for goods that are not there, and the margin
/// on the sales that already happened would stay overstated for ever.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="events">Carries the value to whichever module owns the general ledger.</param>
/// <param name="clock">Supplies the date.</param>
/// <param name="transactionNumbers">Issues the number that groups the entries.</param>
/// <param name="logger">Records what was applied.</param>
/// <param name="chart">
/// Reads the chart of accounts, where a module owns one. The account a charge posts against comes
/// from whoever is keying it, so it is checked here rather than left for the ledger to reject after
/// the value entries are already written.
/// </param>
public sealed class LandedCostService(
    AsapDbContext context,
    IMessageCatalog messages,
    IEventPublisher events,
    IClock clock,
    ITransactionNumberAllocator transactionNumbers,
    ILogger<LandedCostService> logger,
    IChartOfAccounts? chart = null)
{
    /// <summary>How many decimal places a unit cost carries.</summary>
    private const int UnitCostDecimals = 5;

    /// <summary>
    /// Spreads a charge across the goods received against one order.
    /// </summary>
    /// <param name="orderNo">The order whose receipts the charge covered.</param>
    /// <param name="amount">The charge.</param>
    /// <param name="basis">Whether to spread it by value or by quantity.</param>
    /// <param name="contraAccountNo">What the charge posts against -- the carrier's accrual.</param>
    /// <param name="postingDate">The date to report it in, or null for today.</param>
    /// <param name="description">What it was for, which goes on the ledger lines.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What it did, or why it did nothing.</returns>
    public async Task<Result<LandedCostPosted>> ApplyAsync(
        string orderNo,
        decimal amount,
        LandedCostBasis basis,
        string contraAccountNo,
        DateOnly? postingDate = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return Result<LandedCostPosted>.Failure(messages.Render(
                PurchasingMessages.LandedCostNotPositive,
                Args(("Amount", amount))));
        }

        if (string.IsNullOrWhiteSpace(contraAccountNo))
        {
            return Result<LandedCostPosted>.Failure(messages.Render(
                PurchasingMessages.LandedCostNeedsAnAccount,
                Args(("OrderNo", orderNo))));
        }

        // Before anything is written. Half a landed cost is worse than none: the cost layers
        // would carry the charge and the accounts would not, and nothing would say so.
        var unusable = await AccountRefusalAsync(orderNo, contraAccountNo, cancellationToken)
            .ConfigureAwait(false);

        if (unusable is not null)
        {
            return Result<LandedCostPosted>.Failure(unusable);
        }

        var receipts = await context.Set<ItemLedgerEntry>()
            .Where(e => e.DocumentNo == orderNo && e.SourceCode == "PURCH" && e.Quantity > 0)
            .OrderBy(e => e.PostingDate)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (receipts.Count == 0)
        {
            return Result<LandedCostPosted>.Failure(messages.Render(
                PurchasingMessages.NothingReceivedToLandCostOn,
                Args(("OrderNo", orderNo))));
        }

        var values = await ReceiptValuesAsync(receipts, cancellationToken).ConfigureAwait(false);

        var weights = receipts.ToDictionary(
            static e => e.Id,
            e => basis is LandedCostBasis.ByQuantity ? e.Quantity : Math.Abs(values.GetValueOrDefault(e.Id)));

        var totalWeight = weights.Values.Sum();

        if (totalWeight <= 0m)
        {
            // Nothing to apportion by. Spreading a charge evenly instead would be inventing a
            // basis nobody chose, and the wrong item would carry it.
            return Result<LandedCostPosted>.Failure(messages.Render(
                PurchasingMessages.NothingToApportionBy,
                Args(("OrderNo", orderNo), ("Basis", basis is LandedCostBasis.ByQuantity ? "quantity" : "value"))));
        }

        var date = postingDate ?? clock.Today;
        var transactionNo = await transactionNumbers.NextAsync(cancellationToken).ConfigureAwait(false);

        var shares = new List<LandedCostShare>();
        var spread = 0m;
        var toInventory = 0m;
        var toCostOfSales = 0m;

        for (var index = 0; index < receipts.Count; index++)
        {
            var receipt = receipts[index];

            // The last one carries the rounding, so the shares add up to the charge rather than to
            // within a halala of it.
            var share = index == receipts.Count - 1
                ? amount - spread
                : Math.Round(amount * weights[receipt.Id] / totalWeight, 2, MidpointRounding.AwayFromZero);

            spread += share;

            var perUnit = Math.Round(share / receipt.Quantity, UnitCostDecimals, MidpointRounding.AwayFromZero);
            var onHand = receipt.RemainingQuantity;
            var sold = receipt.Quantity - onHand;

            var inventoryPart = Math.Round(perUnit * onHand, 2, MidpointRounding.AwayFromZero);
            var soldPart = share - inventoryPart;

            // The part covering stock still here. A zero-quantity entry, because nothing moved:
            // its UnitCost is what each remaining unit was written up by, and the layer costs the
            // new figure from here on.
            if (inventoryPart != 0m || perUnit != 0m)
            {
                context.Set<ValueEntry>().Add(new ValueEntry
                {
                    TenantId = receipt.TenantId,
                    CompanyId = receipt.CompanyId,
                    ItemLedgerEntryId = receipt.Id,
                    ItemId = receipt.ItemId,
                    ItemNo = receipt.ItemNo,
                    EntryType = ValueEntryType.IndirectCost,
                    ItemLedgerEntryType = receipt.EntryType,
                    PostingDate = date,
                    Quantity = 0m,
                    CostAmount = inventoryPart,
                    UnitCost = perUnit,
                    IsExpected = false,
                    DocumentNo = description ?? orderNo,
                    TransactionNo = transactionNo,
                    SourceCode = "LANDED",
                    BranchId = receipt.BranchId,
                });
            }

            // And the part covering what has already gone, against the outbound entries that
            // consumed this receipt. Their cost of sales was booked without this charge in it.
            if (soldPart != 0m)
            {
                await CorrectSoldAsync(receipt, perUnit, date, transactionNo, description, cancellationToken)
                    .ConfigureAwait(false);
            }

            toInventory += inventoryPart;
            toCostOfSales += soldPart;

            shares.Add(new LandedCostShare(
                receipt.ItemNo,
                share,
                perUnit,
                receipt.Quantity,
                onHand,
                inventoryPart,
                soldPart));
        }

        await PostToLedgerAsync(
                receipts,
                toInventory,
                toCostOfSales,
                contraAccountNo,
                date,
                transactionNo,
                description ?? $"Landed cost {orderNo}",
                cancellationToken)
            .ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Landed {Amount} on {OrderNo} across {Count} receipt(s): {Inventory} to inventory, "
            + "{Cogs} to cost of sales, as transaction {TransactionNo}.",
            amount,
            orderNo,
            receipts.Count,
            toInventory,
            toCostOfSales,
            transactionNo);

        return Result<LandedCostPosted>.Success(
            new LandedCostPosted(transactionNo, amount, toInventory, toCostOfSales, shares),
            toCostOfSales != 0m
                ? [messages.Render(
                    PurchasingMessages.LandedCostReachedGoodsAlreadySold,
                    Args(("OrderNo", orderNo), ("Amount", toCostOfSales)))]
                : []);
    }

    /// <summary>
    /// Corrects the cost of what was already sold out of one receipt.
    /// </summary>
    /// <remarks>
    /// One entry per application, which is the record of which sale took how much of which
    /// receipt. Correcting the sale rather than the stock is the whole point: those goods are
    /// gone, and what is wrong is the figure booked against the revenue they earned.
    /// </remarks>
    private async Task CorrectSoldAsync(
        ItemLedgerEntry receipt,
        decimal perUnit,
        DateOnly date,
        long transactionNo,
        string? description,
        CancellationToken cancellationToken)
    {
        var applications = await context.Set<ItemApplicationEntry>()
            .Where(a => a.InboundEntryId == receipt.Id && a.Quantity > 0)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var application in applications)
        {
            var correction = Math.Round(perUnit * application.Quantity, 2, MidpointRounding.AwayFromZero);

            if (correction == 0m)
            {
                continue;
            }

            var outbound = await context.Set<ItemLedgerEntry>()
                .FirstOrDefaultAsync(e => e.Id == application.OutboundEntryId, cancellationToken)
                .ConfigureAwait(false);

            if (outbound is null)
            {
                continue;
            }

            context.Set<ValueEntry>().Add(new ValueEntry
            {
                TenantId = outbound.TenantId,
                CompanyId = outbound.CompanyId,
                ItemLedgerEntryId = outbound.Id,
                ItemId = outbound.ItemId,
                ItemNo = outbound.ItemNo,
                EntryType = ValueEntryType.Revaluation,
                ItemLedgerEntryType = outbound.EntryType,
                PostingDate = date,

                // No quantity: the sale itself is unchanged, only what it cost.
                Quantity = 0m,
                CostAmount = -correction,
                UnitCost = perUnit,
                IsExpected = false,
                DocumentNo = description,
                TransactionNo = transactionNo,
                SourceCode = "LANDED",
                BranchId = outbound.BranchId,
            });
        }
    }

    /// <summary>What each receipt was worth, so a value basis has something to divide by.</summary>
    private async Task<Dictionary<Guid, decimal>> ReceiptValuesAsync(
        List<ItemLedgerEntry> receipts,
        CancellationToken cancellationToken)
    {
        var ids = receipts.Select(static e => e.Id).ToList();

        var totals = await context.Set<ValueEntry>()
            .Where(v => ids.Contains(v.ItemLedgerEntryId))
            .GroupBy(static v => v.ItemLedgerEntryId)
            .Select(static g => new { EntryId = g.Key, Cost = g.Sum(static v => v.CostAmount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return totals.ToDictionary(static t => t.EntryId, static t => t.Cost);
    }

    /// <summary>
    /// Asks whichever module owns the ledger to post the charge.
    /// </summary>
    /// <remarks>
    /// Three lines rather than two, because the charge lands in two places: what is still on the
    /// shelf raises inventory, and what has been sold raises cost of sales. Collapsing them would
    /// put freight for goods that are gone into the inventory account, where nothing can ever take
    /// it out again.
    /// </remarks>
    private async Task PostToLedgerAsync(
        List<ItemLedgerEntry> receipts,
        decimal toInventory,
        decimal toCostOfSales,
        string contraAccountNo,
        DateOnly date,
        long transactionNo,
        string description,
        CancellationToken cancellationToken)
    {
        var accounts = await context.Set<Item>()
            .AsNoTracking()
            .Where(i => receipts.Select(r => r.ItemId).Contains(i.Id) && i.Category != null)
            .Select(static i => new
            {
                Inventory = i.Category!.InventoryAccountNo,
                Cogs = i.Category.CostOfGoodsSoldAccountNo,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (accounts?.Inventory is not { Length: > 0 } inventoryAccount)
        {
            return;
        }

        var lines = new List<LedgerPostingLine>();

        if (toInventory != 0m)
        {
            lines.Add(new LedgerPostingLine(inventoryAccount, toInventory, description));
        }

        if (toCostOfSales != 0m && accounts.Cogs is { Length: > 0 } cogsAccount)
        {
            lines.Add(new LedgerPostingLine(cogsAccount, toCostOfSales, description));
        }

        if (lines.Count == 0)
        {
            return;
        }

        lines.Add(new LedgerPostingLine(contraAccountNo, -lines.Sum(static l => l.Amount), description));

        await events
            .PublishAsync(
                new LedgerPostingRequested
                {
                    SourceModule = PurchasingModule.Id,
                    SourceCode = "LANDED",
                    PostingDate = date,
                    DocumentNo = description,
                    SourceTransactionNo = transactionNo,
                    Lines = lines,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Says why the charge's account will not take an entry, when it will not.</summary>
    /// <remarks>
    /// Nothing is checked where no module owns a chart of accounts, for the same reason nothing
    /// else is: running without a general ledger is a supported way to run.
    /// </remarks>
    private async Task<AsapMessage?> AccountRefusalAsync(
        string orderNo,
        string accountNo,
        CancellationToken cancellationToken)
    {
        if (chart is null)
        {
            return null;
        }

        var described = await chart.DescribeAsync(accountNo, cancellationToken).ConfigureAwait(false);

        if (described is { Postability: AccountPostability.Postable })
        {
            return null;
        }

        var reason = described is null
            ? "the chart of accounts has no such number"
            : described.Value.Postability is AccountPostability.Blocked
                ? "it is blocked"
                : "it is a heading or a total";

        return messages.Render(
            PurchasingMessages.LandedCostAccountUnusable,
            Args(("OrderNo", orderNo), ("AccountNo", accountNo), ("Reason", reason)));
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
