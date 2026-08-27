using ASAP.Modules.Pos.Receipts;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Pos.Reporting;

/// <summary>How one offer did.</summary>
/// <param name="OfferCode">The offer.</param>
/// <param name="Receipts">How many sales it appeared on.</param>
/// <param name="Units">How many units it moved.</param>
/// <param name="GivenAway">What it cost, which is what came off the price.</param>
/// <param name="RevenueAtList">What those goods would have come to at the shelf price.</param>
/// <param name="NetRevenue">What was actually charged for them.</param>
/// <param name="CostOfGoods">What they cost, at the cost recorded when they went out.</param>
/// <param name="CostIsKnown">
/// Whether every line that sold goods had a cost recorded. False where any did not, and the
/// margin below is then not an answer — a report that quietly treated a missing cost as nothing
/// would claim a hundred per cent margin on the strength of having no data at all.
/// <para>
/// A charge line — a delivery fee, a service — has no goods behind it and so no cost, and that
/// is not a gap. Counting it as one would make a single delivery charge silence the margin on
/// every campaign it ever appeared beside.
/// </para>
/// </param>
public readonly record struct OfferUptakeRow(
    string OfferCode,
    int Receipts,
    decimal Units,
    decimal GivenAway,
    decimal RevenueAtList,
    decimal NetRevenue,
    decimal CostOfGoods,
    bool CostIsKnown)
{
    /// <summary>What was left after the goods were paid for, or null where the cost is not known.</summary>
    public decimal? GrossProfit => CostIsKnown ? NetRevenue - CostOfGoods : null;

    /// <summary>
    /// What was left, as a percentage of what was charged.
    /// </summary>
    /// <remarks>
    /// The figure the whole promotion is judged on, and the one the margin floor was checked
    /// against when each sale went through — so a campaign that reports below the floor here is
    /// one where costs moved after the sale, not one where the floor failed.
    /// </remarks>
    public decimal? RealisedMarginPercent
        => !CostIsKnown || NetRevenue == 0m
            ? null
            : Round((NetRevenue - CostOfGoods) / NetRevenue * 100m);

    /// <summary>How much of the shelf price was given away.</summary>
    public decimal DiscountPercent
        => RevenueAtList == 0m ? 0m : Round(GivenAway / RevenueAtList * 100m);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>What every offer did over a period, and what the shop sold without one.</summary>
/// <param name="From">The first day counted.</param>
/// <param name="To">The last day counted.</param>
/// <param name="Offers">One row per offer, most given away first.</param>
/// <param name="UnpromotedNetRevenue">What was sold at the ordinary price.</param>
/// <param name="UnpromotedCostOfGoods">What that cost.</param>
/// <param name="UnpromotedCostIsKnown">Whether every unpromoted line had a cost recorded.</param>
public readonly record struct OfferUptake(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<OfferUptakeRow> Offers,
    decimal UnpromotedNetRevenue,
    decimal UnpromotedCostOfGoods,
    bool UnpromotedCostIsKnown)
{
    /// <summary>Everything the offers gave away.</summary>
    public decimal TotalGivenAway => Offers.Sum(static o => o.GivenAway);

    /// <summary>What the shop took on promoted lines.</summary>
    public decimal PromotedNetRevenue => Offers.Sum(static o => o.NetRevenue);

    /// <summary>
    /// The margin left on everything sold without an offer.
    /// </summary>
    /// <remarks>
    /// Reported beside the promoted margin because the number nobody can interpret is a
    /// promotion's margin on its own. Twenty per cent is either good or a disaster depending
    /// entirely on what the shop makes when it is not discounting, and this is that.
    /// </remarks>
    public decimal? UnpromotedMarginPercent
        => !UnpromotedCostIsKnown || UnpromotedNetRevenue == 0m
            ? null
            : Math.Round(
                (UnpromotedNetRevenue - UnpromotedCostOfGoods) / UnpromotedNetRevenue * 100m,
                2,
                MidpointRounding.AwayFromZero);
}

/// <summary>
/// What the promotions actually did.
/// </summary>
/// <remarks>
/// <para>
/// Read from receipt lines rather than from counters on the offer. There were counters, and
/// nothing wrote to them; the offer screen showed zero beside an offer that had been used, which
/// is worse than showing nothing because a figure that is present is believed.
/// </para>
/// <para>
/// Reading from the lines also answers questions a counter cannot: this month, at this branch,
/// against what the shop makes when it is not discounting. That last one matters most — a
/// promotion's margin on its own is a number nobody can interpret.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
public sealed class PromotionUptakeReport(AsapDbContext context)
{
    /// <summary>Runs the report.</summary>
    /// <param name="from">The first day to count.</param>
    /// <param name="to">The last day to count.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per offer, and what the shop did without one.</returns>
    public async Task<OfferUptake> RunAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        // Only what was paid for. A parked basket is not a sale and a voided one never was, and
        // counting either would report a campaign moving stock that is still on the shelf.
        var lines = await context.Set<PosReceipt>()
            .AsNoTracking()
            .Where(r => r.Status == PosReceiptStatus.Posted
                        && r.BusinessDate >= from
                        && r.BusinessDate <= to)
            .SelectMany(static r => r.Lines.Select(l => new
            {
                r.No,
                l.OfferCode,
                l.Type,
                l.Quantity,
                l.UnitPrice,
                l.DiscountPercent,
                l.OfferDiscountAmount,
                l.UnitCostAtSale,
            }))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var offers = lines
            .Where(static l => !string.IsNullOrWhiteSpace(l.OfferCode))
            .GroupBy(static l => l.OfferCode!, StringComparer.OrdinalIgnoreCase)
            .Select(static g => new OfferUptakeRow(
                g.Key,

                // Distinct, because one receipt carrying three lines on the same offer is one
                // customer who took it up, not three.
                g.Select(static l => l.No).Distinct(StringComparer.Ordinal).Count(),
                Round(g.Sum(static l => l.Quantity)),
                Round(g.Sum(static l => l.OfferDiscountAmount)),
                Round(g.Sum(static l => l.Quantity * l.UnitPrice)),
                Round(g.Sum(static l =>
                    (l.Quantity * l.UnitPrice * (1m - (l.DiscountPercent / 100m)))
                    - l.OfferDiscountAmount)),
                Round(g.Sum(static l => l.Quantity * (l.UnitCostAtSale ?? 0m))),
                g.Where(static l => l.Type is PosLineType.Item)
                 .All(static l => l.UnitCostAtSale is not null)))
            .OrderByDescending(static r => r.GivenAway)
            .ToList();

        var plain = lines.Where(static l => string.IsNullOrWhiteSpace(l.OfferCode)).ToList();

        return new OfferUptake(
            from,
            to,
            offers,
            Round(plain.Sum(static l => l.Quantity * l.UnitPrice * (1m - (l.DiscountPercent / 100m)))),
            Round(plain.Sum(static l => l.Quantity * (l.UnitCostAtSale ?? 0m))),
            plain.Where(static l => l.Type is PosLineType.Item)
                 .All(static l => l.UnitCostAtSale is not null));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
