using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Sales.Orders;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Sales.Reporting;

/// <summary>What one line of a margin report came to.</summary>
/// <param name="Key">The item number or customer number, depending on how it was grouped.</param>
/// <param name="Name">What that is called.</param>
/// <param name="Quantity">How much went out.</param>
/// <param name="Revenue">What it sold for, before tax.</param>
/// <param name="Cost">What the goods cost.</param>
/// <param name="Margin">Revenue less cost.</param>
/// <param name="MarginPercent">
/// The margin as a percentage of revenue, or null where nothing was sold -- a margin on nought
/// revenue is not nought, it is a question with no answer, and printing nought would be a lie a
/// spreadsheet then averages.
/// </param>
/// <param name="EstimatedCost">
/// How much of the cost is still an estimate awaiting settlement. Anything above nought means this
/// margin will move.
/// </param>
public readonly record struct MarginRow(
    string Key,
    string Name,
    decimal Quantity,
    decimal Revenue,
    decimal Cost,
    decimal Margin,
    decimal? MarginPercent,
    decimal EstimatedCost);

/// <summary>One order with goods still to go out.</summary>
/// <param name="OrderNo">The order.</param>
/// <param name="CustomerNo">Who it is for.</param>
/// <param name="CustomerName">Their name as it stood when it was taken.</param>
/// <param name="OrderDate">When it was taken.</param>
/// <param name="RequestedDeliveryDate">When it was promised, where a date was given.</param>
/// <param name="DaysOverdue">How late it is. Null means nobody promised a date.</param>
/// <param name="QuantityOutstanding">How much has not shipped.</param>
/// <param name="ValueOutstanding">What that is worth at the agreed price.</param>
/// <param name="Status">Where the order stands.</param>
public readonly record struct OpenSalesOrderRow(
    string OrderNo,
    string CustomerNo,
    string CustomerName,
    DateOnly OrderDate,
    DateOnly? RequestedDeliveryDate,
    int? DaysOverdue,
    decimal QuantityOutstanding,
    decimal ValueOutstanding,
    string Status);

/// <summary>
/// What was sold, what it cost, and what is still to go out.
/// </summary>
/// <remarks>
/// <para>
/// Margin is read from the item ledger rather than from sales documents, which is why every
/// outbound value entry carries what the goods sold for alongside what they cost. One consequence
/// is worth stating: a sale at a till and a sale on an invoice are the same rows here, so the
/// margin report cannot tell which door a sale came through any more than the P&amp;L can. That is
/// the point of it.
/// </para>
/// <para>
/// The judgment in this report is what to do about cost that is not settled. A sale made from
/// stock that had not arrived is valued at an estimate, and its margin is provisional until the
/// goods are received and the settlement runs. Reporting that as a fact would be a figure somebody
/// acts on and it changes underneath them, so every row says how much of its cost is still in
/// doubt.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="clock">Supplies today, for working out what is overdue.</param>
/// <param name="parties">Says who each document was with, one implementation per module that owns any.</param>
public sealed class SalesReportService(
    AsapDbContext context,
    IClock clock,
    IEnumerable<ASAP.Platform.Kernel.Documents.IDocumentParties> parties)
{
    /// <summary>
    /// Revenue, cost and margin by item.
    /// </summary>
    /// <param name="from">The first day counted.</param>
    /// <param name="to">The last day counted.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A row per item, thinnest margin first.</returns>
    /// <remarks>
    /// Thinnest first, because a margin report is read to find the problems. The items making
    /// money need no attention and sorting them to the top would bury the ones losing it.
    /// </remarks>
    public async Task<IReadOnlyList<MarginRow>> MarginByItemAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var sales = await SoldAsync(from, to, cancellationToken).ConfigureAwait(false);

        if (sales.Count == 0)
        {
            return [];
        }

        var names = await context.Set<Inventory.Items.Item>()
            .AsNoTracking()
            .Select(static i => new { i.No, i.Description })
            .ToDictionaryAsync(static i => i.No, static i => i.Description, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. sales
                .GroupBy(static s => s.ItemNo)
                .Select(g => Row(g.Key, names.GetValueOrDefault(g.Key) ?? g.Key, g))
                .OrderBy(static r => r.MarginPercent ?? decimal.MaxValue)
                .ThenBy(static r => r.Key, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// Revenue, cost and margin by customer.
    /// </summary>
    /// <param name="from">The first day counted.</param>
    /// <param name="to">The last day counted.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A row per customer, thinnest margin first.</returns>
    /// <remarks>
    /// Sales at a till are gathered under the station's walk-in customer, which is what the till
    /// recorded them against. A chain doing most of its trade over a counter will find one enormous
    /// row there and nothing wrong with it: that is genuinely who bought the goods, as far as
    /// anybody knows.
    /// </remarks>
    public async Task<IReadOnlyList<MarginRow>> MarginByCustomerAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var sales = await SoldAsync(from, to, cancellationToken).ConfigureAwait(false);

        if (sales.Count == 0)
        {
            return [];
        }

        var documents = sales
            .Select(static s => s.DocumentNo)
            .Where(static d => !string.IsNullOrWhiteSpace(d))
            .Select(static d => d!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var customers = await CustomersByDocumentAsync(documents, cancellationToken).ConfigureAwait(false);

        return
        [
            .. sales
                // A sale whose document nothing recognises is left out rather than lumped under a
                // blank customer. It means a module that owned it is not installed, and inventing
                // a row for it would put somebody else's trade in this company's worst-margin list.
                .Where(s => customers.ContainsKey(s.DocumentNo ?? string.Empty))
                .GroupBy(s => customers[s.DocumentNo!])
                .Select(g => Row(g.Key.No, g.Key.Name, g))
                .OrderBy(static r => r.MarginPercent ?? decimal.MaxValue)
                .ThenBy(static r => r.Key, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// Orders with goods still to go out, latest first.
    /// </summary>
    /// <param name="customerNo">One customer, or null for all of them.</param>
    /// <param name="overdueOnly">Whether to list only what is already late.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The open orders.</returns>
    public async Task<IReadOnlyList<OpenSalesOrderRow>> OpenOrdersAsync(
        string? customerNo = null,
        bool overdueOnly = false,
        CancellationToken cancellationToken = default)
    {
        var wanted = customerNo?.Trim().ToUpperInvariant();
        var today = clock.Today;

        var orders = await context.Set<SalesOrder>()
            .AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.Status != SalesOrderStatus.Cancelled
                && (wanted == null || o.CustomerNo == wanted))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<OpenSalesOrderRow>();

        foreach (var order in orders)
        {
            var outstanding = order.Lines.Sum(static l => l.OutstandingToShip);

            if (outstanding <= 0m)
            {
                continue;
            }

            var overdue = order.RequestedDeliveryDate is { } promised
                ? today.DayNumber - promised.DayNumber
                : (int?)null;

            if (overdueOnly && overdue is not > 0)
            {
                continue;
            }

            rows.Add(new OpenSalesOrderRow(
                order.No,
                order.CustomerNo,
                order.CustomerName,
                order.OrderDate,
                order.RequestedDeliveryDate,
                overdue,
                outstanding,
                order.Lines.Sum(static l => l.OutstandingToShip * l.NetUnitPrice),
                order.Status.ToString()));
        }

        return
        [
            .. rows
                .OrderByDescending(static r => r.DaysOverdue ?? int.MinValue)
                .ThenBy(static r => r.OrderNo, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>What one group of sales came to.</summary>
    private static MarginRow Row(string key, string name, IEnumerable<Sold> sales)
    {
        var quantity = 0m;
        var revenue = 0m;
        var cost = 0m;
        var estimated = 0m;

        foreach (var sale in sales)
        {
            quantity += sale.Quantity;
            revenue += sale.SalesAmount;

            // Cost on an outbound entry is negative, because stock left. A margin wants it the
            // other way up.
            cost += -sale.CostAmount;
            estimated += -sale.EstimatedCost;
        }

        var margin = revenue - cost;

        return new MarginRow(
            key,
            name,
            quantity,
            revenue,
            cost,
            margin,

            // A margin on nought revenue has no answer, and printing nought would be a lie that a
            // spreadsheet then averages into everything else.
            revenue == 0m ? null : Math.Round(margin / revenue * 100m, 2, MidpointRounding.AwayFromZero),
            estimated);
    }

    /// <summary>One item's worth of a sale, as the value entries recorded it.</summary>
    private readonly record struct Sold(
        string ItemNo,
        string? DocumentNo,
        decimal Quantity,
        decimal SalesAmount,
        decimal CostAmount,
        decimal EstimatedCost);

    /// <summary>
    /// What went out over a period, at what it sold for and what it cost.
    /// </summary>
    /// <remarks>
    /// Sales and sales returns together, so a month with a lot of goods coming back reports the
    /// margin the company actually made rather than the one it made before anybody changed their
    /// mind. A return carries a negative quantity and a negative revenue, and both belong here.
    /// </remarks>
    private async Task<List<Sold>> SoldAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
        => await context.Set<ValueEntry>()
            .AsNoTracking()
            .Where(v => v.PostingDate >= from && v.PostingDate <= to
                && (v.ItemLedgerEntryType == ItemLedgerEntryType.Sale
                    || v.ItemLedgerEntryType == ItemLedgerEntryType.SalesReturn))
            .Select(static v => new Sold(
                v.ItemNo,
                v.DocumentNo,
                v.Quantity,
                v.SalesAmount,
                v.CostAmount,
                v.IsExpected ? v.CostAmount : 0m))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Who bought on each document, whichever module owns it.</summary>
    /// <remarks>
    /// Asked of every module that owns documents with a party on them, because a sale comes
    /// through more than one door and Sales cannot see the till any more than the till can see
    /// Sales. A report that understood only one of them would quietly cover half a company's
    /// trade.
    /// </remarks>
    private async Task<Dictionary<string, (string No, string Name)>> CustomersByDocumentAsync(
        List<string> documents,
        CancellationToken cancellationToken)
    {
        var byDocument = new Dictionary<string, (string No, string Name)>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in parties)
        {
            var found = await source.ForAsync(documents, cancellationToken).ConfigureAwait(false);

            foreach (var party in found)
            {
                byDocument[party.DocumentNo] = (party.PartyNo, party.PartyName);
            }
        }

        return byDocument;
    }
}
