namespace ASAP.Modules.Inventory.Posting;

/// <summary>
/// Turns one document line and the serial or lot numbers given for it into the movements to post.
/// </summary>
/// <remarks>
/// <para>
/// A receipt line for three cars is three movements, one per chassis number, because each car
/// carries its own cost. A line for a lot is one movement with the lot on it. Documents stay
/// written in lines — a purchase order for three cars is one line — and this is the one place
/// that knows how a line becomes units.
/// </para>
/// <para>
/// Whether the item is tracked at all, and by serial or lot, is not decided here. Posting knows
/// the item and refuses a serial moving more than one unit or a tracked item named without a
/// number. This only refuses what it can see without the item: several numbers that do not add
/// up to the quantity.
/// </para>
/// </remarks>
public static class TrackedMovements
{
    /// <summary>
    /// The movements for one line.
    /// </summary>
    /// <param name="movement">The line as a single movement, quantity signed.</param>
    /// <param name="trackingNos">The serials or lot given for it, or null.</param>
    /// <returns>
    /// The movements to post, or null where several numbers were given and they do not match the
    /// quantity one for one.
    /// </returns>
    public static IReadOnlyList<StockMovementRequest>? Split(
        StockMovementRequest movement,
        IReadOnlyList<string>? trackingNos)
    {
        ArgumentNullException.ThrowIfNull(movement);

        var numbers = (trackingNos ?? [])
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Select(static n => n.Trim().ToUpperInvariant())
            .ToList();

        if (numbers.Count == 0)
        {
            return [movement];
        }

        if (numbers.Count == 1)
        {
            return [movement with { TrackingNo = numbers[0] }];
        }

        var units = Math.Abs(movement.Quantity);

        if (units != numbers.Count)
        {
            return null;
        }

        var sign = Math.Sign(movement.Quantity);
        var share = Math.Round(movement.SalesAmount / numbers.Count, 2, MidpointRounding.AwayFromZero);
        var given = 0m;
        var split = new List<StockMovementRequest>(numbers.Count);

        for (var index = 0; index < numbers.Count; index++)
        {
            // The last unit takes whatever rounding left, so the parts add back to what the line
            // sold for rather than a halala short.
            var sales = index == numbers.Count - 1 ? movement.SalesAmount - given : share;
            given += sales;

            split.Add(movement with
            {
                Quantity = sign,
                SalesAmount = sales,
                TrackingNo = numbers[index],
            });
        }

        return split;
    }
}
