using ASAP.Modules.Inventory.Costing;
using ASAP.Platform.Kernel.Events;

namespace ASAP.Modules.Inventory.Events;

/// <summary>
/// Raised after ASAP has checked a stock movement and before anything is written.
/// </summary>
/// <remarks>
/// The extension point for rules the core knows nothing about: a minimum shelf quantity that must
/// never be sold below, a customer allocation that has to be respected, a hazardous good that only
/// certain staff may issue. A subscriber that objects stops the movement, and the user sees the
/// objection as an ordinary ASAP message.
/// </remarks>
public sealed class StockPosting : VetoableEvent
{
    /// <inheritdoc />
    public override string EventName => "Inventory.StockPosting";

    /// <summary>The movements about to be written, with items and locations resolved.</summary>
    public required IReadOnlyList<MovementView> Movements { get; init; }

    /// <summary>The document behind them.</summary>
    public string? DocumentNo { get; init; }

    /// <summary>The date they will be reported in.</summary>
    public DateOnly PostingDate { get; init; }
}

/// <summary>
/// Raised once stock has moved and the transaction has committed.
/// </summary>
/// <remarks>
/// Delivered through the outbox. This is what Finance subscribes to in order to post the value of
/// the movement into the general ledger -- the two modules meeting through an event rather than a
/// reference, which is what lets a customer buy Inventory without Finance and get a stock system
/// that simply does not post to a ledger.
/// </remarks>
public sealed class StockPosted : IIntegrationEvent
{
    /// <inheritdoc />
    public string EventName => "Inventory.StockPosted";

    /// <inheritdoc />
    public DateTime OccurredAtUtc { get; init; }

    /// <summary>The transaction grouping every entry the posting wrote.</summary>
    public long TransactionNo { get; init; }

    /// <summary>The document behind it.</summary>
    public string? DocumentNo { get; init; }

    /// <summary>The date it is reported in.</summary>
    public DateOnly PostingDate { get; init; }

    /// <summary>How many item ledger entries were written.</summary>
    public int EntryCount { get; init; }

    /// <summary>
    /// The total change in the value of stock. Positive on a receipt, negative on an issue.
    /// </summary>
    public decimal CostAmount { get; init; }

    /// <summary>
    /// How much of that is still an estimate because stock went below zero.
    /// </summary>
    /// <remarks>
    /// Carried on the event so a subscriber posting to the general ledger can leave it out. An
    /// estimate that reached the inventory account would put the ledger out of step with the stock
    /// valuation by exactly the amount nobody has confirmed yet.
    /// </remarks>
    public decimal EstimatedCostAmount { get; init; }

    /// <summary>Where the movement came from, for example <c>POS</c> or <c>PURCH</c>.</summary>
    public required string SourceCode { get; init; }
}

/// <summary>
/// Raised when an estimated cost has been settled against what the goods really cost.
/// </summary>
/// <remarks>
/// Separate from <see cref="StockPosted"/> because it happens later, often days later, and
/// produces a general ledger posting of its own. A subscriber that treated the two alike would
/// post the settlement as though it were a fresh movement and double the cost.
/// </remarks>
public sealed class StockCostSettled : IIntegrationEvent
{
    /// <inheritdoc />
    public string EventName => "Inventory.StockCostSettled";

    /// <inheritdoc />
    public DateTime OccurredAtUtc { get; init; }

    /// <summary>The transaction grouping the settlement entries.</summary>
    public long TransactionNo { get; init; }

    /// <summary>The item whose cost was settled.</summary>
    public required string ItemNo { get; init; }

    /// <summary>How many units were settled.</summary>
    public decimal Quantity { get; init; }

    /// <summary>
    /// The correction posted. Signed so that adding it to what was already booked leaves the
    /// truth, and zero settlements are never raised at all.
    /// </summary>
    public decimal CostCorrection { get; init; }
}
