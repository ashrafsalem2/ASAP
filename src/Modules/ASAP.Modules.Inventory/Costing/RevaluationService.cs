using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Kernel.Accounting;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Inventory.Costing;

/// <summary>What stock is worth now, before anybody changes it.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is called.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="LocationCode">Where.</param>
/// <param name="Quantity">How much is on hand.</param>
/// <param name="UnitCost">What one costs now.</param>
/// <param name="Value">What the lot is worth now.</param>
public readonly record struct StockValuation(
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    string LocationCode,
    decimal Quantity,
    decimal UnitCost,
    decimal Value);

/// <summary>What a revaluation did.</summary>
/// <param name="TransactionNo">The number the entries were written under.</param>
/// <param name="Quantity">How much stock was revalued.</param>
/// <param name="OldUnitCost">What it cost before, averaged over what is on hand.</param>
/// <param name="NewUnitCost">What it costs now.</param>
/// <param name="ValueChange">What the stock is worth now, less what it was worth before.</param>
/// <param name="LayerCount">How many open receipts were touched.</param>
public readonly record struct RevaluationPosted(
    long TransactionNo,
    decimal Quantity,
    decimal OldUnitCost,
    decimal NewUnitCost,
    decimal ValueChange,
    int LayerCount);

/// <summary>
/// Changes what stock is worth without changing how much of it there is.
/// </summary>
/// <remarks>
/// <para>
/// The other half of an adjustment. An adjustment says there are fewer than the system thought; a
/// revaluation says there are exactly as many as the system thought and they are worth less. Damp
/// stock, a supplier credit after the fact, a line nobody will pay full price for again -- none of
/// those are quantity problems, and writing them off as breakage would lose that distinction and
/// make a shrinkage report say something untrue.
/// </para>
/// <para>
/// The difficult part is making it stick. A revaluation posted as a lump sum against nothing in
/// particular leaves the cost layers carrying their old figures, so the next sale is costed at the
/// price the goods were bought at and the write-down quietly reverses itself as the stock sells.
/// Nothing errors; the inventory account simply drifts back. So this writes against the open
/// receipts themselves, as an amount per remaining unit, and the layer costs the new figure from
/// here on.
/// </para>
/// <para>
/// Only what is on hand is touched. Goods already sold were costed at what they were worth at the
/// time, and their cost of sales is already booked against the revenue they earned; reaching back
/// into a closed month to restate them is a different operation with different permissions.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="events">Carries the value to whichever module owns the general ledger.</param>
/// <param name="branches">Says which branch a location belongs to.</param>
/// <param name="transactionNumbers">Issues the number that groups the entries.</param>
/// <param name="logger">Records revaluations.</param>
public sealed class RevaluationService(
    AsapDbContext context,
    IMessageCatalog messages,
    IEventPublisher events,
    LocationBranchLookup branches,
    ITransactionNumberAllocator transactionNumbers,
    ILogger<RevaluationService> logger)
{
    /// <summary>How many decimal places a unit cost carries.</summary>
    private const int UnitCostDecimals = 5;

    /// <summary>
    /// What one item is worth at one location right now.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="locationCode">The location.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The valuation, or why it could not be read.</returns>
    public async Task<Result<StockValuation>> ValuationAsync(
        string itemNo,
        string locationCode,
        CancellationToken cancellationToken = default)
    {
        var found = await FindAsync(itemNo, locationCode, cancellationToken).ConfigureAwait(false);

        if (found.Refusal is { } refusal)
        {
            return Result<StockValuation>.Failure(refusal);
        }

        var (item, location) = (found.Item!, found.Location!);
        var layers = await OpenLayersAsync(item.Id, location.Id, cancellationToken).ConfigureAwait(false);

        var quantity = layers.Sum(static l => l.Remaining);
        var value = layers.Sum(static l => l.Remaining * l.UnitCost);

        return Result<StockValuation>.Success(new StockValuation(
            item.No,
            item.Description,
            item.DescriptionArabic,
            location.Code,
            quantity,
            quantity == 0m ? 0m : Math.Round(value / quantity, UnitCostDecimals, MidpointRounding.AwayFromZero),
            value));
    }

    /// <summary>
    /// Writes stock up or down to a new cost per unit.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="locationCode">The location.</param>
    /// <param name="newUnitCost">What one should cost from now on.</param>
    /// <param name="postingDate">The date to report it in.</param>
    /// <param name="reason">Why, which goes on the entries and the ledger description.</param>
    /// <param name="contraAccountNo">Where the loss or gain lands, or null for the category's own.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What it did, or why it did nothing.</returns>
    public async Task<Result<RevaluationPosted>> RevalueAsync(
        string itemNo,
        string locationCode,
        decimal newUnitCost,
        DateOnly postingDate,
        string? reason = null,
        string? contraAccountNo = null,
        CancellationToken cancellationToken = default)
    {
        var found = await FindAsync(itemNo, locationCode, cancellationToken).ConfigureAwait(false);

        if (found.Refusal is { } refusal)
        {
            return Result<RevaluationPosted>.Failure(refusal);
        }

        var (item, location) = (found.Item!, found.Location!);

        if (newUnitCost < 0m)
        {
            // Stock worth less than nothing is not a valuation, it is a liability, and it belongs
            // in a provision somebody can see rather than hidden inside an inventory balance.
            return Result<RevaluationPosted>.Failure(messages.Render(
                InventoryMessages.RevaluationCostNegative,
                Args(("ItemNo", item.No), ("UnitCost", newUnitCost))));
        }

        var layers = await OpenLayersAsync(item.Id, location.Id, cancellationToken).ConfigureAwait(false);
        var quantity = layers.Sum(static l => l.Remaining);

        if (quantity <= 0m)
        {
            // Nothing to write down. Refused rather than posted as a lump, because a value with no
            // quantity under it has no layer to attach to and would sit in the inventory account
            // as a balance no stock report can explain.
            return Result<RevaluationPosted>.Failure(messages.Render(
                InventoryMessages.NothingToRevalue,
                Args(("ItemNo", item.No), ("Location", location.Code))));
        }

        var oldValue = layers.Sum(static l => l.Remaining * l.UnitCost);
        var oldUnitCost = Math.Round(oldValue / quantity, UnitCostDecimals, MidpointRounding.AwayFromZero);
        var newValue = Math.Round(newUnitCost * quantity, 2, MidpointRounding.AwayFromZero);
        var change = newValue - oldValue;

        if (change == 0m)
        {
            // Not an error. Somebody asked what it was already worth, and the honest answer is
            // that nothing needed doing -- an entry saying so would be a row that means nothing.
            return Result<RevaluationPosted>.Success(
                new RevaluationPosted(0, quantity, oldUnitCost, newUnitCost, 0m, 0),
                [messages.Render(
                    InventoryMessages.RevaluationChangesNothing,
                    Args(("ItemNo", item.No), ("Location", location.Code), ("UnitCost", newUnitCost)))]);
        }

        var transactionNo = await transactionNumbers.NextAsync(cancellationToken).ConfigureAwait(false);

        var branchId = await branches.BranchOfAsync(location.Code, cancellationToken).ConfigureAwait(false);
        var written = 0m;

        for (var index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            var perUnit = newUnitCost - layer.UnitCost;

            // The last layer carries whatever rounding is left over, so the entries add up to the
            // figure that reaches the ledger rather than to within a halala of it.
            var costAmount = index == layers.Count - 1
                ? change - written
                : Math.Round(layer.Remaining * perUnit, 2, MidpointRounding.AwayFromZero);

            written += costAmount;

            if (costAmount == 0m && perUnit == 0m)
            {
                continue;
            }

            context.Set<ValueEntry>().Add(new ValueEntry
            {
                TenantId = layer.Entry.TenantId,
                CompanyId = layer.Entry.CompanyId,
                ItemLedgerEntryId = layer.Entry.Id,
                ItemId = item.Id,
                ItemNo = item.No,
                EntryType = ValueEntryType.Revaluation,
                ItemLedgerEntryType = layer.Entry.EntryType,
                PostingDate = postingDate,

                // No quantity, because nothing moved. That is what marks this as a per-unit
                // adjustment to the layer rather than another receipt into it.
                Quantity = 0m,
                CostAmount = costAmount,
                UnitCost = perUnit,
                IsExpected = false,
                DocumentNo = reason,
                TransactionNo = transactionNo,
                SourceCode = "REVAL",
                BranchId = branchId,
            });
        }

        await PostToLedgerAsync(
                item,
                location,
                change,
                postingDate,
                transactionNo,
                reason,
                contraAccountNo,
                branchId,
                cancellationToken)
            .ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Revalued {Quantity} of {ItemNo} at {Location} from {OldCost} to {NewCost}, "
            + "a change of {Change} as transaction {TransactionNo}.",
            quantity,
            item.No,
            location.Code,
            oldUnitCost,
            newUnitCost,
            change,
            transactionNo);

        return Result<RevaluationPosted>.Success(new RevaluationPosted(
            transactionNo,
            quantity,
            oldUnitCost,
            newUnitCost,
            change,
            layers.Count));
    }

    /// <summary>One open receipt and what it currently costs.</summary>
    private readonly record struct Layer(ItemLedgerEntry Entry, decimal Remaining, decimal UnitCost);

    private async Task<List<Layer>> OpenLayersAsync(
        Guid itemId,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        var open = await context.Set<ItemLedgerEntry>()
            .Where(e => e.ItemId == itemId && e.LocationId == locationId && e.RemainingQuantity > 0)
            .OrderBy(e => e.PostingDate)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (open.Count == 0)
        {
            return [];
        }

        var ids = open.Select(static e => e.Id).ToList();

        // The same two-part sum the costing engine uses: what the goods were bought for, plus
        // whatever previous revaluations moved each remaining unit by.
        var costs = await context.Set<ValueEntry>()
            .Where(v => ids.Contains(v.ItemLedgerEntryId))
            .GroupBy(static v => v.ItemLedgerEntryId)
            .Select(static g => new
            {
                EntryId = g.Key,
                Cost = g.Where(static v => v.Quantity != 0m).Sum(static v => v.CostAmount),
                Quantity = g.Sum(static v => v.Quantity),
                Revalued = g.Where(static v => v.Quantity == 0m).Sum(static v => v.UnitCost),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byId = costs.ToDictionary(
            static c => c.EntryId,
            static c => c.Quantity == 0
                ? Math.Round(c.Revalued, UnitCostDecimals, MidpointRounding.AwayFromZero)
                : Math.Round((c.Cost / c.Quantity) + c.Revalued, UnitCostDecimals, MidpointRounding.AwayFromZero));

        return [.. open.Select(e => new Layer(e, e.RemainingQuantity, byId.GetValueOrDefault(e.Id)))];
    }

    private async Task PostToLedgerAsync(
        Item item,
        Location location,
        decimal change,
        DateOnly postingDate,
        long transactionNo,
        string? reason,
        string? contraAccountNo,
        Guid? branchId,
        CancellationToken cancellationToken)
    {
        var accounts = await context.Set<Item>()
            .AsNoTracking()
            .Where(i => i.Id == item.Id && i.Category != null)
            .Select(static i => new InventoryAccounts.CategoryAccounts(
                i.Category!.InventoryAccountNo,
                i.Category.CostOfGoodsSoldAccountNo,
                i.Category.VarianceAccountNo))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = InventoryAccounts.ForRevaluation(
            change,
            accounts,
            $"Revaluation {item.No} at {location.Code}{(reason is { Length: > 0 } why ? $": {why}" : string.Empty)}",
            contraAccountNo,
            branchId);

        if (lines.Count == 0)
        {
            return;
        }

        await events
            .PublishAsync(
                new LedgerPostingRequested
                {
                    SourceModule = InventoryModule.Id,
                    SourceCode = "REVAL",
                    PostingDate = postingDate,
                    DocumentNo = reason,
                    SourceTransactionNo = transactionNo,
                    Lines = lines,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private readonly record struct Found(Item? Item, Location? Location, AsapMessage? Refusal);

    private async Task<Found> FindAsync(
        string itemNo,
        string locationCode,
        CancellationToken cancellationToken)
    {
        var normalisedItem = itemNo?.Trim().ToUpperInvariant() ?? string.Empty;
        var normalisedLocation = locationCode?.Trim().ToUpperInvariant() ?? string.Empty;

        var item = await context.Set<Item>()
            .FirstOrDefaultAsync(i => i.No == normalisedItem, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return new Found(null, null, messages.Render(
                InventoryMessages.ItemNotFound,
                Args(("ItemNo", normalisedItem))));
        }

        var location = await context.Set<Location>()
            .FirstOrDefaultAsync(l => l.Code == normalisedLocation, cancellationToken)
            .ConfigureAwait(false);

        return location is null
            ? new Found(null, null, messages.Render(
                InventoryMessages.LocationNotFound,
                Args(("Location", normalisedLocation))))
            : new Found(item, location, null);
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
