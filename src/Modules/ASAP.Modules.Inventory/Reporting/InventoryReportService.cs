using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Reporting;

/// <summary>What stock was worth at one place on one day.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is.</param>
/// <param name="DescriptionArabic">What it is, in Arabic.</param>
/// <param name="LocationCode">Where.</param>
/// <param name="VariantCode">Which variant, where the item has them.</param>
/// <param name="Quantity">How much was there.</param>
/// <param name="Value">What it was worth.</param>
/// <param name="EstimatedValue">
/// How much of that value is a guess, because it rests on stock that had not arrived.
/// </param>
/// <param name="UnitCost">What one unit works out at. Nothing when the quantity is nought.</param>
public readonly record struct ValuationRow(
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    string LocationCode,
    string? VariantCode,
    decimal Quantity,
    decimal Value,
    decimal EstimatedValue,
    decimal? UnitCost);

/// <summary>How long stock has been sitting, in bands.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is.</param>
/// <param name="DescriptionArabic">What it is, in Arabic.</param>
/// <param name="LocationCode">Where.</param>
/// <param name="VariantCode">Which variant, where the item has them.</param>
/// <param name="Quantity">How much is there in total.</param>
/// <param name="Value">What all of it is worth.</param>
/// <param name="Buckets">What sits in each band, oldest band last.</param>
/// <param name="OldestDays">How long the oldest unit has been there.</param>
public readonly record struct AgeingRow(
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    string LocationCode,
    string? VariantCode,
    decimal Quantity,
    decimal Value,
    IReadOnlyList<AgeingBucket> Buckets,
    int OldestDays);

/// <summary>One age band.</summary>
/// <param name="Label">What the band covers, as days.</param>
/// <param name="FromDays">The first day of the band.</param>
/// <param name="ToDays">The last day of it, or null where the band has no end.</param>
/// <param name="Quantity">How much sits in it.</param>
/// <param name="Value">What that is worth.</param>
public readonly record struct AgeingBucket(
    string Label,
    int FromDays,
    int? ToDays,
    decimal Quantity,
    decimal Value);

/// <summary>How fast an item moves.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is.</param>
/// <param name="DescriptionArabic">What it is, in Arabic.</param>
/// <param name="QuantitySold">How much went out over the period.</param>
/// <param name="CostOfSales">What those goods cost.</param>
/// <param name="QuantityOnHand">What is left.</param>
/// <param name="ValueOnHand">What that is worth.</param>
/// <param name="Turns">
/// How many times the stock turned over in the period, or nothing where there is no stock to
/// divide by.
/// </param>
/// <param name="DaysOfCover">
/// How long what is left would last at the rate it has been going, or nothing where it has not
/// been going at all.
/// </param>
/// <param name="LastMovedOn">The last day anything moved, or nothing where it never has.</param>
public readonly record struct VelocityRow(
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    decimal QuantitySold,
    decimal CostOfSales,
    decimal QuantityOnHand,
    decimal ValueOnHand,
    decimal? Turns,
    decimal? DaysOfCover,
    DateOnly? LastMovedOn);

/// <summary>
/// What the stock is worth, how old it is, and how fast it moves.
/// </summary>
/// <remarks>
/// <para>
/// All three are built from the value entries and the cost layers rather than from the running
/// figures on the item. That is the whole point of them. <c>Item.UnitCost</c> is a convenience
/// kept up to date for the next posting; the ledger is the record, and only the ledger can be
/// asked what something was worth on a date that has passed.
/// </para>
/// <para>
/// The valuation is deliberately the same arithmetic that posts to the inventory account -- the
/// sum of cost amounts up to a date. A valuation worked out any other way is a second opinion, and
/// a second opinion about the inventory account is exactly what nobody wants at a period end.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
public sealed class InventoryReportService(AsapDbContext context)
{
    /// <summary>The default age bands, in days.</summary>
    private static readonly int[] DefaultBands = [30, 60, 90, 180];

    /// <summary>
    /// What the stock was worth on a given day.
    /// </summary>
    /// <remarks>
    /// Every value entry posted on or before the date, grouped by item, variant and location. That
    /// is the same set of rows the inventory account was built from, so the two tie by
    /// construction rather than by agreement.
    /// </remarks>
    /// <param name="asOf">The day to value it on.</param>
    /// <param name="itemNo">One item, or null for all of them.</param>
    /// <param name="locationCode">One location, or null for all of them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per item, variant and location that has anything to say.</returns>
    public async Task<IReadOnlyList<ValuationRow>> ValuationAsync(
        DateOnly asOf,
        string? itemNo = null,
        string? locationCode = null,
        CancellationToken cancellationToken = default)
    {
        var query =
            from value in context.Set<ValueEntry>().AsNoTracking()
            join entry in context.Set<ItemLedgerEntry>().AsNoTracking()
                on value.ItemLedgerEntryId equals entry.Id
            where value.PostingDate <= asOf
            select new { value.ItemNo, entry.LocationCode, entry.VariantCode, value.Quantity, value.CostAmount, value.IsExpected };

        if (!string.IsNullOrWhiteSpace(itemNo))
        {
            query = query.Where(r => r.ItemNo == itemNo);
        }

        if (!string.IsNullOrWhiteSpace(locationCode))
        {
            query = query.Where(r => r.LocationCode == locationCode);
        }

        var grouped = await query
            .GroupBy(static r => new { r.ItemNo, r.LocationCode, r.VariantCode })
            .Select(static g => new
            {
                g.Key.ItemNo,
                g.Key.LocationCode,
                g.Key.VariantCode,
                Quantity = g.Sum(static r => r.Quantity),
                Value = g.Sum(static r => r.CostAmount),
                Estimated = g.Sum(static r => r.IsExpected ? r.CostAmount : 0m),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await DescriptionsAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. grouped
                .Where(static g => g.Quantity != 0m || g.Value != 0m)
                .Select(g => new ValuationRow(
                    g.ItemNo,
                    items.GetValueOrDefault(g.ItemNo).Description ?? string.Empty,
                    items.GetValueOrDefault(g.ItemNo).DescriptionArabic,
                    g.LocationCode,
                    g.VariantCode,
                    g.Quantity,
                    g.Value,

                    // Reported positive, which is how anybody reading a valuation thinks about the
                    // part of it that is not yet certain.
                    Math.Abs(g.Estimated),

                    // Nothing rather than a division by nought. A location holding no stock and
                    // some value is a real state -- goods sold before they arrived -- and it has
                    // no unit cost, only a balance waiting to be settled.
                    g.Quantity == 0m
                        ? null
                        : Math.Round(g.Value / g.Quantity, 5, MidpointRounding.AwayFromZero)))
                .OrderBy(static r => r.ItemNo, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static r => r.LocationCode, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// How long the stock on hand has been on hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read straight off the cost layers, which is the one place the answer already exists. A
    /// receipt that still has quantity remaining is stock that arrived on its posting date and has
    /// not left, so the age of the stock is the age of the layer it is still sitting in.
    /// </para>
    /// <para>
    /// This is exact under FIFO and an approximation under average costing, where the layers are
    /// still consumed oldest-first for quantity even though the cost is averaged. That is the
    /// right approximation: it answers how long the goods have physically been there, which is
    /// what somebody looking for slow stock is asking, rather than anything about their value.
    /// </para>
    /// </remarks>
    /// <param name="asOf">The day to measure the age against.</param>
    /// <param name="itemNo">One item, or null for all of them.</param>
    /// <param name="locationCode">One location, or null for all of them.</param>
    /// <param name="bands">The band boundaries in days, or null for thirty, sixty, ninety and a hundred and eighty.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per item, variant and location holding anything.</returns>
    public async Task<IReadOnlyList<AgeingRow>> AgeingAsync(
        DateOnly asOf,
        string? itemNo = null,
        string? locationCode = null,
        IReadOnlyList<int>? bands = null,
        CancellationToken cancellationToken = default)
    {
        var boundaries = bands is { Count: > 0 } ? [.. bands.Order()] : DefaultBands;

        var query = context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.RemainingQuantity > 0m && e.PostingDate <= asOf);

        if (!string.IsNullOrWhiteSpace(itemNo))
        {
            query = query.Where(e => e.ItemNo == itemNo);
        }

        if (!string.IsNullOrWhiteSpace(locationCode))
        {
            query = query.Where(e => e.LocationCode == locationCode);
        }

        var layers = await query
            .Select(static e => new
            {
                e.Id,
                e.ItemNo,
                e.LocationCode,
                e.VariantCode,
                e.PostingDate,
                e.RemainingQuantity,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (layers.Count == 0)
        {
            return [];
        }

        var costs = await LayerCostsAsync(
                [.. layers.Select(static l => l.Id)], cancellationToken)
            .ConfigureAwait(false);

        var items = await DescriptionsAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. layers
                .GroupBy(static l => new { l.ItemNo, l.LocationCode, l.VariantCode })
                .Select(group =>
                {
                    var aged = group
                        .Select(l => new
                        {
                            Days = asOf.DayNumber - l.PostingDate.DayNumber,
                            l.RemainingQuantity,
                            Value = l.RemainingQuantity * costs.GetValueOrDefault(l.Id),
                        })
                        .ToList();

                    return new AgeingRow(
                        group.Key.ItemNo,
                        items.GetValueOrDefault(group.Key.ItemNo).Description ?? string.Empty,
                        items.GetValueOrDefault(group.Key.ItemNo).DescriptionArabic,
                        group.Key.LocationCode,
                        group.Key.VariantCode,
                        aged.Sum(static a => a.RemainingQuantity),
                        Math.Round(aged.Sum(static a => a.Value), 2, MidpointRounding.AwayFromZero),
                        Bucket(aged.Select(static a => (a.Days, a.RemainingQuantity, a.Value)), boundaries),
                        aged.Max(static a => a.Days));
                })
                .OrderByDescending(static r => r.OldestDays)
                .ThenBy(static r => r.ItemNo, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// How fast each item moves, and how long what is left would last.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Items that never moved are the point of this report, so they appear with nothing in the
    /// sold column rather than being left out. A velocity report that only lists what sold cannot
    /// answer the question anybody runs it to answer.
    /// </para>
    /// <para>
    /// Turns and days of cover are left empty rather than filled with nought where they have no
    /// answer. Nought turns and no turns are different states -- one is stock that did not sell,
    /// the other is a shelf with nothing on it -- and printing the same figure for both is how a
    /// spreadsheet ends up averaging a fiction.
    /// </para>
    /// </remarks>
    /// <param name="from">The first day of the period.</param>
    /// <param name="to">The last day of it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per item, slowest first.</returns>
    public async Task<IReadOnlyList<VelocityRow>> VelocityAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var sold = await context.Set<ValueEntry>()
            .AsNoTracking()
            .Where(v => v.PostingDate >= from && v.PostingDate <= to
                && (v.ItemLedgerEntryType == ItemLedgerEntryType.Sale
                    || v.ItemLedgerEntryType == ItemLedgerEntryType.SalesReturn))
            .GroupBy(static v => v.ItemNo)
            .Select(static g => new
            {
                ItemNo = g.Key,

                // Negated: an issue is a negative quantity and a negative cost, and nobody reads a
                // velocity report expecting to see minus signs against what sold.
                Quantity = -g.Sum(static v => v.Quantity),
                Cost = -g.Sum(static v => v.CostAmount),
            })
            .ToDictionaryAsync(static g => g.ItemNo, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var onHand = await context.Set<ValueEntry>()
            .AsNoTracking()
            .Where(v => v.PostingDate <= to)
            .GroupBy(static v => v.ItemNo)
            .Select(static g => new
            {
                ItemNo = g.Key,
                Quantity = g.Sum(static v => v.Quantity),
                Value = g.Sum(static v => v.CostAmount),
            })
            .ToDictionaryAsync(static g => g.ItemNo, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var lastMoved = await context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.PostingDate <= to)
            .GroupBy(static e => e.ItemNo)
            .Select(static g => new { ItemNo = g.Key, Last = g.Max(static e => e.PostingDate) })
            .ToDictionaryAsync(static g => g.ItemNo, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var items = await context.Set<Item>()
            .AsNoTracking()
            .Where(i => i.Kind == ItemKind.Inventory)
            .Select(static i => new { i.No, i.Description, i.DescriptionArabic })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var days = Math.Max(1, to.DayNumber - from.DayNumber + 1);

        return
        [
            .. items
                .Select(item =>
                {
                    var movement = sold.GetValueOrDefault(item.No);
                    var stock = onHand.GetValueOrDefault(item.No);

                    var quantitySold = movement?.Quantity ?? 0m;
                    var costOfSales = movement?.Cost ?? 0m;
                    var quantityOnHand = stock?.Quantity ?? 0m;
                    var valueOnHand = stock?.Value ?? 0m;

                    return new VelocityRow(
                        item.No,
                        item.Description,
                        item.DescriptionArabic,
                        quantitySold,
                        costOfSales,
                        quantityOnHand,
                        valueOnHand,

                        // Nothing on the shelf is not nought turns; it is no answer at all.
                        valueOnHand <= 0m
                            ? null
                            : Math.Round(costOfSales / valueOnHand, 2, MidpointRounding.AwayFromZero),

                        // Nothing sold is not nought days of cover; stock that never moves would
                        // last for ever, and printing nought says the opposite.
                        quantitySold <= 0m || quantityOnHand <= 0m
                            ? null
                            : Math.Round(quantityOnHand / (quantitySold / days), 0, MidpointRounding.AwayFromZero),
                        lastMoved.GetValueOrDefault(item.No)?.Last);
                })
                .OrderBy(static r => r.QuantitySold)
                .ThenByDescending(static r => r.ValueOnHand),
        ];
    }

    /// <summary>Puts each layer into the band its age falls in.</summary>
    private static IReadOnlyList<AgeingBucket> Bucket(
        IEnumerable<(int Days, decimal Quantity, decimal Value)> aged,
        IReadOnlyList<int> boundaries)
    {
        var buckets = new List<AgeingBucket>();
        var from = 0;

        foreach (var boundary in boundaries)
        {
            var inBand = aged.Where(a => a.Days >= from && a.Days <= boundary).ToList();

            buckets.Add(new AgeingBucket(
                $"{from}-{boundary}",
                from,
                boundary,
                inBand.Sum(static a => a.Quantity),
                Math.Round(inBand.Sum(static a => a.Value), 2, MidpointRounding.AwayFromZero)));

            from = boundary + 1;
        }

        var older = aged.Where(a => a.Days >= from).ToList();

        buckets.Add(new AgeingBucket(
            $"{from}+",
            from,
            null,
            older.Sum(static a => a.Quantity),
            Math.Round(older.Sum(static a => a.Value), 2, MidpointRounding.AwayFromZero)));

        return buckets;
    }

    private async Task<Dictionary<Guid, decimal>> LayerCostsAsync(
        List<Guid> layerIds,
        CancellationToken cancellationToken)
    {
        var costs = await context.Set<ValueEntry>()
            .AsNoTracking()
            .Where(v => layerIds.Contains(v.ItemLedgerEntryId))
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

        return costs.ToDictionary(
            static c => c.EntryId,
            static c => LayerCosting.UnitCost(c.Cost, c.Quantity, c.Revalued));
    }

    private async Task<Dictionary<string, (string Description, string? DescriptionArabic)>> DescriptionsAsync(
        CancellationToken cancellationToken)
        => await context.Set<Item>()
            .AsNoTracking()
            .ToDictionaryAsync(
                static i => i.No,
                static i => (i.Description, i.DescriptionArabic),
                StringComparer.OrdinalIgnoreCase,
                cancellationToken)
            .ConfigureAwait(false);
}
