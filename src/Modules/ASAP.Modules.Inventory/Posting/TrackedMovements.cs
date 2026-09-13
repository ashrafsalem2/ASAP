using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;

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

    /// <summary>
    /// Turns every movement on a document into the units it moves, by the line each came from.
    /// </summary>
    /// <param name="movements">The document's movements, each carrying its line number.</param>
    /// <param name="trackingByLine">The serials or lot keyed for each line. Lines not named move as they are.</param>
    /// <param name="messages">Renders a refusal.</param>
    /// <returns>The movements unit by unit, or a refusal for every line whose numbers do not match its quantity.</returns>
    /// <remarks>
    /// Several numbers that do not match the quantity are refused here, where the line is still
    /// known. Whether the item needs numbers at all, and by serial or lot, is posting's to decide.
    /// </remarks>
    public static Result<List<StockMovementRequest>> SplitLines(
        IReadOnlyList<StockMovementRequest> movements,
        IReadOnlyDictionary<int, IReadOnlyList<string>> trackingByLine,
        IMessageCatalog messages)
    {
        ArgumentNullException.ThrowIfNull(movements);
        ArgumentNullException.ThrowIfNull(trackingByLine);
        ArgumentNullException.ThrowIfNull(messages);

        var units = new List<StockMovementRequest>(movements.Count);
        var mismatched = new List<AsapMessage>();

        foreach (var movement in movements)
        {
            var numbers = movement.LineNo is { } lineNo ? trackingByLine.GetValueOrDefault(lineNo) : null;
            var split = Split(movement, numbers);

            if (split is null)
            {
                mismatched.Add(messages.Render(
                    InventoryMessages.TrackingNumbersDoNotMatch,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["LineNo"] = movement.LineNo,
                        ["ItemNo"] = movement.ItemNo,
                        ["Quantity"] = Math.Abs(movement.Quantity),
                        ["Count"] = numbers!.Count(static n => !string.IsNullOrWhiteSpace(n)),
                        ["Tracking"] = "serial",
                    },
                    MessageTarget.OnField($"Lines[{movement.LineNo}]")));

                continue;
            }

            units.AddRange(split);
        }

        return mismatched.Count > 0
            ? Result<List<StockMovementRequest>>.Failure(mismatched)
            : Result<List<StockMovementRequest>>.Success(units);
    }

    /// <summary>The numbers keyed on each line of a request, by line number, leaving out lines that named none.</summary>
    /// <typeparam name="T">The kind of line.</typeparam>
    /// <param name="lines">What the caller sent.</param>
    /// <param name="lineNo">Reads a line's number.</param>
    /// <param name="trackingNos">Reads the numbers keyed on it.</param>
    /// <returns>The numbers by line.</returns>
    public static Dictionary<int, IReadOnlyList<string>> ByLine<T>(
        IEnumerable<T>? lines,
        Func<T, int> lineNo,
        Func<T, IReadOnlyList<string>?> trackingNos)
    {
        ArgumentNullException.ThrowIfNull(lineNo);
        ArgumentNullException.ThrowIfNull(trackingNos);

        var byLine = new Dictionary<int, IReadOnlyList<string>>();

        foreach (var line in lines ?? [])
        {
            if (trackingNos(line) is { Count: > 0 } numbers)
            {
                byLine[lineNo(line)] = numbers;
            }
        }

        return byLine;
    }
}
