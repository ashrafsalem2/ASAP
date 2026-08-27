using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Sales.Orders;

/// <summary>How much of one line went out.</summary>
/// <param name="LineNo">The order line.</param>
/// <param name="Quantity">How much shipped. Always positive.</param>
public readonly record struct ShipmentLineRequest(int LineNo, decimal Quantity);

/// <summary>What a shipment moved.</summary>
/// <param name="OrderNo">The order shipped against.</param>
/// <param name="TransactionNo">The transaction the movements were posted under.</param>
/// <param name="LineCount">How many lines shipped.</param>
/// <param name="CostAmount">What the goods cost, charged to cost of sales.</param>
/// <param name="Status">Where the order stands now.</param>
public readonly record struct SalesShipment(
    string OrderNo,
    long TransactionNo,
    int LineCount,
    decimal CostAmount,
    SalesOrderStatus Status);

/// <summary>
/// Records that goods have left.
/// </summary>
/// <remarks>
/// <para>
/// A shipment moves stock out and charges what it cost to cost of sales. The figure it posts has
/// nothing to do with the price on the order: the customer pays what was agreed, and the goods
/// carry whatever the costing engine says they cost, which is the whole reason cost and price are
/// held apart. Confusing the two produces a margin report that agrees with itself and describes
/// nothing.
/// </para>
/// <para>
/// It is also where availability is decided, and deliberately not before. The order made a promise;
/// whether it can be kept depends on what is on the shelf at the moment somebody reaches for it,
/// and that is a question only the posting engine can answer. It refuses, or permits and values the
/// shortfall as an estimate, according to the company's negative-stock setting — the same rules as
/// every other issue of stock, because a sale is not a special case.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="orders">Loads the order.</param>
/// <param name="posting">Moves and values the stock.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="overrides">Records every protection this shipment pushed past.</param>
/// <param name="setup">Supplies the negative-stock policy.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records shipments.</param>
public sealed class SalesShipmentService(
    AsapDbContext context,
    SalesOrderService orders,
    StockPostingService posting,
    IMessageCatalog messages,
    OverrideAuditor overrides,
    ISetupService setup,
    IClock clock,
    ILogger<SalesShipmentService> logger)
{
    /// <summary>
    /// Ships goods against an order.
    /// </summary>
    /// <param name="orderNo">The order being shipped.</param>
    /// <param name="lines">
    /// How much of each line went, or null to ship everything still outstanding.
    /// </param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What moved, or every reason it was refused.</returns>
    public async Task<Result<SalesShipment>> ShipAsync(
        string orderNo,
        IReadOnlyList<ShipmentLineRequest>? lines = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        var order = await orders.LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result<SalesShipment>.FailureFrom(orders.NotFound(orderNo));
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OrderNo"] = order.No,
            ["Status"] = order.Status.ToString(),
        };

        var going = Going(order, lines);

        if (going.Count == 0)
        {
            return Result<SalesShipment>.Failure(
                messages.Render(SalesMessages.NothingToShip, arguments));
        }

        var found = CheckGoing(order, going, heldOverridePermissions);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<SalesShipment>.Failure(found);
        }

        var movements = going
            .Where(static g => g.Line.Type is SalesLineType.Item)
            .Select(g => new StockMovementRequest(
                g.Line.ItemNo!,
                g.Line.LocationCode ?? order.LocationCode!,

                // Negative: stock is leaving. The cost is worked out by the engine from what is on
                // hand, so nothing here says what it is worth.
                -g.Quantity,
                UnitCost: 0m,
                ItemLedgerEntryType.Sale,

                // What it sold for, carried onto the entry so a margin report can be built from
                // the item ledger without joining back to a sales document.
                SalesAmount: g.Quantity * g.Line.NetUnitPrice))
            .ToList();

        var transactionNo = 0L;
        var cost = 0m;

        if (movements.Count > 0)
        {
            var allowsNegative = await setup
                .GetAsync<bool>(
                    $"{Modules.Inventory.InventoryModule.Id}.Costing.AllowNegativeInventory",
                    cancellationToken)
                .ConfigureAwait(false);

            var posted = await posting
                .PostAsync(
                    movements,
                    clock.Today,
                    "SALES",
                    order.No,
                    allowsNegative,
                    heldOverridePermissions,
                    overrideReason,
                    cancellationToken)
                .ConfigureAwait(false);

            if (posted.Failed)
            {
                return Result<SalesShipment>.FailureFrom(posted);
            }

            transactionNo = posted.Value.TransactionNo;

            // Negative, because stock went down. Reported as a positive cost, which is how anybody
            // reading a shipment thinks about it.
            cost = -posted.Value.CostAmount;
            found.AddRange(posted.Messages);
        }

        foreach (var (line, quantity) in going)
        {
            line.QuantityShipped += quantity;
        }

        order.Status = order.HasOutstandingShipment
            ? SalesOrderStatus.PartiallyShipped
            : SalesOrderStatus.Shipped;

        overrides.Record(found, "Sales.Shipment", order.No, overrideReason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Shipped {LineCount} line(s) against sales order {OrderNo}, now {Status}.",
            going.Count,
            order.No,
            order.Status);

        return Result<SalesShipment>.Success(
            new SalesShipment(order.No, transactionNo, going.Count, cost, order.Status),
            found);
    }

    /// <summary>What is going out: what the caller said, or everything outstanding.</summary>
    private static List<(SalesOrderLine Line, decimal Quantity)> Going(
        SalesOrder order,
        IReadOnlyList<ShipmentLineRequest>? lines)
    {
        if (lines is null)
        {
            return
            [
                .. order.Lines
                    .Where(static l => l.OutstandingToShip > 0)
                    .OrderBy(static l => l.LineNo)
                    .Select(static l => (l, l.OutstandingToShip)),
            ];
        }

        var byLineNo = order.Lines.ToDictionary(static l => l.LineNo);

        return
        [
            .. lines
                .Where(r => r.Quantity > 0 && byLineNo.ContainsKey(r.LineNo))
                .Select(r => (byLineNo[r.LineNo], r.Quantity)),
        ];
    }

    private List<AsapMessage> CheckGoing(
        SalesOrder order,
        List<(SalesOrderLine Line, decimal Quantity)> going,
        IReadOnlySet<string>? held)
    {
        var found = new List<AsapMessage>();

        foreach (var (line, quantity) in going.Where(g => g.Quantity > g.Line.OutstandingToShip))
        {
            var rendered = messages.Render(
                SalesMessages.OverShipment,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OrderNo"] = order.No,
                    ["LineNo"] = line.LineNo,
                    ["ItemNo"] = line.ItemNo ?? line.AccountNo,
                    ["Shipped"] = quantity,
                    ["Outstanding"] = line.OutstandingToShip,
                    ["Ordered"] = line.Quantity,
                },
                MessageTarget.OnField($"Lines[{line.LineNo}]"));

            found.Add(
                rendered.OverridePermission is { } permission && held?.Contains(permission) == true
                    ? messages.AsOverridden(rendered)
                    : rendered);
        }

        return found;
    }
}
