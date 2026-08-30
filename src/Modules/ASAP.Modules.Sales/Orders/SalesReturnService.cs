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

namespace ASAP.Modules.Sales.Orders;

/// <summary>How much of one line is coming back.</summary>
/// <param name="LineNo">The order line.</param>
/// <param name="Quantity">How much came back. Always positive.</param>
public readonly record struct SalesReturnLineRequest(int LineNo, decimal Quantity);

/// <summary>What a return posted.</summary>
/// <param name="OrderNo">The order the goods came back on.</param>
/// <param name="CreditMemoNo">The credit memo number ASAP issued.</param>
/// <param name="StockTransactionNo">The transaction the stock movements were posted under.</param>
/// <param name="LedgerTransactionNo">The transaction the credit memo was posted under.</param>
/// <param name="LineCount">How many lines came back.</param>
/// <param name="CostAmount">What the returned goods cost when they left.</param>
/// <param name="NetAmount">What is being credited, after discount and before tax.</param>
/// <param name="DiscountAmount">The discount being taken back with it.</param>
/// <param name="TaxAmount">The tax being credited.</param>
/// <param name="TotalAmount">What the customer no longer owes.</param>
public readonly record struct SalesReturnReceipt(
    string OrderNo,
    string CreditMemoNo,
    long StockTransactionNo,
    long LedgerTransactionNo,
    int LineCount,
    decimal CostAmount,
    decimal NetAmount,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TotalAmount);

/// <summary>
/// Takes goods back and credits the customer for them.
/// </summary>
/// <remarks>
/// <para>
/// A return is two postings that must both be right and are right for different reasons.
/// </para>
/// <para>
/// The stock comes back at <em>what it cost when it left</em>, not at what the item costs today.
/// That is the whole reason the return names the order it came back on: sold at ten and returned
/// in a month when the item costs thirty, valuing it as an ordinary receipt would conjure twenty
/// out of nothing and leave the original sale's cost of sales where it was. The costing engine
/// already knows how to do this; all this service has to do is name the document.
/// </para>
/// <para>
/// The credit memo is the invoice run backwards, line for line, at the prices the customer was
/// actually charged. Not at today's price, and not at a price somebody types: a credit is an
/// undoing of a specific invoice, and if it can disagree with what was billed then the two
/// together are a way of moving money that no report will ever explain.
/// </para>
/// <para>
/// What can come back is bounded by what was <em>invoiced</em>, less what has already come back.
/// Goods that shipped and were never billed have nothing to credit; they go back by correcting the
/// shipment rather than by raising a credit memo for nothing.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="orders">Loads the order.</param>
/// <param name="posting">Moves and values the stock coming back.</param>
/// <param name="documents">Posts the credit memo, as a document rather than by hand.</param>
/// <param name="branches">Says which branch a location belongs to.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="overrides">Records every protection this return pushed past.</param>
/// <param name="numbers">Issues the credit memo number.</param>
/// <param name="setup">Supplies the revenue and discount accounts, and the number series.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records returns.</param>
public sealed class SalesReturnService(
    AsapDbContext context,
    SalesOrderService orders,
    StockPostingService posting,
    DocumentPostingService documents,
    Inventory.Locations.LocationBranchLookup branches,
    IMessageCatalog messages,
    OverrideAuditor overrides,
    INumberSeriesService numbers,
    ISetupService setup,
    IClock clock,
    ILogger<SalesReturnService> logger)
{
    /// <summary>
    /// Takes goods back against an order and credits the customer.
    /// </summary>
    /// <param name="orderNo">The order the goods went out on.</param>
    /// <param name="lines">
    /// How much of each line came back, or null for everything that could still come back.
    /// </param>
    /// <param name="reason">Why they came back, which goes on both postings.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was posted, or every reason it was refused.</returns>
    public async Task<Result<SalesReturnReceipt>> ReturnAsync(
        string orderNo,
        IReadOnlyList<SalesReturnLineRequest>? lines = null,
        string? reason = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        var order = await orders.LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result<SalesReturnReceipt>.FailureFrom(orders.NotFound(orderNo));
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OrderNo"] = order.No,
            ["Status"] = order.Status.ToString(),
        };

        var coming = Coming(order, lines);

        if (coming.Count == 0)
        {
            return Result<SalesReturnReceipt>.Failure(
                messages.Render(SalesMessages.NothingToReturn, arguments));
        }

        var found = Check(order, coming, heldOverridePermissions);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<SalesReturnReceipt>.Failure(found);
        }

        var revenueAccount = await Account("RevenueAccount", cancellationToken).ConfigureAwait(false);
        var discountAccount = await Account("DiscountAccount", cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(revenueAccount))
        {
            return Result<SalesReturnReceipt>.Failure(
                messages.Render(SalesMessages.NoRevenueAccount, arguments));
        }

        var today = clock.Today;
        var seriesCode = await SeriesCodeAsync(cancellationToken).ConfigureAwait(false);
        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<SalesReturnReceipt>.FailureFrom(numbered);
        }

        var stock = await ReceiveAsync(order, coming, reason, cancellationToken).ConfigureAwait(false);

        if (stock.Failed)
        {
            return Result<SalesReturnReceipt>.FailureFrom(stock);
        }

        found.AddRange(stock.Messages);

        var rates = await RatesAsync(order, coming, cancellationToken).ConfigureAwait(false);
        var journal = BuildJournal(order, coming, revenueAccount, discountAccount, rates, numbered.Value);


        var branchId = await branches
            .BranchOfAsync(order.LocationCode, cancellationToken)
            .ConfigureAwait(false);

        var posted = await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: order.No,
                    Lines: journal.Lines,
                    SourceCode: "SALES",
                    IsManualEntry: false,
                    PartyKind: PartyKind.Customer,
                    DocumentType: GlDocumentType.CreditMemo,
                    DocumentNo: numbered.Value,
                    Description: Describe(order, reason),
                    BranchId: branchId,
                    OverrideReason: overrideReason),
                cancellationToken)
            .ConfigureAwait(false);

        if (posted.Failed)
        {
            return Result<SalesReturnReceipt>.FailureFrom(posted);
        }

        foreach (var (line, quantity) in coming)
        {
            line.QuantityReturned += quantity;
        }

        overrides.Record(found, "Sales.Return", numbered.Value, overrideReason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Credited {CreditMemoNo} for {LineCount} returned line(s) against order {OrderNo}.",
            numbered.Value,
            coming.Count,
            order.No);

        return Result<SalesReturnReceipt>.Success(
            new SalesReturnReceipt(
                order.No,
                numbered.Value,
                stock.Value.TransactionNo,
                posted.Value.TransactionNo,
                coming.Count,
                stock.Value.CostAmount,
                journal.NetAmount,
                journal.DiscountAmount,
                journal.TaxAmount,
                journal.NetAmount + journal.TaxAmount),
            [.. found, .. posted.Messages]);
    }

    /// <summary>
    /// Puts the goods back on the shelf at what they cost on the way out.
    /// </summary>
    /// <remarks>
    /// The document the movement applies to is the order, which is what every outbound entry for
    /// this sale was posted against. That is the only link between the return and the layers the
    /// sale drew on, and without it the costing engine has nothing to go on but today's cost.
    /// </remarks>
    private async Task<Result<StockReceived>> ReceiveAsync(
        SalesOrder order,
        List<(SalesOrderLine Line, decimal Quantity)> coming,
        string? reason,
        CancellationToken cancellationToken)
    {
        var movements = coming
            .Where(static c => c.Line.Type is SalesLineType.Item)
            .Select(c => new StockMovementRequest(
                c.Line.ItemNo!,
                c.Line.LocationCode ?? order.LocationCode!,

                // Positive: the goods are coming back in.
                c.Quantity,

                // Nought, so the engine works it out from what left on this order rather than
                // taking a figure from here. A return valued by whoever keys it is a return that
                // can be used to move the inventory account.
                UnitCost: 0m,
                ItemLedgerEntryType.SalesReturn,

                // Negative, because the sale it undoes was positive. The margin report reads
                // these, and a return that carried no sales amount would leave the original sale
                // looking as profitable as it did before the goods came back.
                SalesAmount: -(c.Quantity * c.Line.NetUnitPrice),
                Note: reason,
                VariantCode: c.Line.VariantCode,
                AppliesToDocumentNo: order.No))
            .ToList();

        if (movements.Count == 0)
        {
            // Every line was a charge. Nothing moves, and the credit memo still stands.
            return Result<StockReceived>.Success(new StockReceived(0L, 0m));
        }

        var posted = await posting
            .PostAsync(
                movements,
                clock.Today,
                "SALES",
                order.No,

                // Goods coming in never take stock below zero, so the policy has nothing to say.
                companyAllowsNegative: true,
                heldOverridePermissions: null,
                overrideReason: null,
                cancellationToken)
            .ConfigureAwait(false);

        return posted.Failed
            ? Result<StockReceived>.FailureFrom(posted)
            : Result<StockReceived>.Success(
                new StockReceived(posted.Value.TransactionNo, posted.Value.CostAmount),
                posted.Messages);
    }

    /// <summary>What came back on the shelf, and what it was worth when it left.</summary>
    /// <param name="TransactionNo">The stock transaction.</param>
    /// <param name="CostAmount">
    /// What the goods cost on the way out. Positive: the inventory account went up by it, and cost
    /// of sales went down by the same, which is the entry the whole exercise exists to get right.
    /// </param>
    private readonly record struct StockReceived(long TransactionNo, decimal CostAmount);

    /// <summary>The journal a credit memo becomes, with the figures it reports.</summary>
    private readonly record struct CreditJournal(
        List<PostJournalLine> Lines,
        decimal NetAmount,
        decimal DiscountAmount,
        decimal TaxAmount);

    /// <summary>
    /// Turns the returned lines into a journal: the invoice, every sign reversed.
    /// </summary>
    /// <remarks>
    /// Revenue is debited at list and the discount credited back, which is the exact mirror of how
    /// the invoice posted. Netting them would give the same profit and lose the answer to how much
    /// was discounted and how much of that came back — and a discount that can only be given and
    /// never taken back is a figure that drifts every time a customer changes their mind.
    /// </remarks>
    private static CreditJournal BuildJournal(
        SalesOrder order,
        List<(SalesOrderLine Line, decimal Quantity)> coming,
        string revenueAccount,
        string? discountAccount,
        IReadOnlyDictionary<string, decimal> rates,
        string creditMemoNo)
    {
        var lines = new List<PostJournalLine>();
        var net = 0m;
        var discount = 0m;
        var tax = 0m;

        foreach (var (line, quantity) in coming)
        {
            var lineNet = quantity * line.NetUnitPrice;
            var lineDiscount = quantity * line.UnitPrice * (line.DiscountPercent / 100m);
            var account = line.Type is SalesLineType.GlAccount ? line.AccountNo! : revenueAccount;

            net += lineNet;
            discount += lineDiscount;

            if (line.TaxCode is { Length: > 0 } code && rates.TryGetValue(code, out var percentage))
            {
                tax += TaxCalculator.FromNet(lineNet, percentage).Tax;
            }

            var showDiscount = lineDiscount != 0m && discountAccount is { Length: > 0 };

            // Revenue debited: the sale is being taken back.
            lines.Add(new PostJournalLine(
                account,
                showDiscount ? lineNet + lineDiscount : lineNet,
                $"{line.Description} — returned",
                TaxCode: line.TaxCode));

            if (showDiscount)
            {
                lines.Add(new PostJournalLine(
                    discountAccount!,
                    -lineDiscount,
                    $"{line.Description} — discount returned",
                    TaxCode: line.TaxCode));
            }
        }

        // The customer takes the credit, and with it an entry on their account that the aged
        // analysis and the credit-limit check both see.
        lines.Add(new PostJournalLine(
            order.CustomerNo,
            -(net + tax),
            $"{order.CustomerName} — {creditMemoNo}",
            AccountType: JournalAccountType.Customer,
            ExternalDocumentNo: order.CustomerOrderNo));

        return new CreditJournal(lines, net, discount, tax);
    }

    /// <summary>What is coming back: what the caller said, or everything that still could.</summary>
    private static List<(SalesOrderLine Line, decimal Quantity)> Coming(
        SalesOrder order,
        IReadOnlyList<SalesReturnLineRequest>? lines)
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
    /// Says so when more is coming back than ever went out and was billed.
    /// </summary>
    /// <remarks>
    /// A refusal rather than a warning, and deliberately not overridable. Everything else this
    /// module guards is a judgment somebody is entitled to make differently; crediting a customer
    /// for goods they were never charged for is not a judgment, it is arithmetic that does not
    /// add up, and the stock it puts on the shelf never existed either.
    /// </remarks>
    private List<AsapMessage> Check(
        SalesOrder order,
        List<(SalesOrderLine Line, decimal Quantity)> coming,
        IReadOnlySet<string>? held)
    {
        _ = held;

        var found = new List<AsapMessage>();

        foreach (var (line, quantity) in coming.Where(c => c.Quantity > c.Line.ReturnableQuantity))
        {
            found.Add(messages.Render(
                SalesMessages.ReturnExceedsInvoiced,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OrderNo"] = order.No,
                    ["LineNo"] = line.LineNo,
                    ["ItemNo"] = line.ItemNo ?? line.AccountNo,
                    ["Returned"] = quantity,
                    ["OutstandingQuantity"] = line.ReturnableQuantity,
                    ["Invoiced"] = line.QuantityInvoiced,
                },
                MessageTarget.OnField($"Lines[{line.LineNo}]")));
        }

        return found;
    }

    private static string Describe(SalesOrder order, string? reason)
        => reason is { Length: > 0 } why
            ? $"{order.CustomerName} — {order.No} — {why}"
            : $"{order.CustomerName} — {order.No} — returned";

    private async Task<Dictionary<string, decimal>> RatesAsync(
        SalesOrder order,
        List<(SalesOrderLine Line, decimal Quantity)> coming,
        CancellationToken cancellationToken)
    {
        var codes = coming
            .Select(static c => c.Line.TaxCode)
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        // The rate the sale was made at, not today's. A rate change between the invoice and the
        // return would otherwise credit a different amount of tax than was charged, and the
        // difference sits in the tax account forever with nothing to explain it.
        var taxCodes = await context.Set<TaxCode>()
            .AsNoTracking()
            .Include(c => c.Rates)
            .Where(c => codes.Contains(c.Code) && c.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return taxCodes.ToDictionary(
            static c => c.Code,
            c => c.RateOn(order.OrderDate) ?? 0m,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> SeriesCodeAsync(CancellationToken cancellationToken)
        => await setup
               .GetAsync<string>($"{SalesModule.Id}.CreditMemos.NumberSeries", cancellationToken)
               .ConfigureAwait(false)
           ?? "SALES-CM";

    private async Task<string?> Account(string name, CancellationToken cancellationToken)
        => await setup
            .GetAsync<string>($"{SalesModule.Id}.Posting.{name}", cancellationToken)
            .ConfigureAwait(false);
}
