using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Promotions.Offers;
using ASAP.Platform.Kernel.Promotions;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Promotions.Reporting;

/// <summary>What one offer actually did.</summary>
/// <param name="OfferCode">The offer.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Kind">What sort of offer it is.</param>
/// <param name="StartsOn">When it started.</param>
/// <param name="EndsOn">When it ends, or nothing where it runs on.</param>
/// <param name="IsActive">Whether it may still apply.</param>
/// <param name="TimesApplied">How many lines it took money off.</param>
/// <param name="Documents">How many separate sales it appeared on.</param>
/// <param name="Quantity">How many units went out under it.</param>
/// <param name="DiscountGiven">What it gave away.</param>
/// <param name="NetSold">What the customers actually paid on those lines.</param>
/// <param name="CostOfSales">What those goods cost, where the cost is known.</param>
/// <param name="QuantityWithoutCost">
/// How many units carry no cost at all, so the margin below is measured on less than everything.
/// </param>
/// <param name="Margin">
/// What was left after cost, on the lines that have one. Nothing where none of them do.
/// </param>
/// <param name="MarginPercent">
/// That margin as a share of what was paid, or nothing where there is nothing to divide by.
/// </param>
public readonly record struct OfferUptakeRow(
    string OfferCode,
    string Name,
    string? NameArabic,
    string Kind,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    bool IsActive,
    int TimesApplied,
    int Documents,
    decimal Quantity,
    decimal DiscountGiven,
    decimal NetSold,
    decimal CostOfSales,
    decimal QuantityWithoutCost,
    decimal? Margin,
    decimal? MarginPercent);

/// <summary>How much of an item moved before an offer and during it.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is.</param>
/// <param name="DescriptionArabic">What it is, in Arabic.</param>
/// <param name="QuantityBefore">How much went in the window before.</param>
/// <param name="QuantityDuring">How much went in the offer's own window.</param>
/// <param name="Change">The difference, which is a comparison and not a cause.</param>
public readonly record struct OfferMovementRow(
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    decimal QuantityBefore,
    decimal QuantityDuring,
    decimal Change);

/// <summary>
/// What the offers did, and what one would do before it runs.
/// </summary>
/// <remarks>
/// <para>
/// The uptake report is built from what every selling module says it sold under an offer, asked
/// through <see cref="IOfferUsage"/> rather than looked up. Promotions cannot see the till: the
/// till depends on Promotions and not the other way about, and a report that reached across that
/// the wrong way would either break the module graph or quietly cover one door out of several.
/// </para>
/// <para>
/// The margin is measured against what the costing engine said at the moment of sale -- the same
/// figure the margin floor was checked against when the offer was let through. A report and a
/// refusal that read different numbers would eventually disagree, and the report is the one
/// nobody can argue with afterwards.
/// </para>
/// <para>
/// What an offer <em>would</em> do before it runs is not here. That arithmetic already exists in
/// <c>OfferService.PreviewAsync</c>, where it is also what refuses an offer that breaks the margin
/// floor, and a second copy would drift the first time either changed -- with the report and the
/// refusal then disagreeing about the same offer.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="usage">Every module that sells under an offer.</param>
/// <param name="clock">Supplies today, for an offer that has not ended yet.</param>
public sealed class PromotionReportService(
    AsapDbContext context,
    IEnumerable<IOfferUsage> usage,
    IClock clock)
{
    /// <summary>
    /// What each offer did over a period.
    /// </summary>
    /// <remarks>
    /// Offers that never applied are included. An offer nobody used is the most useful row in the
    /// report, and one that lists only what worked cannot say which campaign was a waste of a
    /// fortnight.
    /// </remarks>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per offer, most given away first.</returns>
    public async Task<IReadOnlyList<OfferUptakeRow>> UptakeAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var lines = await UsageAsync(from, to, cancellationToken).ConfigureAwait(false);

        var byOffer = lines
            .GroupBy(static l => l.OfferCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var offers = await context.Set<Offer>()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. offers
                .Select(offer =>
                {
                    var used = byOffer.GetValueOrDefault(offer.Code) ?? [];

                    var costed = used.Where(static l => l.UnitCostAtSale is not null).ToList();
                    var cost = costed.Sum(l => l.Quantity * l.UnitCostAtSale!.Value);
                    var netOfCosted = costed.Sum(static l => l.NetAmount);

                    var margin = costed.Count == 0 ? (decimal?)null : netOfCosted - cost;

                    return new OfferUptakeRow(
                        offer.Code,
                        offer.Name,
                        offer.NameArabic,
                        offer.Kind.ToString(),
                        offer.StartsOn,
                        offer.EndsOn,
                        offer.IsActive,
                        used.Count,
                        used.Select(static l => l.DocumentNo).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        used.Sum(static l => l.Quantity),
                        Round(used.Sum(static l => l.DiscountAmount)),
                        Round(used.Sum(static l => l.NetAmount)),
                        Round(cost),

                        // Said plainly rather than folded in. A margin measured on two thirds of
                        // the units is a different claim from one measured on all of them, and
                        // the report has to be able to make that difference visible.
                        used.Where(static l => l.UnitCostAtSale is null).Sum(static l => l.Quantity),
                        margin is null ? null : Round(margin.Value),

                        // Nothing to divide by is not nought per cent. An offer that gave goods
                        // away has a real negative margin and no percentage, and printing nought
                        // is a lie a spreadsheet then averages.
                        margin is null || netOfCosted == 0m
                            ? null
                            : Round(margin.Value / netOfCosted * 100m));
                })
                .OrderByDescending(static r => r.DiscountGiven)
                .ThenBy(static r => r.OfferCode, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// How much of the offer's items moved during it, against the same length of time before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A comparison, and deliberately not called anything stronger. Sales move for a great many
    /// reasons -- a season, a competitor, the weather, a shelf that was empty in the earlier
    /// window -- and none of them are visible here. Anything that reported a cannibalisation
    /// figure from these two numbers would be inventing a cause out of a coincidence, and it
    /// would be believed because it had a decimal point in it.
    /// </para>
    /// <para>
    /// What it is good for is noticing. An item that sold no more under an offer than without one
    /// is worth a question, and the question is the output.
    /// </para>
    /// </remarks>
    /// <param name="offerCode">The offer.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per item the offer targets.</returns>
    public async Task<IReadOnlyList<OfferMovementRow>> MovementAsync(
        string offerCode,
        CancellationToken cancellationToken = default)
    {
        var offer = await context.Set<Offer>()
            .AsNoTracking()
            .Include(o => o.Targets)
            .FirstOrDefaultAsync(o => o.Code == offerCode, cancellationToken)
            .ConfigureAwait(false);

        if (offer is null)
        {
            return [];
        }

        var ends = offer.EndsOn ?? clock.Today;
        var days = Math.Max(1, ends.DayNumber - offer.StartsOn.DayNumber + 1);

        var before = await SoldAsync(
                offer.StartsOn.AddDays(-days), offer.StartsOn.AddDays(-1), cancellationToken)
            .ConfigureAwait(false);

        var during = await SoldAsync(offer.StartsOn, ends, cancellationToken).ConfigureAwait(false);

        var itemNos = offer.Targets
            .Where(static t => t.ItemNo is { Length: > 0 })
            .Select(static t => t.ItemNo!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = await context.Set<Item>()
            .AsNoTracking()
            .Where(i => itemNos.Contains(i.No))
            .ToDictionaryAsync(static i => i.No, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. itemNos
                .Select(itemNo =>
                {
                    var was = before.GetValueOrDefault(itemNo);
                    var now = during.GetValueOrDefault(itemNo);
                    var item = items.GetValueOrDefault(itemNo);

                    return new OfferMovementRow(
                        itemNo,
                        item?.Description ?? string.Empty,
                        item?.DescriptionArabic,
                        was,
                        now,
                        now - was);
                })
                .OrderBy(static r => r.Change),
        ];
    }

    /// <summary>How much of each item sold in a window, under any offer or none.</summary>
    private async Task<Dictionary<string, decimal>> SoldAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var entries = await context.Set<Inventory.Ledger.ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.PostingDate >= from
                && e.PostingDate <= to
                && e.EntryType == Inventory.Ledger.ItemLedgerEntryType.Sale)
            .GroupBy(static e => e.ItemNo)
            .Select(static g => new { ItemNo = g.Key, Quantity = -g.Sum(static e => e.Quantity) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entries.ToDictionary(
            static e => e.ItemNo,
            static e => e.Quantity,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<OfferUsageLine>> UsageAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var lines = new List<OfferUsageLine>();

        foreach (var source in usage)
        {
            lines.AddRange(await source.BetweenAsync(from, to, cancellationToken).ConfigureAwait(false));
        }

        return lines;
    }

    private static decimal Round(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
