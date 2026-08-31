namespace ASAP.Modules.Inventory.Items;

/// <summary>
/// How much a reorder policy asks for, given where stock stands.
/// </summary>
/// <remarks>
/// Kept apart from the worksheet that uses it because it is the part worth being sure about, and
/// because the same figures have to be shown on screen beside the suggestion. A worksheet that
/// proposed a number nobody could reproduce would be a worksheet nobody trusted.
/// </remarks>
public static class Reordering
{
    /// <summary>
    /// What is available to plan against.
    /// </summary>
    /// <remarks>
    /// Free stock plus what is already on order. The second term is the one that matters: without
    /// it the worksheet suggests the same order every day until the goods arrive, and a week of
    /// that is a stockroom nobody can pay for.
    /// </remarks>
    /// <param name="quantityOnHand">What is on the shelf.</param>
    /// <param name="quantityReserved">What is promised to somebody else.</param>
    /// <param name="quantityOnOrder">What is bought and not yet received.</param>
    /// <returns>What can be counted on.</returns>
    public static decimal Projected(
        decimal quantityOnHand,
        decimal quantityReserved,
        decimal quantityOnOrder)
        => quantityOnHand - quantityReserved + quantityOnOrder;

    /// <summary>
    /// How much to order, or zero where nothing is needed.
    /// </summary>
    /// <param name="policy">The policy.</param>
    /// <param name="projected">What can be counted on, from <see cref="Projected"/>.</param>
    /// <returns>The quantity to order, rounded to something the vendor will ship.</returns>
    public static decimal Suggest(ReorderPolicy policy, decimal projected)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (projected > policy.ReorderPoint)
        {
            return 0m;
        }

        var wanted = policy.Kind switch
        {
            ReorderKind.UpToMaximum => policy.MaximumInventory - projected,
            _ => policy.ReorderQuantity,
        };

        if (wanted <= 0m)
        {
            return 0m;
        }

        // The vendor's minimum first, then the pack. A minimum of ten from a vendor selling in
        // twelves is an order of twelve, not ten: the pack has the last word because it is the
        // only one of the two that is a physical fact.
        if (policy.MinimumOrderQuantity > 0m && wanted < policy.MinimumOrderQuantity)
        {
            wanted = policy.MinimumOrderQuantity;
        }

        if (policy.OrderMultiple > 0m)
        {
            var packs = Math.Ceiling(wanted / policy.OrderMultiple);
            wanted = packs * policy.OrderMultiple;
        }

        return Math.Round(wanted, 5, MidpointRounding.AwayFromZero);
    }
}
