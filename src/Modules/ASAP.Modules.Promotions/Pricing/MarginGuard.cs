using ASAP.Modules.Promotions.Offers;
using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Promotions.Pricing;

/// <summary>What an offer would do to one line's margin.</summary>
/// <param name="LineNo">The line.</param>
/// <param name="ItemNo">What is being sold.</param>
/// <param name="UnitCost">What it costs today.</param>
/// <param name="OfferUnitPrice">What the offer would charge for it.</param>
/// <param name="MarginPercent">
/// What is left, as a percentage of the selling price. Negative when the offer sells below cost.
/// </param>
/// <param name="FloorPercent">The least this company accepts.</param>
/// <param name="ShortfallPerUnit">
/// How far under the floor it is, per unit, in money. The figure somebody actually needs: a
/// percentage tells you there is a problem and money tells you how big it is.
/// </param>
public readonly record struct MarginCheck(
    int LineNo,
    string ItemNo,
    decimal UnitCost,
    decimal OfferUnitPrice,
    decimal MarginPercent,
    decimal FloorPercent,
    decimal ShortfallPerUnit)
{
    /// <summary>Whether the offer clears the floor.</summary>
    public bool IsAcceptable => MarginPercent >= FloorPercent;
}

/// <summary>
/// Refuses an offer that would sell below what the company is prepared to accept.
/// </summary>
/// <remarks>
/// <para>
/// Priced against live cost, and that is the whole point. An offer is written weeks before it
/// runs, against costs that were true then. Suppliers put prices up. A promotions system that
/// checked the margin once, at design time, would be one that ran a loss-making campaign for a
/// fortnight and reported it afterwards.
/// </para>
/// <para>
/// The floor is expressed as a percentage of the selling price rather than of cost, because that
/// is what a gross margin is and what every report the company already runs will compare it to.
/// A floor of zero means "never below cost", which is the setting most shops want and the one
/// that ships.
/// </para>
/// <para>
/// It refuses rather than warns. Selling at a loss is a decision somebody is entitled to make —
/// clearing old stock is a real reason — but it should be made by somebody holding the permission
/// to make it, not arrived at by a category discount nobody checked the contents of.
/// </para>
/// </remarks>
public static class MarginGuard
{
    /// <summary>
    /// Works out what an offer leaves on a line.
    /// </summary>
    /// <param name="line">The line, carrying today's cost.</param>
    /// <param name="discount">What the offer would take off the whole line.</param>
    /// <param name="floorPercent">The least margin this company accepts.</param>
    /// <returns>The figures, and whether they clear the floor.</returns>
    public static MarginCheck Check(BasketLine line, decimal discount, decimal floorPercent)
    {
        var quantity = line.Quantity <= 0m ? 1m : line.Quantity;
        var netAmount = OfferCalculator.NetAmount(line) - discount;
        var unitPrice = Round(netAmount / quantity);

        return new MarginCheck(
            line.LineNo,
            line.ItemNo,
            line.UnitCost,
            unitPrice,
            MarginPercent(unitPrice, line.UnitCost),
            floorPercent,
            Round(FloorPrice(line.UnitCost, floorPercent) - unitPrice));
    }

    /// <summary>
    /// The margin an offer leaves, as a percentage of what the customer pays.
    /// </summary>
    /// <remarks>
    /// Giving something away is a hundred per cent loss rather than an infinite one. Dividing by a
    /// selling price of nothing has no answer, and reporting one would put a figure on a screen
    /// that no arithmetic produced.
    /// </remarks>
    public static decimal MarginPercent(decimal unitPrice, decimal unitCost)
    {
        if (unitPrice == 0m)
        {
            return unitCost == 0m ? 0m : -100m;
        }

        return Round((unitPrice - unitCost) / unitPrice * 100m);
    }

    /// <summary>
    /// The lowest price that clears the floor.
    /// </summary>
    /// <remarks>
    /// Rearranged from the margin: a floor of twenty per cent on a cost of eighty is a price of a
    /// hundred, not ninety-six. Working it the other way is the mistake that makes a shop think it
    /// is holding a margin it is not.
    /// </remarks>
    public static decimal FloorPrice(decimal unitCost, decimal floorPercent)
    {
        if (floorPercent >= 100m)
        {
            // Nothing satisfies it, and dividing by nought would claim something does.
            return decimal.MaxValue;
        }

        return Round(unitCost / (1m - (floorPercent / 100m)));
    }

    /// <summary>
    /// The arguments a refusal needs, so the message can name every figure.
    /// </summary>
    /// <param name="check">What the offer would leave.</param>
    /// <param name="offer">The offer.</param>
    /// <param name="description">What the item is called.</param>
    /// <returns>Values for the message template.</returns>
    public static Dictionary<string, object?> Arguments(
        MarginCheck check,
        Offer offer,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(offer);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OfferCode"] = offer.Code,
            ["OfferName"] = offer.Name,
            ["ItemNo"] = check.ItemNo,
            ["Description"] = description,
            ["UnitCost"] = check.UnitCost,
            ["OfferPrice"] = check.OfferUnitPrice,
            ["MarginPercent"] = check.MarginPercent,
            ["FloorPercent"] = check.FloorPercent,
            ["Shortfall"] = check.ShortfallPerUnit,
        };
    }

    /// <summary>Where a refusal points, so a screen can highlight the offending row.</summary>
    /// <param name="check">What the offer would leave.</param>
    /// <returns>The target for the message.</returns>
    public static MessageTarget TargetFor(MarginCheck check)
        => MessageTarget.OnField($"Lines[{check.LineNo}]");

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
