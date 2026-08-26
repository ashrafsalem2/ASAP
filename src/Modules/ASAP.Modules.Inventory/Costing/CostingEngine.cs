using ASAP.Modules.Inventory.Items;

namespace ASAP.Modules.Inventory.Costing;

/// <summary>
/// A receipt with stock still available to draw from.
/// </summary>
/// <param name="EntryId">The inbound item ledger entry.</param>
/// <param name="PostingDate">When it was received, which is what FIFO orders by.</param>
/// <param name="RemainingQuantity">How much of it has not yet been consumed. Always positive.</param>
/// <param name="UnitCost">What it cost per unit.</param>
public readonly record struct InboundLayer(
    Guid EntryId,
    DateOnly PostingDate,
    decimal RemainingQuantity,
    decimal UnitCost);

/// <summary>
/// One part of an outbound movement, and where its cost came from.
/// </summary>
/// <param name="InboundEntryId">
/// The receipt it was taken from, or null when there was no stock and the cost is an estimate.
/// </param>
/// <param name="Quantity">How many units this part covers. Always positive.</param>
/// <param name="UnitCost">The cost per unit applied to it.</param>
/// <param name="CostAmount">The cost of this part, rounded to the currency.</param>
/// <param name="IsEstimate">True when nothing backed it and the cost will have to be settled later.</param>
public readonly record struct CostApplication(
    Guid? InboundEntryId,
    decimal Quantity,
    decimal UnitCost,
    decimal CostAmount,
    bool IsEstimate);

/// <summary>What an outbound movement cost, and how that was arrived at.</summary>
/// <param name="Applications">Each part of the movement and the receipt it came from.</param>
/// <param name="TotalCost">The whole cost, negative because stock is leaving.</param>
/// <param name="ShortfallQuantity">How much was taken with nothing on hand to back it.</param>
public sealed record CostingOutcome(
    IReadOnlyList<CostApplication> Applications,
    decimal TotalCost,
    decimal ShortfallQuantity)
{
    /// <summary>True when the movement drove stock below zero.</summary>
    public bool WentNegative => ShortfallQuantity > 0;

    /// <summary>
    /// The part of the cost that is an estimate rather than settled. Kept out of the general
    /// ledger until the goods arrive and the real figure is known.
    /// </summary>
    public decimal EstimatedCost =>
        Applications.Where(static a => a.IsEstimate).Sum(static a => a.CostAmount);
}

/// <summary>
/// Works out what leaving stock costs.
/// </summary>
/// <remarks>
/// <para>
/// Pure logic with no database behind it, so every rule here is settled by a test rather than by
/// reading it and hoping. Costing is the part of an ERP that goes wrong quietly: nothing errors,
/// the numbers simply drift, and the discrepancy is found at year end by someone who cannot say
/// when it started.
/// </para>
/// <para>
/// The case this is built around is selling stock that is not there. ASAP allows it, because a
/// shop that can see the goods on the shelf should not be stopped from selling them by paperwork
/// that has not caught up. What it will not do is invent a cost and forget about it: the shortfall
/// is valued at an estimate, marked as an estimate, and settled against the real cost when the
/// receipt arrives. That is the whole difference between permitting negative stock and corrupting
/// the books with it.
/// </para>
/// </remarks>
public static class CostingEngine
{
    /// <summary>Decimals a cost amount is rounded to. Every one of these becomes a ledger amount.</summary>
    private const int CostDecimals = 2;

    /// <summary>Decimals a unit cost keeps. More than the amount, because it is divided into.</summary>
    private const int UnitCostDecimals = 5;

    /// <summary>
    /// Works out the cost of taking a quantity out of stock.
    /// </summary>
    /// <param name="quantity">How much is leaving. Positive.</param>
    /// <param name="layers">Receipts with stock still available, in any order.</param>
    /// <param name="method">How the item is costed.</param>
    /// <param name="fallbackUnitCost">
    /// What to value a shortfall at when there is no stock to draw from. The item's current unit
    /// cost, or its last purchase cost when it has never been costed.
    /// </param>
    /// <param name="standardCost">The fixed cost, when the item is costed at standard.</param>
    /// <returns>What it cost, and which receipts it came from.</returns>
    public static CostingOutcome ApplyOutbound(
        decimal quantity,
        IReadOnlyList<InboundLayer> layers,
        CostingMethod method,
        decimal fallbackUnitCost,
        decimal standardCost = 0m)
    {
        ArgumentNullException.ThrowIfNull(layers);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "An outbound quantity must be positive. The sign is applied when the entry is written.");
        }

        return method switch
        {
            CostingMethod.Standard => AtFixedCost(quantity, standardCost),
            CostingMethod.Average => AtAverage(quantity, layers, fallbackUnitCost),
            _ => AtFifo(quantity, layers, fallbackUnitCost),
        };
    }

    /// <summary>
    /// Takes from the oldest receipts first, and estimates whatever is left over.
    /// </summary>
    /// <remarks>
    /// Ordered by posting date and then by entry key. The tie-break matters: two receipts on the
    /// same day would otherwise be consumed in whatever order the database returned them, so the
    /// same sale could cost two different amounts on two runs, and nobody could explain either.
    /// </remarks>
    private static CostingOutcome AtFifo(
        decimal quantity,
        IReadOnlyList<InboundLayer> layers,
        decimal fallbackUnitCost)
    {
        var applications = new List<CostApplication>();
        var outstanding = quantity;

        var ordered = layers
            .Where(static l => l.RemainingQuantity > 0)
            .OrderBy(static l => l.PostingDate)
            .ThenBy(static l => l.EntryId);

        foreach (var layer in ordered)
        {
            if (outstanding <= 0)
            {
                break;
            }

            var taken = Math.Min(outstanding, layer.RemainingQuantity);

            applications.Add(new CostApplication(
                layer.EntryId,
                taken,
                layer.UnitCost,
                Round(taken * layer.UnitCost, CostDecimals),
                IsEstimate: false));

            outstanding -= taken;
        }

        AddShortfall(applications, outstanding, fallbackUnitCost);

        return Summarise(applications, outstanding);
    }

    /// <summary>
    /// Values the whole movement at the weighted average of everything on hand.
    /// </summary>
    /// <remarks>
    /// One application per receipt even so, rather than a single row at the average. The cost is
    /// the same either way, and keeping the breakdown means the receipts are still marked as
    /// consumed -- so a later switch of method, or a question about which goods were sold, has an
    /// answer rather than a shrug.
    /// </remarks>
    private static CostingOutcome AtAverage(
        decimal quantity,
        IReadOnlyList<InboundLayer> layers,
        decimal fallbackUnitCost)
    {
        var available = layers.Where(static l => l.RemainingQuantity > 0).ToList();

        var onHand = available.Sum(static l => l.RemainingQuantity);
        var valueOnHand = available.Sum(static l => l.RemainingQuantity * l.UnitCost);

        var average = onHand > 0
            ? Round(valueOnHand / onHand, UnitCostDecimals)
            : fallbackUnitCost;

        var applications = new List<CostApplication>();
        var outstanding = quantity;

        foreach (var layer in available.OrderBy(static l => l.PostingDate).ThenBy(static l => l.EntryId))
        {
            if (outstanding <= 0)
            {
                break;
            }

            var taken = Math.Min(outstanding, layer.RemainingQuantity);

            applications.Add(new CostApplication(
                layer.EntryId,
                taken,
                average,
                Round(taken * average, CostDecimals),
                IsEstimate: false));

            outstanding -= taken;
        }

        AddShortfall(applications, outstanding, average);

        return Summarise(applications, outstanding);
    }

    /// <summary>
    /// Values everything at the fixed cost, whatever is on hand.
    /// </summary>
    /// <remarks>
    /// Standard costing does not consult the receipts, which is the point of it: the cost of a
    /// sale is known before anything is bought, and the difference from what was actually paid is
    /// reported as variance. So there is no shortfall here either -- selling into negative stock at
    /// standard cost is exact, not an estimate.
    /// </remarks>
    private static CostingOutcome AtFixedCost(decimal quantity, decimal standardCost)
    {
        var applications = new List<CostApplication>
        {
            new(null, quantity, standardCost, Round(quantity * standardCost, CostDecimals), IsEstimate: false),
        };

        return Summarise(applications, shortfall: 0m);
    }

    /// <summary>
    /// Values the part of a movement that nothing on hand could cover.
    /// </summary>
    /// <remarks>
    /// Marked as an estimate, with no receipt behind it. That pair of facts is what the settlement
    /// routine looks for when goods finally arrive, and what keeps this cost out of the general
    /// ledger in the meantime.
    /// </remarks>
    private static void AddShortfall(
        List<CostApplication> applications,
        decimal shortfall,
        decimal estimateUnitCost)
    {
        if (shortfall <= 0)
        {
            return;
        }

        applications.Add(new CostApplication(
            InboundEntryId: null,
            shortfall,
            estimateUnitCost,
            Round(shortfall * estimateUnitCost, CostDecimals),
            IsEstimate: true));
    }

    private static CostingOutcome Summarise(List<CostApplication> applications, decimal shortfall)
        => new(
            applications,

            // Negative because stock is leaving and its value comes off the balance sheet.
            -applications.Sum(static a => a.CostAmount),
            shortfall);

    /// <summary>
    /// Settles an estimate against the cost that was actually paid.
    /// </summary>
    /// <param name="estimatedUnitCost">What the sale was valued at when nothing was on hand.</param>
    /// <param name="actualUnitCost">What the goods turned out to cost when they arrived.</param>
    /// <param name="quantity">How many units are being settled.</param>
    /// <returns>
    /// The correction to post, signed so that adding it to the original leaves the true figure.
    /// Zero when the estimate happened to be right, in which case nothing is posted at all.
    /// </returns>
    /// <remarks>
    /// This is the second half of allowing negative stock, and the half that is usually missing.
    /// Selling from stock that is not there is only safe if the guess is corrected afterwards;
    /// without this the books carry whatever the guess happened to be, for ever.
    /// </remarks>
    public static decimal SettleEstimate(
        decimal estimatedUnitCost,
        decimal actualUnitCost,
        decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "A settlement quantity must be positive.");
        }

        var estimated = Round(quantity * estimatedUnitCost, CostDecimals);
        var actual = Round(quantity * actualUnitCost, CostDecimals);

        // Negated because the cost of goods sold is a credit to inventory: if the goods cost more
        // than estimated, more value has to come off stock, which is a larger negative.
        return -(actual - estimated);
    }

    /// <summary>
    /// The weighted average unit cost of a set of receipts.
    /// </summary>
    /// <param name="layers">Receipts with stock on hand.</param>
    /// <returns>The average, or zero when nothing is on hand.</returns>
    public static decimal AverageUnitCost(IReadOnlyList<InboundLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var onHand = layers.Sum(static l => l.RemainingQuantity);

        if (onHand <= 0)
        {
            return 0m;
        }

        return Round(layers.Sum(static l => l.RemainingQuantity * l.UnitCost) / onHand, UnitCostDecimals);
    }

    /// <summary>
    /// Rounds away from zero, which is what accounting expects.
    /// </summary>
    /// <remarks>
    /// .NET rounds to even by default, so 0.125 becomes 0.12. That is right for statistics and
    /// wrong for money: an accountant expects 0.13, and a system that quietly rounds half its
    /// half-fils downwards accumulates a bias nobody can account for.
    /// </remarks>
    private static decimal Round(decimal value, int decimals)
        => Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
