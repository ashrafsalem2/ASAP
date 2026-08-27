using ASAP.Modules.Promotions.Offers;

namespace ASAP.Modules.Promotions.Pricing;

/// <summary>
/// Works out what one offer takes off one line.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately free of the database, the clock and the container. Everything about whether an
/// offer applies is decided by the caller and passed in; everything here is arithmetic. That is
/// what makes three-for-two testable without a shop.
/// </para>
/// <para>
/// Every method returns a positive amount to take off, never a new price. A promotion that
/// rewrote prices would leave a receipt unable to say what the customer saved, and being able to
/// say that is most of the point of running one.
/// </para>
/// </remarks>
public static class OfferCalculator
{
    /// <summary>
    /// What this offer takes off this line.
    /// </summary>
    /// <param name="offer">The offer, already known to apply.</param>
    /// <param name="line">The line.</param>
    /// <param name="qualifyingAmount">
    /// What the qualifying lines come to, for an offer measured across several of them. Equal to
    /// the line's own amount for the kinds that are not.
    /// </param>
    /// <returns>How much to take off, never more than the line is worth and never negative.</returns>
    public static decimal DiscountFor(Offer offer, BasketLine line, decimal qualifyingAmount)
    {
        ArgumentNullException.ThrowIfNull(offer);

        var lineAmount = NetAmount(line);

        if (lineAmount <= 0m || line.Quantity <= 0m)
        {
            return 0m;
        }

        var discount = offer.Kind switch
        {
            OfferKind.Percentage => lineAmount * (offer.Value / 100m),
            OfferKind.AmountPerUnit => offer.Value * line.Quantity,
            OfferKind.BuyXGetY => BuyXGetY(offer, line),
            OfferKind.Threshold => Threshold(offer, lineAmount, qualifyingAmount),
            OfferKind.FixedPrice => Math.Max(lineAmount - offer.Value, 0m),
            _ => 0m,
        };

        // Never more than the line is worth. An offer that took a line negative would be paying
        // the customer to shop, which no offer means and every rounding error eventually tries.
        return Round(Math.Clamp(discount, 0m, lineAmount));
    }

    /// <summary>What the line comes to after any discount the person keying it already applied.</summary>
    public static decimal NetAmount(BasketLine line)
        => Round(line.Quantity * line.UnitPrice * (1m - (line.ManualDiscountPercent / 100m)));

    /// <summary>What the goods on the line cost.</summary>
    public static decimal CostAmount(BasketLine line) => Round(line.Quantity * line.UnitCost);

    /// <summary>
    /// Three for two, and everything shaped like it.
    /// </summary>
    /// <remarks>
    /// The free ones are the cheapest, because that is what a customer expects and what every
    /// shop does. Charging for the cheapest and discounting the dearest is arithmetically the same
    /// offer and feels like a trick, so the convention is worth keeping even on a single line
    /// where every unit is the same price.
    /// </remarks>
    private static decimal BuyXGetY(Offer offer, BasketLine line)
    {
        if (offer.BuyQuantity <= 0m || offer.GetQuantity <= 0m)
        {
            return 0m;
        }

        // How many complete deals the line contains. Four items on a three-for-two is one deal
        // and one item at full price, not one and a third deals.
        var deals = Math.Floor(line.Quantity / (offer.BuyQuantity + offer.GetQuantity));

        if (deals <= 0m)
        {
            return 0m;
        }

        var freeUnits = deals * offer.GetQuantity;
        var unitNet = line.UnitPrice * (1m - (line.ManualDiscountPercent / 100m));

        return freeUnits * unitNet * (offer.GetDiscountPercent / 100m);
    }

    /// <summary>
    /// Spend past a threshold and the discount applies.
    /// </summary>
    /// <remarks>
    /// Measured across the qualifying lines only. Measuring it across the whole basket would let a
    /// bag of crisps unlock a discount on furniture, which is not what anybody setting a threshold
    /// on a category meant.
    /// </remarks>
    private static decimal Threshold(Offer offer, decimal lineAmount, decimal qualifyingAmount)
        => qualifyingAmount >= offer.Value ? lineAmount * (offer.GetDiscountPercent / 100m) : 0m;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
