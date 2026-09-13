using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Locations;

/// <summary>
/// Works out which shelves goods leaving a bin-tracked location come off, where nobody said.
/// </summary>
/// <remarks>
/// <para>
/// Posting refuses an issue at a bin-tracked location that does not name a bin, and it is right
/// to: a shelf picked at random leaves the bins describing stock that is not there. But a sales
/// shipment or a transfer leaving the warehouse has no picker's note to read the bin from, and
/// refusing it outright meant a warehouse with bins could not ship at all.
/// </para>
/// <para>
/// So the documents that ship ask here first. The answer is what a picker would be sent to do:
/// take from the bins that actually hold the item, in the location's pick order, splitting a line
/// across shelves where one does not cover it. Nothing is guessed -- every bin named holds what is
/// taken from it -- and the result is said back on the posting, so the shelf the goods came off is
/// on record and on screen. A line that names its bin is left exactly as it was.
/// </para>
/// <para>
/// Where the bins together hold less than the line takes, the shortfall goes on the last bin
/// picked, and posting judges it under the company's negative-stock rules like any other
/// shortfall. Where no bin holds any, it goes on the receiving bin, where the goods that settle it
/// will arrive; a location with neither is refused by posting as before.
/// </para>
/// </remarks>
/// <param name="context">Reads what each bin holds.</param>
/// <param name="messages">Renders what was picked.</param>
public sealed class BinPicker(AsapDbContext context, IMessageCatalog messages)
{
    /// <summary>
    /// Names a bin on every outbound movement at a bin-tracked location that has none.
    /// </summary>
    /// <param name="movements">The movements about to be posted.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The movements, split by bin where needed, and a note of each pick.</returns>
    public async Task<Result<List<StockMovementRequest>>> PickAsync(
        IReadOnlyList<StockMovementRequest> movements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movements);

        var unbinned = movements
            .Where(static m => m.Quantity < 0m && string.IsNullOrWhiteSpace(m.BinCode))
            .ToList();

        if (unbinned.Count == 0)
        {
            return Result<List<StockMovementRequest>>.Success([.. movements]);
        }

        var locationCodes = unbinned.Select(static m => m.LocationCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var locations = await context.Set<Location>()
            .AsNoTracking()
            .Where(l => l.UsesBins && locationCodes.Contains(l.Code))
            .ToDictionaryAsync(static l => l.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        if (locations.Count == 0)
        {
            return Result<List<StockMovementRequest>>.Success([.. movements]);
        }

        var locationIds = locations.Values.Select(static l => l.Id).ToList();
        var itemNos = unbinned.Select(static m => m.ItemNo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var items = await context.Set<Item>()
            .AsNoTracking()
            .Where(i => itemNos.Contains(i.No))
            .Select(static i => new { i.Id, i.No })
            .ToDictionaryAsync(static i => i.No, static i => i.Id, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var itemIds = items.Values.ToList();

        var bins = await context.Set<Bin>()
            .AsNoTracking()
            .Where(b => locationIds.Contains(b.LocationId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // What each bin holds, by variant and by serial or lot. Summed over every entry at the bin,
        // shelf-to-shelf moves included, which is how the bin figures everywhere else are read.
        var held = (await context.Set<ItemLedgerEntry>()
                .AsNoTracking()
                .Where(e => locationIds.Contains(e.LocationId) && itemIds.Contains(e.ItemId) && e.BinId != null)
                .GroupBy(static e => new { e.BinId, e.ItemId, e.VariantCode, e.SerialNo, e.LotNo })
                .Select(static g => new
                {
                    BinId = g.Key.BinId!.Value,
                    g.Key.ItemId,
                    g.Key.VariantCode,
                    g.Key.SerialNo,
                    g.Key.LotNo,
                    Quantity = g.Sum(static e => e.Quantity),
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(static h => new Holding(h.BinId, h.ItemId, Normal(h.VariantCode), h.SerialNo ?? h.LotNo, h.Quantity))
            .ToList();

        var picked = new List<StockMovementRequest>(movements.Count);
        var notes = new List<AsapMessage>();

        foreach (var movement in movements)
        {
            if (movement.Quantity >= 0m
                || !string.IsNullOrWhiteSpace(movement.BinCode)
                || !locations.TryGetValue(movement.LocationCode, out var location)
                || !items.TryGetValue(movement.ItemNo, out var itemId))
            {
                picked.Add(movement);
                continue;
            }

            var parts = Allocate(movement, location, itemId, bins, held);

            if (parts.Count == 0)
            {
                // Nowhere to put it at all. Posting refuses it and says a bin is needed.
                picked.Add(movement);
                continue;
            }

            picked.AddRange(Split(movement, parts));

            notes.Add(messages.Render(
                InventoryMessages.BinsPicked,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LineNo"] = movement.LineNo,
                    ["ItemNo"] = movement.ItemNo,
                    ["Quantity"] = -movement.Quantity,
                    ["Location"] = location.Code,
                    ["Bins"] = string.Join(", ", parts.Select(static p => $"{p.Bin.Code} ({p.Quantity:0.#####})")),
                },
                MessageTarget.OnField($"Lines[{movement.LineNo}]")));
        }

        return Result<List<StockMovementRequest>>.Success(picked, notes);
    }

    /// <summary>Which bins a movement comes off, and how much from each.</summary>
    private static List<(Bin Bin, decimal Quantity)> Allocate(
        StockMovementRequest movement,
        Location location,
        Guid itemId,
        List<Bin> bins,
        List<Holding> held)
    {
        var variant = Normal(movement.VariantCode);
        var tracking = string.IsNullOrWhiteSpace(movement.TrackingNo) ? null : movement.TrackingNo.Trim().ToUpperInvariant();

        var candidates = held
            .Where(h => h.ItemId == itemId && h.VariantCode == variant)
            .ToList();

        // A named serial or lot comes off the shelf holding it. Shelf-to-shelf moves do not carry
        // the number, so where nothing is recorded for it the item's own bins stand in.
        if (tracking is not null && candidates.Any(h => h.TrackingNo == tracking && h.Quantity > 0m))
        {
            candidates = [.. candidates.Where(h => h.TrackingNo == tracking)];
        }

        var byBin = candidates
            .GroupBy(static h => h.BinId)
            .Select(g => (Bin: bins.Find(b => b.Id == g.Key), Quantity: g.Sum(static h => h.Quantity)))
            .Where(static x => x.Bin is not null && x.Quantity > 0m)
            .OrderBy(static x => x.Bin!.PickOrder)
            .ThenBy(static x => x.Bin!.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var needed = -movement.Quantity;
        var parts = new List<(Bin Bin, decimal Quantity)>();

        foreach (var (bin, quantity) in byBin)
        {
            if (needed <= 0m)
            {
                break;
            }

            var take = Math.Min(quantity, needed);
            parts.Add((bin!, take));
            needed -= take;

            // Taken now, so a later line of the same posting does not pick it a second time.
            foreach (var holding in candidates.Where(h => h.BinId == bin!.Id && h.Quantity > 0m))
            {
                var used = Math.Min(holding.Quantity, take);
                holding.Quantity -= used;
                take -= used;
            }
        }

        if (needed > 0m)
        {
            if (parts.Count > 0)
            {
                var last = parts[^1];
                parts[^1] = (last.Bin, last.Quantity + needed);
            }
            else if (bins.Find(b => b.LocationId == location.Id && b.IsReceiving && !b.IsBlocked) is { } receiving)
            {
                parts.Add((receiving, needed));
            }
        }

        return parts;
    }

    /// <summary>One movement as one per bin, its sales amount shared out without losing a halala.</summary>
    private static IEnumerable<StockMovementRequest> Split(
        StockMovementRequest movement,
        List<(Bin Bin, decimal Quantity)> parts)
    {
        var whole = -movement.Quantity;
        var given = 0m;

        for (var index = 0; index < parts.Count; index++)
        {
            var (bin, quantity) = parts[index];

            var sales = index == parts.Count - 1
                ? movement.SalesAmount - given
                : Math.Round(movement.SalesAmount * quantity / whole, 2, MidpointRounding.AwayFromZero);

            given += sales;

            yield return movement with
            {
                Quantity = -quantity,
                SalesAmount = sales,
                BinCode = bin.Code,
            };
        }
    }

    private static string? Normal(string? variantCode)
        => string.IsNullOrWhiteSpace(variantCode) ? null : variantCode.Trim().ToUpperInvariant();

    /// <summary>What one bin holds of one item, variant and serial or lot, as picking uses it up.</summary>
    private sealed class Holding(Guid binId, Guid itemId, string? variantCode, string? trackingNo, decimal quantity)
    {
        public Guid BinId { get; } = binId;

        public Guid ItemId { get; } = itemId;

        public string? VariantCode { get; } = variantCode;

        public string? TrackingNo { get; } = trackingNo;

        public decimal Quantity { get; set; } = quantity;
    }
}
