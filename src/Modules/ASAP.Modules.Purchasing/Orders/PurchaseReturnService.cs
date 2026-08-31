using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Finance.Tax;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Purchasing.Orders;

/// <summary>How much of one line is going back to the vendor.</summary>
/// <param name="LineNo">The order line.</param>
/// <param name="Quantity">How much is going back. Always positive.</param>
public readonly record struct PurchaseReturnLineRequest(int LineNo, decimal Quantity);

/// <summary>What a purchase return posted.</summary>
/// <param name="OrderNo">The order the goods came in on.</param>
/// <param name="CreditMemoNo">The credit memo number, or empty where nothing was invoiced yet.</param>
/// <param name="StockTransactionNo">The transaction the stock movements were posted under.</param>
/// <param name="LedgerTransactionNo">The transaction the credit memo was posted under, or nought.</param>
/// <param name="LineCount">How many lines went back.</param>
/// <param name="CostAmount">What the goods were worth on the way out. Negative: stock went down.</param>
/// <param name="CreditedQuantity">How much of it had been invoiced and so could be credited.</param>
/// <param name="NetAmount">What is being credited, before tax.</param>
/// <param name="TaxAmount">The tax being credited.</param>
/// <param name="TotalAmount">What the company no longer owes.</param>
public readonly record struct PurchaseReturnReceipt(
    string OrderNo,
    string CreditMemoNo,
    long StockTransactionNo,
    long LedgerTransactionNo,
    int LineCount,
    decimal CostAmount,
    decimal CreditedQuantity,
    decimal NetAmount,
    decimal TaxAmount,
    decimal TotalAmount);

/// <summary>
/// Sends goods back to the vendor and takes the money off what is owed.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of a sales return, and it differs in one place that matters. A customer can only be
/// credited for goods they were billed for, so a sales return is bounded by what was invoiced.
/// Goods can be sent <em>back</em> to a vendor before their invoice ever turns up -- rejecting a
/// faulty delivery at the door is the ordinary case, not the exception -- so a purchase return is
/// bounded by what was <em>received</em>.
/// </para>
/// <para>
/// That difference is why the posting comes in two parts.
/// </para>
/// <code>
///   Always, for everything going back:
///     Accrual (goods received not invoiced)   debit    what the goods cost
///     Inventory                               credit   the same, by the costing engine
///
///   And separately, only for the part that had been invoiced:
///     Vendor (payables)                       debit    what was billed, plus its tax
///     Accrual                                 credit   the net
///     VAT recoverable                         credit   the tax, given back
/// </code>
/// <para>
/// Goods returned before their invoice arrives simply unwind the accrual and stop there. There is
/// no debt to reverse, because nobody has asked to be paid yet. Goods returned after it has
/// arrived unwind both, and the accrual nets to nothing across the pair -- which is what says the
/// two halves agree.
/// </para>
/// <para>
/// The stock leaves at what it cost when it arrived, not at what the item costs today, which is
/// why the return names the order. That is the same rule as a sales return and it is wrong in the
/// same way when it is missed: a vendor return valued at today's cost lets a change of supplier
/// price move the inventory account.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="orders">Loads the order.</param>
/// <param name="posting">Moves and values the stock going back.</param>
/// <param name="documents">Posts the credit memo, as a document rather than by hand.</param>
/// <param name="branches">Says which branch a location belongs to.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="overrides">Records every protection this return pushed past.</param>
/// <param name="numbers">Issues the credit memo number.</param>
/// <param name="setup">Supplies the accrual account and the number series.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records returns.</param>
public sealed class PurchaseReturnService(
    AsapDbContext context,
    PurchaseOrderService orders,
    StockPostingService posting,
    DocumentPostingService documents,
    Inventory.Locations.LocationBranchLookup branches,
    IMessageCatalog messages,
    OverrideAuditor overrides,
    INumberSeriesService numbers,
    ISetupService setup,
    IClock clock,
    ILogger<PurchaseReturnService> logger)
{
    /// <summary>
    /// Sends goods back against an order.
    /// </summary>
    /// <param name="orderNo">The order the goods arrived on.</param>
    /// <param name="lines">
    /// How much of each line is going back, or null for everything that still could.
    /// </param>
    /// <param name="reason">Why they are going back.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was posted, or every reason it was refused.</returns>
    public async Task<Result<PurchaseReturnReceipt>> ReturnAsync(
        string orderNo,
        IReadOnlyList<PurchaseReturnLineRequest>? lines = null,
        string? reason = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        var order = await orders.LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result<PurchaseReturnReceipt>.FailureFrom(orders.NotFound(orderNo));
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OrderNo"] = order.No,
            ["Status"] = order.Status.ToString(),
        };

        var going = Going(order, lines);

        if (going.Count == 0)
        {
            return Result<PurchaseReturnReceipt>.Failure(
                messages.Render(PurchasingMessages.NothingToSendBack, arguments));
        }

        var found = Check(order, going);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PurchaseReturnReceipt>.Failure(found);
        }

        var accrualAccount = await AccrualAccountAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(accrualAccount))
        {
            return Result<PurchaseReturnReceipt>.Failure(
                messages.Render(PurchasingMessages.NoAccrualAccount, arguments));
        }

        var stock = await SendBackAsync(order, going, accrualAccount, reason, heldOverridePermissions, overrideReason, cancellationToken)
            .ConfigureAwait(false);

        if (stock.Failed)
        {
            return Result<PurchaseReturnReceipt>.FailureFrom(stock);
        }

        found.AddRange(stock.Messages);

        // Only the part that was invoiced can be credited. The rest never became a debt.
        var creditable = Creditable(going);
        var creditMemoNo = string.Empty;
        var ledgerTransactionNo = 0L;
        var net = 0m;
        var tax = 0m;

        if (creditable.Exists(static c => c.Quantity > 0m))
        {
            var credited = await CreditAsync(
                    order, creditable, accrualAccount, reason, overrideReason, cancellationToken)
                .ConfigureAwait(false);

            if (credited.Failed)
            {
                return Result<PurchaseReturnReceipt>.FailureFrom(credited);
            }

            creditMemoNo = credited.Value.DocumentNo;
            ledgerTransactionNo = credited.Value.TransactionNo;
            net = credited.Value.NetAmount;
            tax = credited.Value.TaxAmount;
            found.AddRange(credited.Messages);
        }

        foreach (var (line, quantity) in going)
        {
            line.QuantityReturned += quantity;
        }

        overrides.Record(found, "Purchasing.Return", order.No, overrideReason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Sent {LineCount} line(s) back against purchase order {OrderNo}, credit memo {CreditMemoNo}.",
            going.Count,
            order.No,
            creditMemoNo.Length == 0 ? "(none — nothing was invoiced yet)" : creditMemoNo);

        return Result<PurchaseReturnReceipt>.Success(
            new PurchaseReturnReceipt(
                order.No,
                creditMemoNo,
                stock.Value,
                ledgerTransactionNo,
                going.Count,
                0m - Math.Abs(await StockCostAsync(stock.Value, cancellationToken).ConfigureAwait(false)),
                creditable.Sum(static c => c.Quantity),
                net,
                tax,
                net + tax),
            found);
    }

    /// <summary>
    /// Takes the goods off the shelf at what they cost on the way in.
    /// </summary>
    /// <remarks>
    /// Against the accrual account rather than a variance account, because a return is the receipt
    /// run backwards and the receipt debited inventory against the accrual. Sending it anywhere
    /// else would leave the accrual holding a balance for goods that are no longer in the building.
    /// </remarks>
    private async Task<Result<long>> SendBackAsync(
        PurchaseOrder order,
        List<(PurchaseOrderLine Line, decimal Quantity)> going,
        string accrualAccount,
        string? reason,
        IReadOnlySet<string>? heldOverridePermissions,
        string? overrideReason,
        CancellationToken cancellationToken)
    {
        // Which shelf the goods came in on. Inventory refuses an outbound movement from a
        // bin-tracked location without one, and it is right to: what goes out came off a
        // particular shelf and the engine cannot know which. A return is the one case where the
        // answer already exists -- the goods arrived on this order, and the receipt recorded where
        // they were put. Supplying it here keeps the rule intact and answers it from the module
        // that has the document.
        var bins = await ReceivingBinsAsync(order, cancellationToken).ConfigureAwait(false);

        var movements = going
            .Where(static g => g.Line.Type is PurchaseLineType.Item)
            .Select(g => new StockMovementRequest(
                g.Line.ItemNo!,
                g.Line.LocationCode ?? order.LocationCode!,

                // Negative: the goods are leaving.
                -g.Quantity,

                // Nought, so the engine reads what they cost on the way in from the order they
                // arrived on rather than taking a figure from here.
                UnitCost: 0m,
                ItemLedgerEntryType.PurchaseReturn,
                ContraAccountNo: accrualAccount,
                Note: reason,
                BinCode: bins.GetValueOrDefault((g.Line.ItemNo!, g.Line.VariantCode ?? string.Empty)),
                VariantCode: g.Line.VariantCode,
                AppliesToDocumentNo: order.No))
            .ToList();

        if (movements.Count == 0)
        {
            // Every line was a charge. Nothing moves, and the credit memo still stands.
            return Result<long>.Success(0L);
        }

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

        return posted.Failed
            ? Result<long>.FailureFrom(posted)
            : Result<long>.Success(posted.Value.TransactionNo, posted.Messages);
    }


    /// <summary>
    /// Which bin each item arrived into on this order.
    /// </summary>
    /// <remarks>
    /// Where a delivery was split across several shelves, the one still holding the most is taken.
    /// It is a guess between shelves rather than between locations, so the worst it can be is a
    /// bin correction; taking the emptiest would instead send the return through a shelf that has
    /// nothing on it and turn one problem into two.
    /// </remarks>
    private async Task<Dictionary<(string ItemNo, string VariantCode), string>> ReceivingBinsAsync(
        PurchaseOrder order,
        CancellationToken cancellationToken)
    {
        var received = await context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.DocumentNo == order.No && e.Quantity > 0m && e.BinCode != null)
            .Select(static e => new { e.ItemNo, e.VariantCode, e.BinCode, e.RemainingQuantity })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return received
            .GroupBy(static e => (e.ItemNo, e.VariantCode ?? string.Empty))
            .ToDictionary(
                static g => g.Key,
                static g => g
                    .OrderByDescending(static e => e.RemainingQuantity)
                    .Select(static e => e.BinCode!)
                    .First());
    }

    /// <summary>What a credit memo posted.</summary>
    private readonly record struct CreditMemo(
        string DocumentNo,
        long TransactionNo,
        decimal NetAmount,
        decimal TaxAmount);

    /// <summary>
    /// Takes the money off what the company owes, for the part that had been invoiced.
    /// </summary>
    private async Task<Result<CreditMemo>> CreditAsync(
        PurchaseOrder order,
        List<(PurchaseOrderLine Line, decimal Quantity)> creditable,
        string accrualAccount,
        string? reason,
        string? overrideReason,
        CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var seriesCode = await SeriesCodeAsync(cancellationToken).ConfigureAwait(false);
        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<CreditMemo>.FailureFrom(numbered);
        }

        var rates = await RatesAsync(creditable, cancellationToken).ConfigureAwait(false);
        var journalLines = new List<PostJournalLine>();
        var net = 0m;
        var tax = 0m;

        foreach (var (line, quantity) in creditable.Where(static c => c.Quantity > 0m))
        {
            var amount = quantity * line.DirectUnitCost;
            var isItem = line.Type is PurchaseLineType.Item;

            net += amount;

            if (line.TaxCode is { Length: > 0 } code && rates.TryGetValue(code, out var percentage))
            {
                tax += TaxCalculator.FromNet(amount, percentage).Tax;
            }

            // Credited back to wherever the invoice charged it: the accrual on an item line,
            // because that is where the receipt put it, and the expense account on a cost line.
            journalLines.Add(new PostJournalLine(
                isItem ? accrualAccount : line.AccountNo!,
                -amount,
                $"{line.Description} — returned",
                TaxCode: line.TaxCode));
        }

        // The vendor takes the debit: the company owes them less than it did.
        journalLines.Add(new PostJournalLine(
            order.VendorNo,
            net + tax,
            $"{order.VendorName} — {numbered.Value}",
            AccountType: JournalAccountType.Vendor));

        var branchId = await branches
            .BranchOfAsync(order.LocationCode, cancellationToken)
            .ConfigureAwait(false);

        var posted = await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: order.No,
                    Lines: journalLines,
                    SourceCode: "PURCH",
                    IsManualEntry: false,
                    PartyKind: PartyKind.Vendor,
                    DocumentType: GlDocumentType.CreditMemo,
                    DocumentNo: numbered.Value,
                    Description: Describe(order, reason),
                    BranchId: branchId,
                    OverrideReason: overrideReason),
                cancellationToken)
            .ConfigureAwait(false);

        return posted.Failed
            ? Result<CreditMemo>.FailureFrom(posted)
            : Result<CreditMemo>.Success(
                new CreditMemo(numbered.Value, posted.Value.TransactionNo, net, tax),
                posted.Messages);
    }

    /// <summary>
    /// How much of what is going back had been invoiced, and so has a debt to reverse.
    /// </summary>
    /// <remarks>
    /// The arithmetic itself lives in <see cref="PurchaseReturnCrediting"/>, because it is the
    /// whole judgment of this feature and it is worth being able to check on its own.
    /// </remarks>
    private static List<(PurchaseOrderLine Line, decimal Quantity)> Creditable(
        List<(PurchaseOrderLine Line, decimal Quantity)> going)
        =>
        [
            .. going.Select(static g => (
                g.Line,
                PurchaseReturnCrediting.CreditableQuantity(
                    g.Line.QuantityInvoiced,
                    g.Line.QuantityReturned,
                    g.Quantity))),
        ];

    /// <summary>What is going back: what the caller said, or everything that still could.</summary>
    private static List<(PurchaseOrderLine Line, decimal Quantity)> Going(
        PurchaseOrder order,
        IReadOnlyList<PurchaseReturnLineRequest>? lines)
    {
        if (lines is null)
        {
            return
            [
                .. order.Lines
                    .Where(static l => l.ReturnableQuantity > 0)
                    .OrderBy(static l => l.LineNo)
                    .Select(static l => (l, l.ReturnableQuantity)),
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
    /// Says so when more is going back than ever arrived.
    /// </summary>
    /// <remarks>
    /// A refusal, and not overridable. Sending back goods that never came takes stock off a shelf
    /// that never held it and money off a debt that was never owed; that is not a judgment
    /// somebody is entitled to make differently, it is arithmetic that does not add up.
    /// </remarks>
    private List<AsapMessage> Check(
        PurchaseOrder order,
        List<(PurchaseOrderLine Line, decimal Quantity)> going)
    {
        var found = new List<AsapMessage>();

        foreach (var (line, quantity) in going.Where(g => g.Quantity > g.Line.ReturnableQuantity))
        {
            found.Add(messages.Render(
                PurchasingMessages.ReturnExceedsReceipt,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OrderNo"] = order.No,
                    ["LineNo"] = line.LineNo,
                    ["ItemNo"] = line.ItemNo ?? line.AccountNo,
                    ["Returned"] = quantity,
                    ["OutstandingQuantity"] = line.ReturnableQuantity,
                    ["Received"] = line.QuantityReceived,
                },
                MessageTarget.OnField($"Lines[{line.LineNo}]")));
        }

        return found;
    }

    private static string Describe(PurchaseOrder order, string? reason)
        => reason is { Length: > 0 } why
            ? $"{order.VendorName} — {order.No} — {why}"
            : $"{order.VendorName} — {order.No} — returned";

    private async Task<decimal> StockCostAsync(long transactionNo, CancellationToken cancellationToken)
        => transactionNo == 0L
            ? 0m
            : await context.Set<ValueEntry>()
                .AsNoTracking()
                .Where(v => v.TransactionNo == transactionNo)
                .SumAsync(static v => (decimal?)v.CostAmount, cancellationToken)
                .ConfigureAwait(false) ?? 0m;

    private async Task<Dictionary<string, decimal>> RatesAsync(
        List<(PurchaseOrderLine Line, decimal Quantity)> lines,
        CancellationToken cancellationToken)
    {
        var codes = lines
            .Select(static l => l.Line.TaxCode)
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        var taxCodes = await context.Set<TaxCode>()
            .AsNoTracking()
            .Include(c => c.Rates)
            .Where(c => codes.Contains(c.Code) && c.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The rate the goods were bought at, read from the order date. A rate change between the
        // invoice and the return would otherwise reclaim a different amount than was charged, and
        // the difference would sit in the tax account with nothing to explain it.
        return taxCodes.ToDictionary(
            static c => c.Code,
            c => c.RateOn(clock.Today) ?? 0m,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> SeriesCodeAsync(CancellationToken cancellationToken)
        => await setup
               .GetAsync<string>($"{PurchasingModule.Id}.CreditMemos.NumberSeries", cancellationToken)
               .ConfigureAwait(false)
           ?? "PURCH-CM";

    private async Task<string?> AccrualAccountAsync(CancellationToken cancellationToken)
        => await setup
            .GetAsync<string>($"{PurchasingModule.Id}.Posting.AccrualAccount", cancellationToken)
            .ConfigureAwait(false);
}
