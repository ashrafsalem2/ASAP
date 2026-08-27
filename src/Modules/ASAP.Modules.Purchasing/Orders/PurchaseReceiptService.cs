using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Purchasing.Orders;

/// <summary>How much of one line arrived.</summary>
/// <param name="LineNo">The order line.</param>
/// <param name="Quantity">How much arrived. Always positive.</param>
public readonly record struct ReceiptLineRequest(int LineNo, decimal Quantity);

/// <summary>What a receipt moved.</summary>
/// <param name="OrderNo">The order received against.</param>
/// <param name="TransactionNo">The transaction the movements were posted under.</param>
/// <param name="LineCount">How many lines received.</param>
/// <param name="Value">What the goods were worth.</param>
/// <param name="Status">Where the order stands now.</param>
public readonly record struct PurchaseReceipt(
    string OrderNo,
    long TransactionNo,
    int LineCount,
    decimal Value,
    PurchaseOrderStatus Status);

/// <summary>
/// Records that goods have arrived.
/// </summary>
/// <remarks>
/// <para>
/// A receipt does two things at once, and the second is the one people forget. It moves the stock,
/// which is obvious. It also records that the company now owes for those goods -- from the moment
/// they land, not from the moment the invoice does. The value goes to a goods-received-not-invoiced
/// account rather than to payables, because there is nobody to pay yet: no invoice, no due date,
/// nothing to apply a payment against.
/// </para>
/// <para>
/// Skipping that step is what makes a balance sheet a fortnight behind the post. Stock arrives in
/// March, the invoice is dated April, and March closes showing goods the company apparently
/// received for free.
/// </para>
/// <para>
/// The stock movement goes through Inventory's own posting service, which is what values it, so a
/// receipt is costed by exactly the same engine as everything else that moves stock.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="orders">Loads the order.</param>
/// <param name="posting">Moves and values the stock.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="overrides">Records every protection this receipt pushed past.</param>
/// <param name="setup">Supplies the accrual account and the negative-stock policy.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records receipts.</param>
public sealed class PurchaseReceiptService(
    AsapDbContext context,
    PurchaseOrderService orders,
    StockPostingService posting,
    IMessageCatalog messages,
    OverrideAuditor overrides,
    ISetupService setup,
    IClock clock,
    ILogger<PurchaseReceiptService> logger)
{
    /// <summary>
    /// Receives goods against an order.
    /// </summary>
    /// <param name="orderNo">The order the goods arrived against.</param>
    /// <param name="lines">
    /// How much of each line arrived, or null to receive everything still outstanding — which is
    /// the ordinary case and should not need typing.
    /// </param>
    /// <param name="vendorDeliveryNo">The number on the vendor's delivery note.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What moved, or every reason it was refused.</returns>
    public async Task<Result<PurchaseReceipt>> ReceiveAsync(
        string orderNo,
        IReadOnlyList<ReceiptLineRequest>? lines = null,
        string? vendorDeliveryNo = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        var order = await orders.LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result<PurchaseReceipt>.FailureFrom(orders.NotFound(orderNo));
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OrderNo"] = order.No,
            ["Status"] = order.Status.ToString(),
        };

        var arriving = Arriving(order, lines);
        var refusals = CheckArriving(order, arriving, heldOverridePermissions);

        if (arriving.Count == 0)
        {
            return Result<PurchaseReceipt>.Failure(
                messages.Render(PurchasingMessages.NothingToReceive, arguments));
        }

        if (refusals.Exists(static m => m.IsFailure))
        {
            return Result<PurchaseReceipt>.Failure(refusals);
        }

        var accrualAccount = await AccrualAccountAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(accrualAccount))
        {
            return Result<PurchaseReceipt>.Failure(
                messages.Render(PurchasingMessages.NoAccrualAccount, arguments));
        }

        var movements = arriving
            .Where(static a => a.Line.Type is PurchaseLineType.Item)
            .Select(a => new StockMovementRequest(
                a.Line.ItemNo!,
                a.Line.LocationCode ?? order.LocationCode!,
                a.Quantity,
                a.Line.DirectUnitCost,
                ItemLedgerEntryType.Purchase,

                // Inventory goes up against goods-received-not-invoiced rather than against the
                // variance account it would otherwise fall back to. The company owes for these
                // goods from the moment they land; there is simply nobody to pay yet.
                ContraAccountNo: accrualAccount))
            .ToList();

        var transactionNo = 0L;
        var value = 0m;

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
                    "PURCH",
                    order.No,
                    allowsNegative,
                    heldOverridePermissions,
                    overrideReason,
                    cancellationToken)
                .ConfigureAwait(false);

            if (posted.Failed)
            {
                return Result<PurchaseReceipt>.FailureFrom(posted);
            }

            transactionNo = posted.Value.TransactionNo;
            value = posted.Value.CostAmount;
            refusals.AddRange(posted.Messages);
        }

        foreach (var (line, quantity) in arriving)
        {
            line.QuantityReceived += quantity;
            value += line.Type is PurchaseLineType.GlAccount ? quantity * line.DirectUnitCost : 0m;
        }

        order.Status = order.HasOutstandingReceipt
            ? PurchaseOrderStatus.PartiallyReceived
            : PurchaseOrderStatus.Received;

        // Accepting more than was ordered is allowed to whoever holds the permission, and the
        // message says so is recorded. Something has to make that true.
        overrides.Record(refusals, "Purchasing.Receipt", order.No, overrideReason);

        if (!string.IsNullOrWhiteSpace(vendorDeliveryNo))
        {
            order.VendorOrderNo ??= vendorDeliveryNo;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Received {LineCount} line(s) against purchase order {OrderNo}, now {Status}.",
            arriving.Count,
            order.No,
            order.Status);

        return Result<PurchaseReceipt>.Success(
            new PurchaseReceipt(order.No, transactionNo, arriving.Count, value, order.Status),
            refusals);
    }

    /// <summary>
    /// Works out what is arriving: what the caller said, or everything outstanding.
    /// </summary>
    private static List<(PurchaseOrderLine Line, decimal Quantity)> Arriving(
        PurchaseOrder order,
        IReadOnlyList<ReceiptLineRequest>? lines)
    {
        if (lines is null)
        {
            return
            [
                .. order.Lines
                    .Where(static l => l.OutstandingToReceive > 0)
                    .OrderBy(static l => l.LineNo)
                    .Select(static l => (l, l.OutstandingToReceive)),
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

    /// <summary>
    /// Checks nothing is arriving that the order does not cover.
    /// </summary>
    /// <remarks>
    /// Overridable rather than absolute, because a vendor shipping a few extra is real and
    /// refusing to record goods that are physically in the warehouse helps nobody. What it must
    /// not be is silent: the excess has no agreed price behind it.
    /// </remarks>
    private List<AsapMessage> CheckArriving(
        PurchaseOrder order,
        List<(PurchaseOrderLine Line, decimal Quantity)> arriving,
        IReadOnlySet<string>? held)
    {
        var found = new List<AsapMessage>();

        foreach (var (line, quantity) in arriving)
        {
            if (quantity <= line.OutstandingToReceive)
            {
                continue;
            }

            var rendered = messages.Render(
                PurchasingMessages.OverReceipt,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OrderNo"] = order.No,
                    ["LineNo"] = line.LineNo,
                    ["ItemNo"] = line.ItemNo ?? line.AccountNo,
                    ["Received"] = quantity,
                    ["Outstanding"] = line.OutstandingToReceive,
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

    private async Task<string?> AccrualAccountAsync(CancellationToken cancellationToken)
        => await setup
            .GetAsync<string>($"{PurchasingModule.Id}.Posting.AccrualAccount", cancellationToken)
            .ConfigureAwait(false);
}
