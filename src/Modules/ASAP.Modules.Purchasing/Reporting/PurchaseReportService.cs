using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Purchasing.Orders;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Purchasing.Reporting;

/// <summary>One order with goods still to come.</summary>
/// <param name="OrderNo">The order.</param>
/// <param name="VendorNo">Who it is with.</param>
/// <param name="VendorName">Their name as it stood when it was raised.</param>
/// <param name="OrderDate">When it was placed.</param>
/// <param name="ExpectedReceiptDate">When it was promised, where a date was given.</param>
/// <param name="DaysOverdue">
/// How late it is. Nought or less means not yet due; null means nobody promised a date.
/// </param>
/// <param name="QuantityOutstanding">How much has not arrived.</param>
/// <param name="ValueOutstanding">What that is worth at the ordered price.</param>
/// <param name="Status">Where the order stands.</param>
public readonly record struct OpenOrderRow(
    string OrderNo,
    string VendorNo,
    string VendorName,
    DateOnly OrderDate,
    DateOnly? ExpectedReceiptDate,
    int? DaysOverdue,
    decimal QuantityOutstanding,
    decimal ValueOutstanding,
    string Status);

/// <summary>How one vendor has actually behaved.</summary>
/// <param name="VendorNo">The vendor.</param>
/// <param name="VendorName">Their name.</param>
/// <param name="Deliveries">How many separate arrivals there were.</param>
/// <param name="OnTime">How many arrived on or before the promised date.</param>
/// <param name="Late">How many arrived after it.</param>
/// <param name="Unpromised">
/// How many arrived against an order that never carried a date. Reported rather than counted
/// either way, because a vendor who promises nothing must not come out looking punctual.
/// </param>
/// <param name="AverageDaysLate">
/// The average lateness of the late ones only, or null where none were late.
/// </param>
/// <param name="WorstDaysLate">The latest single delivery.</param>
/// <param name="ValueReceived">What arrived, at cost.</param>
public readonly record struct VendorPerformanceRow(
    string VendorNo,
    string VendorName,
    int Deliveries,
    int OnTime,
    int Late,
    int Unpromised,
    decimal? AverageDaysLate,
    int WorstDaysLate,
    decimal ValueReceived);

/// <summary>What was bought, grouped.</summary>
/// <param name="Key">The vendor number or item number, depending on how it was grouped.</param>
/// <param name="Name">What that is called.</param>
/// <param name="Quantity">How much arrived.</param>
/// <param name="Value">What it cost.</param>
/// <param name="Deliveries">How many arrivals made it up.</param>
public readonly record struct PurchaseAnalysisRow(
    string Key,
    string Name,
    decimal Quantity,
    decimal Value,
    int Deliveries);

/// <summary>
/// What is on order, how vendors have behaved, and what was spent.
/// </summary>
/// <remarks>
/// <para>
/// All three are read from what already happened rather than from a summary kept alongside it. A
/// delivery date is the posting date of the stock that arrived, and it is compared to the date the
/// order promised. Nothing is recorded twice, so nothing can disagree.
/// </para>
/// <para>
/// The judgment that matters here is what counts as a fair measure of a vendor. Two decisions are
/// deliberate and neither is obvious.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="clock">Supplies today, for working out what is overdue.</param>
public sealed class PurchaseReportService(AsapDbContext context, IClock clock)
{
    /// <summary>
    /// Orders with goods still to come, latest first.
    /// </summary>
    /// <param name="vendorNo">One vendor, or null for all of them.</param>
    /// <param name="overdueOnly">Whether to list only what is already late.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The open orders.</returns>
    /// <remarks>
    /// Cancelled and rejected orders are left out, because nothing is coming. An order still
    /// waiting for approval is included: nobody has told the vendor to send anything, but the
    /// buyer who raised it is still waiting for it and it is the honest answer to "what have we
    /// got outstanding".
    /// </remarks>
    public async Task<IReadOnlyList<OpenOrderRow>> OpenOrdersAsync(
        string? vendorNo = null,
        bool overdueOnly = false,
        CancellationToken cancellationToken = default)
    {
        var wanted = vendorNo?.Trim().ToUpperInvariant();
        var today = clock.Today;

        var orders = await context.Set<PurchaseOrder>()
            .AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.Status != PurchaseOrderStatus.Cancelled
                && o.Status != PurchaseOrderStatus.Rejected
                && o.Status != PurchaseOrderStatus.Invoiced
                && (wanted == null || o.VendorNo == wanted))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<OpenOrderRow>();

        foreach (var order in orders)
        {
            var outstanding = order.Lines.Sum(static l => l.OutstandingToReceive);

            if (outstanding <= 0m)
            {
                continue;
            }

            var overdue = order.ExpectedReceiptDate is { } promised
                ? today.DayNumber - promised.DayNumber
                : (int?)null;

            if (overdueOnly && overdue is not > 0)
            {
                continue;
            }

            rows.Add(new OpenOrderRow(
                order.No,
                order.VendorNo,
                order.VendorName,
                order.OrderDate,
                order.ExpectedReceiptDate,
                overdue,
                outstanding,
                order.Lines.Sum(static l => l.OutstandingToReceive * l.DirectUnitCost),
                order.Status.ToString()));
        }

        return
        [
            .. rows
                .OrderByDescending(static r => r.DaysOverdue ?? int.MinValue)
                .ThenBy(static r => r.OrderNo, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// How each vendor has actually behaved over a period.
    /// </summary>
    /// <param name="from">The first day counted.</param>
    /// <param name="to">The last day counted.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A row per vendor, worst average lateness first.</returns>
    /// <remarks>
    /// <para>
    /// Lateness is averaged over the late deliveries only. Averaging the early ones in would let a
    /// vendor who is a fortnight late half the time and a fortnight early the rest come out
    /// perfectly punctual, which is the opposite of what anybody wants to know about them: an
    /// erratic supplier is a worse supplier than a consistently slow one, because nothing can be
    /// planned around them.
    /// </para>
    /// <para>
    /// Deliveries against an order that never carried a promised date are counted separately and
    /// left out of both figures. Scoring them as on time would make a vendor who never commits to
    /// a date the best-performing one on the report.
    /// </para>
    /// <para>
    /// Each arrival counts once, not each line on it. A lorry that turns up on Tuesday with six
    /// items on it was one delivery either way round, and counting lines would make a vendor's
    /// record depend on how the buyer chose to split the order.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<VendorPerformanceRow>> VendorPerformanceAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var arrivals = await context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.SourceCode == "PURCH" && e.Quantity > 0
                && e.PostingDate >= from && e.PostingDate <= to
                && e.DocumentNo != null)
            .Select(static e => new { e.DocumentNo, e.PostingDate, e.ItemNo, e.Quantity })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (arrivals.Count == 0)
        {
            return [];
        }

        var orderNos = arrivals.Select(static a => a.DocumentNo!).Distinct().ToList();

        var orders = await context.Set<PurchaseOrder>()
            .AsNoTracking()
            .Where(o => orderNos.Contains(o.No))
            .Select(static o => new { o.No, o.VendorNo, o.VendorName, o.ExpectedReceiptDate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byOrder = orders.ToDictionary(static o => o.No, StringComparer.OrdinalIgnoreCase);

        var values = await ArrivalValuesAsync(from, to, cancellationToken).ConfigureAwait(false);

        // One arrival is one order on one day, however many lines came on it.
        var deliveries = arrivals
            .Select(a => new { a.DocumentNo, a.PostingDate })
            .Distinct()
            .ToList();

        var rows = new List<VendorPerformanceRow>();

        foreach (var group in deliveries.GroupBy(d => byOrder.GetValueOrDefault(d.DocumentNo!)?.VendorNo))
        {
            if (group.Key is not { Length: > 0 } vendor)
            {
                continue;
            }

            var onTime = 0;
            var late = 0;
            var unpromised = 0;
            var lateness = new List<int>();

            foreach (var delivery in group)
            {
                var order = byOrder[delivery.DocumentNo!];

                if (order.ExpectedReceiptDate is not { } promised)
                {
                    unpromised++;
                    continue;
                }

                var days = delivery.PostingDate.DayNumber - promised.DayNumber;

                if (days > 0)
                {
                    late++;
                    lateness.Add(days);
                }
                else
                {
                    onTime++;
                }
            }

            var received = arrivals
                .Where(a => byOrder.GetValueOrDefault(a.DocumentNo!)?.VendorNo == vendor)
                .Sum(a => values.GetValueOrDefault((a.DocumentNo!, a.ItemNo, a.PostingDate)));

            rows.Add(new VendorPerformanceRow(
                vendor,
                byOrder.Values.First(o => o.VendorNo == vendor).VendorName,
                group.Count(),
                onTime,
                late,
                unpromised,
                lateness.Count == 0 ? null : Math.Round((decimal)lateness.Average(), 1, MidpointRounding.AwayFromZero),
                lateness.Count == 0 ? 0 : lateness.Max(),
                received));
        }

        return
        [
            .. rows
                .OrderByDescending(static r => r.AverageDaysLate ?? -1m)
                .ThenBy(static r => r.VendorNo, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// What was bought over a period, by vendor or by item.
    /// </summary>
    /// <param name="from">The first day counted.</param>
    /// <param name="to">The last day counted.</param>
    /// <param name="byItem">True to group by item, false to group by vendor.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A row per group, biggest spend first.</returns>
    /// <remarks>
    /// Read from what arrived rather than from what was ordered. An order is a statement of intent
    /// and a good deal of it never turns into anything; spend is what actually came through the
    /// door and got valued.
    /// </remarks>
    public async Task<IReadOnlyList<PurchaseAnalysisRow>> AnalysisAsync(
        DateOnly from,
        DateOnly to,
        bool byItem = false,
        CancellationToken cancellationToken = default)
    {
        var arrivals = await context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.SourceCode == "PURCH" && e.Quantity > 0
                && e.PostingDate >= from && e.PostingDate <= to
                && e.DocumentNo != null)
            .Select(static e => new { e.DocumentNo, e.ItemNo, e.PostingDate, e.Quantity })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (arrivals.Count == 0)
        {
            return [];
        }

        var values = await ArrivalValuesAsync(from, to, cancellationToken).ConfigureAwait(false);

        if (byItem)
        {
            var items = await context.Set<Inventory.Items.Item>()
                .AsNoTracking()
                .Select(static i => new { i.No, i.Description })
                .ToDictionaryAsync(static i => i.No, static i => i.Description, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);

            return
            [
                .. arrivals
                    .GroupBy(static a => a.ItemNo)
                    .Select(g => new PurchaseAnalysisRow(
                        g.Key,
                        items.GetValueOrDefault(g.Key) ?? g.Key,
                        g.Sum(static a => a.Quantity),
                        g.Sum(a => values.GetValueOrDefault((a.DocumentNo!, a.ItemNo, a.PostingDate))),
                        g.Select(static a => new { a.DocumentNo, a.PostingDate }).Distinct().Count()))
                    .OrderByDescending(static r => r.Value),
            ];
        }

        var orderNos = arrivals.Select(static a => a.DocumentNo!).Distinct().ToList();

        var orders = await context.Set<PurchaseOrder>()
            .AsNoTracking()
            .Where(o => orderNos.Contains(o.No))
            .Select(static o => new { o.No, o.VendorNo, o.VendorName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byOrder = orders.ToDictionary(static o => o.No, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. arrivals
                .Where(a => byOrder.ContainsKey(a.DocumentNo!))
                .GroupBy(a => byOrder[a.DocumentNo!].VendorNo)
                .Select(g => new PurchaseAnalysisRow(
                    g.Key,
                    byOrder[g.First().DocumentNo!].VendorName,
                    g.Sum(static a => a.Quantity),
                    g.Sum(a => values.GetValueOrDefault((a.DocumentNo!, a.ItemNo, a.PostingDate))),
                    g.Select(static a => new { a.DocumentNo, a.PostingDate }).Distinct().Count()))
                .OrderByDescending(static r => r.Value),
        ];
    }

    /// <summary>
    /// What each arrival was worth, keyed by the order, item and day it came in on.
    /// </summary>
    /// <remarks>
    /// From the value entries rather than the ordered price, because what a receipt was worth is
    /// what the costing engine said -- freight landed on it afterwards included. Multiplying an
    /// ordered unit cost by a quantity would report the price somebody agreed rather than the cost
    /// the company carries.
    /// </remarks>
    private async Task<Dictionary<(string OrderNo, string ItemNo, DateOnly Day), decimal>> ArrivalValuesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var totals = await context.Set<ValueEntry>()
            .AsNoTracking()
            .Where(v => v.ItemLedgerEntry != null
                && v.ItemLedgerEntry.SourceCode == "PURCH"
                && v.ItemLedgerEntry.Quantity > 0
                && v.ItemLedgerEntry.PostingDate >= from
                && v.ItemLedgerEntry.PostingDate <= to
                && v.ItemLedgerEntry.DocumentNo != null)
            .GroupBy(static v => new
            {
                v.ItemLedgerEntry!.DocumentNo,
                v.ItemLedgerEntry.ItemNo,
                v.ItemLedgerEntry.PostingDate,
            })
            .Select(static g => new
            {
                g.Key.DocumentNo,
                g.Key.ItemNo,
                g.Key.PostingDate,
                Cost = g.Sum(static v => v.CostAmount),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return totals.ToDictionary(
            static t => (t.DocumentNo!, t.ItemNo, t.PostingDate),
            static t => t.Cost);
    }
}
