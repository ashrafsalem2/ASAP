namespace ASAP.Modules.Inventory.Costing;

/// <summary>
/// What a cost layer is worth per unit.
/// </summary>
/// <remarks>
/// Held in one place because two things need the answer and they must never disagree: the posting
/// engine, working out what to charge when goods leave a layer, and the ageing report, working out
/// what the stock still sitting in it is worth. A second copy of this arithmetic would drift the
/// first time somebody changed one of them, and the symptom would be a valuation that no longer
/// ties to the inventory account.
/// </remarks>
public static class LayerCosting
{
    /// <summary>
    /// Works out a layer's unit cost from the value entries posted against it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two kinds of value entry, added together. The ordinary ones carry both a cost and the
    /// quantity it covers, and their unit cost is one divided by the other -- what the goods were
    /// bought for, freight included.
    /// </para>
    /// <para>
    /// A revaluation carries a cost and no quantity, because nothing moved. Its unit cost is the
    /// amount each remaining unit was written up or down by, and it is <em>added</em> rather than
    /// averaged in. That distinction is the whole reason a revaluation survives: averaging it over
    /// the receipt's original quantity would spread a write-down across units sold months ago, and
    /// the layer would drift back towards its old cost as it drained -- a revaluation that quietly
    /// undoes itself as the stock sells.
    /// </para>
    /// </remarks>
    /// <param name="costOfMovedUnits">
    /// The cost on value entries that carried a quantity.
    /// </param>
    /// <param name="quantity">The quantity those entries covered.</param>
    /// <param name="revaluedPerUnit">
    /// The unit cost on value entries that carried no quantity, added together.
    /// </param>
    /// <returns>What one unit still in this layer is worth.</returns>
    public static decimal UnitCost(decimal costOfMovedUnits, decimal quantity, decimal revaluedPerUnit)
        => quantity == 0m
            ? Math.Round(revaluedPerUnit, 5, MidpointRounding.AwayFromZero)
            : Math.Round((costOfMovedUnits / quantity) + revaluedPerUnit, 5, MidpointRounding.AwayFromZero);
}
