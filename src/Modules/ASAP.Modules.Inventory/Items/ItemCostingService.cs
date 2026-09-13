using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Items;

/// <summary>One serial or lot on hand, and what it cost.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="TrackingNo">The serial or lot.</param>
/// <param name="LocationCode">Where it is.</param>
/// <param name="VariantCode">Which variant, on an item that has them.</param>
/// <param name="ReceivedOn">When it arrived.</param>
/// <param name="QuantityOnHand">How much of it is here. Always one for a serial.</param>
/// <param name="UnitCost">What each unit of it cost.</param>
/// <param name="Value">What it is worth in the books.</param>
/// <param name="DocumentNo">The document it arrived on.</param>
public readonly record struct TrackedUnit(
    string ItemNo,
    string TrackingNo,
    string LocationCode,
    string? VariantCode,
    DateOnly ReceivedOn,
    decimal QuantityOnHand,
    decimal UnitCost,
    decimal Value,
    string? DocumentNo);

/// <summary>
/// Decides how an item is costed, and says what each tracked unit on hand is worth.
/// </summary>
/// <remarks>
/// Changing the method is refused once anything has posted, for the reason the method is fixed at
/// all: it decides what every existing value entry meant, and changing it would leave history
/// valued one way and the future another with nothing to reconcile the two. Tracking is locked
/// with it for the same reason — units received without serials cannot acquire them afterwards.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
public sealed class ItemCostingService(AsapDbContext context, IMessageCatalog messages)
{
    /// <summary>Sets how an item is costed and how its units are told apart.</summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="method">The costing method.</param>
    /// <param name="tracking">How units are told apart; required for specific costing, refused otherwise.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The item, or why the change was refused.</returns>
    public async Task<Result<Item>> SetAsync(
        string itemNo,
        CostingMethod method,
        ItemTracking tracking,
        CancellationToken cancellationToken = default)
    {
        var no = itemNo?.Trim().ToUpperInvariant() ?? string.Empty;

        var item = await context.Set<Item>()
            .FirstOrDefaultAsync(i => i.No == no, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Result<Item>.Failure(messages.Render(InventoryMessages.ItemNotFound, Args(("ItemNo", no))));
        }

        var arguments = Args(
            ("ItemNo", item.No),
            ("CurrentMethod", item.CostingMethod.ToString()),
            ("Tracking", tracking.ToString().ToLowerInvariant()));

        if (item.CostingMethod == method && item.Tracking == tracking)
        {
            return Result<Item>.Success(item);
        }

        var posted = item.HasLedgerEntries
            || await context.Set<ItemLedgerEntry>()
                .AnyAsync(e => e.ItemId == item.Id, cancellationToken)
                .ConfigureAwait(false);

        if (posted)
        {
            return Result<Item>.Failure(messages.Render(InventoryMessages.CostingMethodLocked, arguments));
        }

        if (method is CostingMethod.Specific && tracking is ItemTracking.None)
        {
            return Result<Item>.Failure(messages.Render(InventoryMessages.SpecificCostingNeedsTracking, arguments));
        }

        if (method is not CostingMethod.Specific && tracking is not ItemTracking.None)
        {
            arguments["CurrentMethod"] = method.ToString();

            return Result<Item>.Failure(messages.Render(InventoryMessages.TrackingNeedsSpecificCosting, arguments));
        }

        item.CostingMethod = method;
        item.Tracking = tracking;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<Item>.Success(item);
    }

    /// <summary>
    /// Every serial or lot still on hand, and what it cost.
    /// </summary>
    /// <remarks>
    /// Read off the receipts that still have something left, and their value entries, so the cost
    /// shown is the cost the unit will leave at — including anything a later revaluation or a
    /// settled invoice has added to it.
    /// </remarks>
    /// <param name="itemNo">One item, or null for every tracked item.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The units, by item, location and when they arrived.</returns>
    public async Task<IReadOnlyList<TrackedUnit>> OnHandAsync(
        string? itemNo = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.RemainingQuantity > 0m && (e.SerialNo != null || e.LotNo != null));

        if (!string.IsNullOrWhiteSpace(itemNo))
        {
            var no = itemNo.Trim().ToUpperInvariant();
            query = query.Where(e => e.ItemNo == no);
        }

        var layers = await query
            .OrderBy(e => e.ItemNo)
            .ThenBy(e => e.LocationCode)
            .ThenBy(e => e.PostingDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (layers.Count == 0)
        {
            return [];
        }

        var ids = layers.Select(static l => l.Id).ToList();

        var costs = await context.Set<ValueEntry>()
            .AsNoTracking()
            .Where(v => ids.Contains(v.ItemLedgerEntryId))
            .GroupBy(static v => v.ItemLedgerEntryId)
            .Select(static g => new { Id = g.Key, Cost = g.Sum(static v => v.CostAmount) })
            .ToDictionaryAsync(static g => g.Id, static g => g.Cost, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. layers.Select(l =>
            {
                var unitCost = l.Quantity == 0m
                    ? 0m
                    : Math.Round(costs.GetValueOrDefault(l.Id) / l.Quantity, 5, MidpointRounding.AwayFromZero);

                return new TrackedUnit(
                    l.ItemNo,
                    l.SerialNo ?? l.LotNo!,
                    l.LocationCode,
                    l.VariantCode,
                    l.PostingDate,
                    l.RemainingQuantity,
                    unitCost,
                    Math.Round(unitCost * l.RemainingQuantity, 2, MidpointRounding.AwayFromZero),
                    l.DocumentNo);
            }),
        ];
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
