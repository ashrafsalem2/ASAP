using ASAP.Modules.Inventory.Events;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Inventory.Costing;

/// <summary>What a settlement run corrected.</summary>
/// <param name="ItemsExamined">How many items were looked at.</param>
/// <param name="ApplicationsSettled">How many outstanding issues found a receipt.</param>
/// <param name="TotalCorrection">The net correction posted.</param>
public readonly record struct SettlementReceipt(
    int ItemsExamined,
    int ApplicationsSettled,
    decimal TotalCorrection);

/// <summary>
/// Settles the cost of stock that was sold before it arrived.
/// </summary>
/// <remarks>
/// <para>
/// The second half of allowing negative stock, and the half that is usually missing. Selling from
/// stock that is not there is only safe if the guess is corrected afterwards; without this the
/// books carry whatever the guess happened to be, for ever, and no report can say which figures
/// are real.
/// </para>
/// <para>
/// It runs after every receipt by default. On a very busy installation it can be turned into a
/// scheduled job instead, but it has to run: until it does, the cost of those sales is an
/// estimate, the margin on them is fiction, and the inventory account disagrees with the valuation
/// by exactly the amount nobody has confirmed.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="events">Announces what was settled.</param>
/// <param name="messages">Renders the confirmation.</param>
/// <param name="transactionNumbers">Issues the number grouping the correction entries.</param>
/// <param name="clock">Supplies the time.</param>
/// <param name="logger">Records settlements.</param>
public sealed partial class CostSettlementService(
    AsapDbContext context,
    IEventPublisher events,
    IMessageCatalog messages,
    ITransactionNumberAllocator transactionNumbers,
    IClock clock,
    ILogger<CostSettlementService> logger)
{
    /// <summary>
    /// Settles everything outstanding for one item, or for every item when none is named.
    /// </summary>
    /// <param name="itemNo">The item to settle, or null for all of them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was corrected, with a message for each settlement.</returns>
    public async Task<Result<SettlementReceipt>> SettleAsync(
        string? itemNo = null,
        CancellationToken cancellationToken = default)
    {
        var outstanding = await LoadOutstandingAsync(itemNo, cancellationToken).ConfigureAwait(false);

        if (outstanding.Count == 0)
        {
            return Result<SettlementReceipt>.Success(new SettlementReceipt(0, 0, 0m));
        }

        var confirmations = new List<AsapMessage>();
        var corrections = new List<(Guid ItemId, string ItemNo, decimal Correction)>();
        var settled = 0;
        var correction = 0m;
        long? transactionNo = null;

        foreach (var group in outstanding.GroupBy(static a => a.ItemId))
        {
            var item = await context.Set<Item>()
                .FirstAsync(i => i.Id == group.Key, cancellationToken)
                .ConfigureAwait(false);

            foreach (var application in group.OrderBy(static a => a.PostingDate).ThenBy(static a => a.Id))
            {
                var layer = await NextAvailableLayerAsync(item.Id, application.VariantId, application, cancellationToken)
                    .ConfigureAwait(false);

                // Still nothing to settle against. The goods have not arrived yet, so the estimate
                // stands and this application waits for the next run.
                if (layer is null)
                {
                    continue;
                }

                transactionNo ??= await transactionNumbers.NextAsync(cancellationToken).ConfigureAwait(false);

                var result = await SettleOneAsync(
                        item,
                        application,
                        layer,
                        transactionNo.Value,
                        cancellationToken)
                    .ConfigureAwait(false);

                settled++;
                correction += result.Correction;

                // The estimate was held back from the ledger until now, so what posts here is the
                // whole true cost: the figure released plus the correction to it.
                if (result.Correction != 0m || result.ReleasedEstimate != 0m)
                {
                    corrections.Add((item.Id, item.No, result.Correction + result.ReleasedEstimate));
                }

                if (result.Message is { } message)
                {
                    confirmations.Add(message);
                }
            }
        }

        if (settled == 0)
        {
            return Result<SettlementReceipt>.Success(new SettlementReceipt(
                outstanding.Select(static a => a.ItemId).Distinct().Count(),
                0,
                0m));
        }

        await RequestLedgerCorrectionAsync(corrections, transactionNo!.Value, cancellationToken)
            .ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Settled {Count} outstanding cost application(s), net correction {Correction}.",
            settled,
            correction);

        return Result<SettlementReceipt>.Success(
            new SettlementReceipt(
                outstanding.Select(static a => a.ItemId).Distinct().Count(),
                settled,
                correction),
            confirmations);
    }

    /// <summary>
    /// Matches one outstanding issue to a receipt and posts the difference.
    /// </summary>
    private async Task<(decimal Correction, decimal ReleasedEstimate, AsapMessage? Message)> SettleOneAsync(
        Item item,
        ItemApplicationEntry application,
        ItemLedgerEntry layer,
        long transactionNo,
        CancellationToken cancellationToken)
    {
        var quantity = Math.Min(application.Quantity, layer.RemainingQuantity);
        var actualUnitCost = await UnitCostOfAsync(layer.Id, cancellationToken).ConfigureAwait(false);

        var estimates = await context.Set<ValueEntry>()
            .Where(v => v.ItemLedgerEntryId == application.OutboundEntryId && v.IsExpected)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var estimatedUnitCost = estimates.Count > 0 ? estimates[0].UnitCost : item.UnitCost;

        var correction = CostingEngine.SettleEstimate(estimatedUnitCost, actualUnitCost, quantity);

        layer.RemainingQuantity -= quantity;
        application.InboundEntryId = layer.Id;
        application.Quantity = quantity;
        application.IsOutstanding = false;

        // The estimate stops being an estimate. Its value entry is not rewritten -- the figure it
        // carries is what was booked at the time and stays on the record -- but it is no longer
        // held back from the general ledger.
        foreach (var estimate in estimates)
        {
            estimate.IsExpected = false;
        }

        var outbound = await context.Set<ItemLedgerEntry>()
            .FirstAsync(e => e.Id == application.OutboundEntryId, cancellationToken)
            .ConfigureAwait(false);

        outbound.IsApplied = true;

        // A correction of nothing is not posted. An estimate that happened to be right should
        // leave no trace beyond the application it settled; writing a zero-value entry for every
        // accurate guess fills the ledger with rows that say nothing.
        if (correction != 0m)
        {
            context.Set<ValueEntry>().Add(new ValueEntry
            {
                TenantId = outbound.TenantId,
                CompanyId = outbound.CompanyId,
                ItemLedgerEntryId = outbound.Id,
                ItemId = item.Id,
                ItemNo = item.No,
                EntryType = ValueEntryType.Revaluation,
                ItemLedgerEntryType = outbound.EntryType,
                PostingDate = clock.Today,
                Quantity = 0,
                CostAmount = correction,
                UnitCost = actualUnitCost,
                IsExpected = false,
                DocumentNo = outbound.DocumentNo,
                TransactionNo = transactionNo,
                SourceCode = "COSTADJ",
                BranchId = outbound.BranchId,
            });

            events.Enqueue(new StockCostSettled
            {
                OccurredAtUtc = clock.UtcNow,
                TransactionNo = transactionNo,
                ItemNo = item.No,
                Quantity = quantity,
                CostCorrection = correction,
            });
        }

        var message = correction == 0m
            ? null
            : messages.Render(
                InventoryMessages.CostSettled,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Quantity"] = quantity,
                    ["ItemNo"] = item.No,
                    ["EstimatedUnitCost"] = estimatedUnitCost,
                    ["ActualUnitCost"] = actualUnitCost,
                    ["Difference"] = correction,
                });

        return (correction, estimates.Sum(static e => e.CostAmount), message);
    }

    private async Task<List<ItemApplicationEntry>> LoadOutstandingAsync(
        string? itemNo,
        CancellationToken cancellationToken)
    {
        var query = context.Set<ItemApplicationEntry>().Where(a => a.IsOutstanding);

        if (!string.IsNullOrWhiteSpace(itemNo))
        {
            var itemId = await context.Set<Item>()
                .Where(i => i.No == itemNo)
                .Select(static i => i.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            query = query.Where(a => a.ItemId == itemId);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the oldest receipt with stock left that arrived on or after the issue it will cover.
    /// </summary>
    /// <remarks>
    /// A receipt dated before the issue would have been consumed at the time, so it cannot be what
    /// covered it. Restricting to receipts on or after the issue date is what stops a settlement
    /// from quietly reaching backwards and taking stock that some earlier sale already used.
    /// </remarks>
    private Task<ItemLedgerEntry?> NextAvailableLayerAsync(
        Guid itemId,
        Guid? variantId,
        ItemApplicationEntry application,
        CancellationToken cancellationToken)
        => context.Set<ItemLedgerEntry>()
            .Where(e => e.ItemId == itemId

                        // A blue shortfall is settled by a blue receipt. Without this a red
                        // arrival would quietly pay for a blue sale and both costs would be wrong.
                        && e.VariantId == variantId
                        && e.RemainingQuantity > 0
                        && e.Quantity > 0
                        && e.PostingDate >= application.PostingDate)
            .OrderBy(e => e.PostingDate)
            .ThenBy(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<decimal> UnitCostOfAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var totals = await context.Set<ValueEntry>()
            .Where(v => v.ItemLedgerEntryId == entryId)
            .GroupBy(static v => v.ItemLedgerEntryId)
            .Select(static g => new
            {
                Cost = g.Sum(static v => v.CostAmount),
                Quantity = g.Sum(static v => v.Quantity),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return totals is null || totals.Quantity == 0
            ? 0m
            : Math.Round(totals.Cost / totals.Quantity, 5, MidpointRounding.AwayFromZero);
    }
}
