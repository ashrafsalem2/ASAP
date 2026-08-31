using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Reservations;
using ASAP.Modules.Purchasing.Orders;
using ASAP.Modules.Purchasing.Requisitions;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Purchasing.Replenishment;

/// <summary>
/// One line of the worksheet: what to order, and every figure that produced it.
/// </summary>
/// <param name="ItemNo">The item.</param>
/// <param name="ItemName">What it is called.</param>
/// <param name="LocationCode">Where it is wanted.</param>
/// <param name="VariantCode">Which variant, where the policy names one.</param>
/// <param name="QuantityOnHand">What is on the shelf.</param>
/// <param name="QuantityReserved">What is promised to somebody else.</param>
/// <param name="QuantityOnOrder">What is bought and not yet received.</param>
/// <param name="Projected">What can be counted on: on hand, less reserved, plus on order.</param>
/// <param name="ReorderPoint">The level that triggered it.</param>
/// <param name="Kind">Whether the quantity is fixed or measured against a maximum.</param>
/// <param name="SuggestedQuantity">How much to order.</param>
/// <param name="OrderByDate">When it has to be ordered to arrive by the needed date.</param>
/// <param name="VendorNo">The vendor the policy names, where it names one.</param>
/// <param name="LastDirectCost">What it cost last time, as an estimate.</param>
public readonly record struct ReplenishmentLine(
    string ItemNo,
    string ItemName,
    string LocationCode,
    string? VariantCode,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal QuantityOnOrder,
    decimal Projected,
    decimal ReorderPoint,
    ReorderKind Kind,
    decimal SuggestedQuantity,
    DateOnly OrderByDate,
    string? VendorNo,
    decimal LastDirectCost);

/// <summary>
/// Works out what needs buying, and turns the answer into a requisition.
/// </summary>
/// <remarks>
/// <para>
/// It lives in Purchasing rather than Inventory because it needs to know what is already on
/// order, and only Purchasing knows that. The rule about <em>when</em> to order is a fact about
/// stock and stays in Inventory as the policy; the run that reads it is a buying job.
/// </para>
/// <para>
/// Counting what is on order is the whole difficulty. A worksheet that looks only at the shelf
/// suggests the same order every morning until the goods arrive, and nobody notices until four
/// times what was wanted turns up on the same lorry. Every figure that went into a suggestion
/// comes back with it for that reason: a number nobody can reproduce is a number nobody acts on.
/// </para>
/// <para>
/// It suggests and stops. Nothing is committed to a vendor here — the lines become a requisition,
/// which goes through whatever approval the amount calls for, exactly as one typed by hand does.
/// An automatic run that placed its own orders would be a rule nobody wrote spending money
/// nobody approved.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="requisitions">Raises the requisition the worksheet becomes.</param>
/// <param name="clock">Says what today is.</param>
public sealed class ReplenishmentService(
    AsapDbContext context,
    PurchaseRequisitionService requisitions,
    IClock clock)
{
    /// <summary>
    /// What needs ordering, as at today.
    /// </summary>
    /// <param name="locationCode">One location, or null for every one with policies.</param>
    /// <param name="includeSatisfied">
    /// Whether to return the items that need nothing. Useful for showing somebody that a policy
    /// exists and is simply not triggered, which is otherwise indistinguishable from no policy.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One line per policy that has something to say.</returns>
    public async Task<IReadOnlyList<ReplenishmentLine>> SuggestAsync(
        string? locationCode = null,
        bool includeSatisfied = false,
        CancellationToken cancellationToken = default)
    {
        var policies = await Policies(locationCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (policies.Count == 0)
        {
            return [];
        }

        var itemNos = policies.Select(static p => p.ItemNo).Distinct().ToList();

        var items = await context.Set<Item>()
            .AsNoTracking()
            .Where(i => itemNos.Contains(i.No))
            .ToDictionaryAsync(static i => i.No, cancellationToken)
            .ConfigureAwait(false);

        var onHand = await OnHandAsync(itemNos, cancellationToken).ConfigureAwait(false);
        var reserved = await ReservedAsync(itemNos, cancellationToken).ConfigureAwait(false);
        var onOrder = await OnOrderAsync(itemNos, cancellationToken).ConfigureAwait(false);

        var today = clock.Today;
        var lines = new List<ReplenishmentLine>();

        foreach (var policy in policies)
        {
            var key = (policy.ItemNo, policy.LocationCode);

            var hand = onHand.GetValueOrDefault(key);
            var held = reserved.GetValueOrDefault(key);
            var coming = onOrder.GetValueOrDefault(key);

            var projected = Reordering.Projected(hand, held, coming);
            var suggested = Reordering.Suggest(policy, projected);

            if (suggested <= 0m && !includeSatisfied)
            {
                continue;
            }

            var item = items.GetValueOrDefault(policy.ItemNo);

            lines.Add(new ReplenishmentLine(
                policy.ItemNo,
                item?.Description ?? policy.ItemNo,
                policy.LocationCode,
                policy.VariantCode,
                hand,
                held,
                coming,
                projected,
                policy.ReorderPoint,
                policy.Kind,
                suggested,

                // Dated backwards from when it is wanted rather than forwards from today: the
                // useful question at a worksheet is whether it is already too late, and an order
                // date in the past says so plainly.
                today,
                policy.VendorNo,
                item?.LastDirectCost ?? 0m));
        }

        return lines
            .OrderByDescending(static l => l.SuggestedQuantity > 0m)
            .ThenBy(static l => l.LocationCode)
            .ThenBy(static l => l.ItemNo)
            .ToList();
    }

    /// <summary>
    /// Turns the suggestions into a requisition, so they go through approval like any other.
    /// </summary>
    /// <param name="lines">The suggestions being taken, which may be a subset of the run.</param>
    /// <param name="locationCode">Where the goods are wanted.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requisition, or every reason it was refused.</returns>
    public async Task<Result<PurchaseRequisition>> RequisitionAsync(
        IReadOnlyList<ReplenishmentLine> lines,
        string? locationCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var wanted = lines.Where(static l => l.SuggestedQuantity > 0m).ToList();

        var requested = wanted
            .Select(static l => new PurchaseRequisitionLineRequest(
                PurchaseLineType.Item,
                l.ItemNo,
                l.SuggestedQuantity,
                l.LastDirectCost,
                l.ItemName,
                l.LocationCode,
                l.VariantCode,
                l.VendorNo))
            .ToList();

        return await requisitions
            .CreateAsync(
                requested,
                locationCode,
                neededByDate: null,
                description: "Replenishment",

                // The justification is the arithmetic. Somebody approving this did not run the
                // worksheet and should not have to take its word for the quantity.
                justification: Justification(wanted),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Justification(IReadOnlyList<ReplenishmentLine> lines)
        => lines.Count == 0
            ? "Raised from the replenishment worksheet."
            : "Raised from the replenishment worksheet. "
              + string.Join(
                  " ",
                  lines.Select(static l =>
                      $"{l.ItemNo} at {l.LocationCode}: {l.Projected:0.#####} against a reorder "
                      + $"point of {l.ReorderPoint:0.#####}."));

    private IQueryable<ReorderPolicy> Policies(string? locationCode)
    {
        var query = context.Set<ReorderPolicy>()
            .AsNoTracking()
            .Where(static p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(locationCode))
        {
            var code = locationCode.Trim().ToUpperInvariant();
            query = query.Where(p => p.LocationCode == code);
        }

        return query.OrderBy(p => p.LocationCode).ThenBy(p => p.ItemNo);
    }

    private async Task<Dictionary<(string ItemNo, string LocationCode), decimal>> OnHandAsync(
        IReadOnlyList<string> itemNos,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => itemNos.Contains(e.ItemNo))
            .GroupBy(static e => new { e.ItemNo, e.LocationCode })
            .Select(static g => new
            {
                g.Key.ItemNo,
                g.Key.LocationCode,
                Quantity = g.Sum(static e => e.Quantity),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            static r => (r.ItemNo, r.LocationCode),
            static r => r.Quantity);
    }

    private async Task<Dictionary<(string ItemNo, string LocationCode), decimal>> ReservedAsync(
        IReadOnlyList<string> itemNos,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<StockReservation>()
            .AsNoTracking()
            .Where(r => itemNos.Contains(r.ItemNo) && r.QuantityOutstanding > 0m)
            .GroupBy(static r => new { r.ItemNo, r.LocationCode })
            .Select(static g => new
            {
                g.Key.ItemNo,
                g.Key.LocationCode,
                Quantity = g.Sum(static r => r.QuantityOutstanding),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            static r => (r.ItemNo, r.LocationCode),
            static r => r.Quantity);
    }

    /// <summary>
    /// What is bought and not yet received, by item and place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cancelled and rejected orders are left out, and so are lines already fully received. What
    /// remains is what a lorry is genuinely bringing. An order still waiting for approval counts:
    /// it is a request somebody has already made, and suggesting the same goods again would put
    /// two of them in front of the approver.
    /// </para>
    /// <para>
    /// Getting this wrong in either direction is expensive. Count too little and the worksheet
    /// orders it all again; count too much and it never orders at all, and the shelf goes empty
    /// while the figures look healthy.
    /// </para>
    /// </remarks>
    private async Task<Dictionary<(string ItemNo, string LocationCode), decimal>> OnOrderAsync(
        IReadOnlyList<string> itemNos,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(l =>
                l.Type == PurchaseLineType.Item
                && l.ItemNo != null
                && itemNos.Contains(l.ItemNo)
                && l.Quantity > l.QuantityReceived
                && l.PurchaseOrder!.Status != PurchaseOrderStatus.Cancelled
                && l.PurchaseOrder!.Status != PurchaseOrderStatus.Rejected)
            .Select(static l => new
            {
                ItemNo = l.ItemNo!,
                LocationCode = l.LocationCode ?? l.PurchaseOrder!.LocationCode,
                Outstanding = l.Quantity - l.QuantityReceived,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Where(static r => r.LocationCode is not null)
            .GroupBy(static r => (r.ItemNo, LocationCode: r.LocationCode!))
            .ToDictionary(
                static g => g.Key,
                static g => g.Sum(static r => r.Outstanding));
    }
}
