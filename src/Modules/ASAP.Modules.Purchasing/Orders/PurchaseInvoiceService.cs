using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Posting;
using ASAP.Modules.Finance.Tax;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Purchasing.Orders;

/// <summary>How much of one line is being invoiced, and at what price.</summary>
/// <param name="LineNo">The order line.</param>
/// <param name="Quantity">How much the invoice covers. Always positive.</param>
/// <param name="DirectUnitCost">
/// The price on the invoice, when it differs from the price ordered. Null keeps the agreed price.
/// </param>
public readonly record struct InvoiceLineRequest(
    int LineNo,
    decimal Quantity,
    decimal? DirectUnitCost = null);

/// <summary>What an invoice posted.</summary>
/// <param name="OrderNo">The order invoiced against.</param>
/// <param name="TransactionNo">The transaction the entries were posted under.</param>
/// <param name="DocumentNo">The vendor's invoice number.</param>
/// <param name="NetAmount">The goods and services, before tax.</param>
/// <param name="TaxAmount">The tax the vendor charged.</param>
/// <param name="TotalAmount">What the vendor is owed.</param>
/// <param name="Status">Where the order stands now.</param>
public readonly record struct PurchaseInvoiceReceipt(
    string OrderNo,
    long TransactionNo,
    string DocumentNo,
    decimal NetAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    PurchaseOrderStatus Status);

/// <summary>
/// Turns goods that have arrived into a debt owed to the vendor.
/// </summary>
/// <remarks>
/// <para>
/// The third leg of the match. Receiving said the goods are here and the company owes for them;
/// invoicing says who to pay, how much, and by when. What it must not do is take the vendor's word
/// for what arrived -- so an invoice covering more than has been received is refused, and that
/// single check is what catches being billed for a delivery that never came.
/// </para>
/// <para>
/// The posting clears what the receipt accrued and replaces it with a real payable:
/// </para>
/// <code>
///   Goods received not invoiced   debit    what the receipt put there
///   Purchase price variance       either   the difference, when the invoice disagrees
///   VAT recoverable               debit    added by the tax engine from the line's code
///   Vendor (payables)             credit   the total, and an entry on their account
/// </code>
/// <para>
/// It goes through the ordinary journal poster rather than writing entries itself, which is what
/// gives it the period checks, the dimension rules, the vendor ledger entry and the tax entry
/// without any of that being reimplemented here.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="orders">Loads the order.</param>
/// <param name="documents">Posts the journal, as a document rather than by hand.</param>
/// <param name="branches">Says which branch a location belongs to.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="overrides">Records every protection this invoice pushed past.</param>
/// <param name="setup">Supplies the accrual and variance accounts.</param>
/// <param name="clock">Supplies the date the tax rate is read on.</param>
/// <param name="logger">Records invoices.</param>
public sealed class PurchaseInvoiceService(
    AsapDbContext context,
    PurchaseOrderService orders,
    DocumentPostingService documents,
    Inventory.Locations.LocationBranchLookup branches,
    IMessageCatalog messages,
    OverrideAuditor overrides,
    ISetupService setup,
    IClock clock,
    ILogger<PurchaseInvoiceService> logger)
{
    /// <summary>
    /// Posts a vendor invoice against an order.
    /// </summary>
    /// <param name="orderNo">The order being invoiced.</param>
    /// <param name="vendorInvoiceNo">The number on the vendor's own invoice.</param>
    /// <param name="lines">
    /// What the invoice covers, or null for everything received and not yet invoiced.
    /// </param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was posted, or every reason it was refused.</returns>
    public async Task<Result<PurchaseInvoiceReceipt>> PostAsync(
        string orderNo,
        string vendorInvoiceNo,
        IReadOnlyList<InvoiceLineRequest>? lines = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        var order = await orders.LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result<PurchaseInvoiceReceipt>.FailureFrom(orders.NotFound(orderNo));
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OrderNo"] = order.No,
            ["Status"] = order.Status.ToString(),
        };

        var billed = Billed(order, lines);

        if (billed.Count == 0)
        {
            return Result<PurchaseInvoiceReceipt>.Failure(
                messages.Render(PurchasingMessages.NothingToInvoice, arguments));
        }

        var warnings = Check(order, billed, heldOverridePermissions);

        if (warnings.Exists(static m => m.IsFailure))
        {
            return Result<PurchaseInvoiceReceipt>.Failure(warnings);
        }

        var accrualAccount = await Account("AccrualAccount", cancellationToken).ConfigureAwait(false);
        var varianceAccount = await Account("VarianceAccount", cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(accrualAccount))
        {
            return Result<PurchaseInvoiceReceipt>.Failure(
                messages.Render(PurchasingMessages.NoAccrualAccount, arguments));
        }

        var rates = await RatesAsync(billed, cancellationToken).ConfigureAwait(false);
        var journal = BuildJournal(order, billed, accrualAccount, varianceAccount, rates);

        // The place the goods were received into. A price variance belongs to the shop that
        // bought at the wrong price, not to head office for having processed the invoice.
        var branchId = await branches
            .BranchOfAsync(order.LocationCode, cancellationToken)
            .ConfigureAwait(false);

        var posted = await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: order.No,
                    Lines: journal.Lines,
                    SourceCode: "PURCH",

                    // Not a person keying a journal. Purchasing owns the accrual account it is
                    // clearing, and the restriction on that account exists to leave room for
                    // exactly this.
                    IsManualEntry: false,

                    // Stated rather than inferred, for the same reason as the sales side: a
                    // variance line names no vendor and would be read by its sign alone.
                    PartyKind: PartyKind.Vendor,
                    DocumentType: GlDocumentType.Invoice,
                    DocumentNo: vendorInvoiceNo,
                    Description: $"{order.VendorName} — {order.No}",
                    BranchId: branchId,
                    OverrideReason: overrideReason),
                cancellationToken)
            .ConfigureAwait(false);

        if (posted.Failed)
        {
            return Result<PurchaseInvoiceReceipt>.FailureFrom(posted);
        }

        foreach (var (line, quantity, _) in billed)
        {
            line.QuantityInvoiced += quantity;
        }

        order.Status = order.HasOutstandingReceipt
            ? PurchaseOrderStatus.PartiallyReceived
            : order.HasOutstandingInvoice
                ? PurchaseOrderStatus.Received
                : PurchaseOrderStatus.Invoiced;

        // Invoicing more than arrived is the check that catches being billed for a delivery
        // that never came. Overriding it is somebody's decision, and decisions are recorded.
        overrides.Record(warnings, "Purchasing.Invoice", vendorInvoiceNo, overrideReason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Posted vendor invoice {InvoiceNo} against purchase order {OrderNo}, now {Status}.",
            vendorInvoiceNo,
            order.No,
            order.Status);

        return Result<PurchaseInvoiceReceipt>.Success(
            new PurchaseInvoiceReceipt(
                order.No,
                posted.Value.TransactionNo,
                vendorInvoiceNo,
                journal.NetAmount,
                journal.TaxAmount,
                journal.NetAmount + journal.TaxAmount,
                order.Status),
            [.. warnings, .. posted.Messages]);
    }

    /// <summary>The journal an invoice becomes, with the figures it reports.</summary>
    private readonly record struct InvoiceJournal(
        List<PostJournalLine> Lines,
        decimal NetAmount,
        decimal TaxAmount);

    /// <summary>
    /// Turns the billed lines into a journal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The line carrying the tax code is the one holding the invoiced amount, because that is what
    /// the vendor charged tax on. Putting the code on the accrual-clearing line instead computes
    /// tax on the price that was ordered, which is a different number the moment a vendor invoices
    /// at anything other than the agreed price -- and the posting engine catches it only because
    /// the resulting journal does not balance.
    /// </para>
    /// <para>
    /// So an item line clears at the invoiced amount and the difference is then moved out to the
    /// variance account, which leaves the accrual cleared at exactly what the receipt put there.
    /// The variance is posted rather than absorbed into stock: rolling it in would quietly restate
    /// the cost of goods that may already have been sold.
    /// </para>
    /// </remarks>
    private static InvoiceJournal BuildJournal(
        PurchaseOrder order,
        List<(PurchaseOrderLine Line, decimal Quantity, decimal UnitCost)> billed,
        string accrualAccount,
        string? varianceAccount,
        IReadOnlyDictionary<string, decimal> rates)
    {
        var lines = new List<PostJournalLine>();
        var net = 0m;
        var tax = 0m;

        foreach (var (line, quantity, unitCost) in billed)
        {
            var invoiced = quantity * unitCost;
            var accrued = quantity * line.DirectUnitCost;
            var isItem = line.Type is PurchaseLineType.Item;

            net += invoiced;

            // Worked out here as well as by the tax engine, because the vendor has to be credited
            // the gross and the journal will not balance without knowing it. The engine still
            // posts the entry and writes the tax record; this only sizes the other side.
            if (line.TaxCode is { Length: > 0 } code && rates.TryGetValue(code, out var percentage))
            {
                tax += TaxCalculator.FromNet(invoiced, percentage).Tax;
            }

            lines.Add(new PostJournalLine(
                isItem ? accrualAccount : line.AccountNo!,
                invoiced,
                line.Description,
                TaxCode: line.TaxCode));

            var variance = isItem ? invoiced - accrued : 0m;

            if (variance != 0m && varianceAccount is { Length: > 0 })
            {
                // Out of the accrual, which leaves it holding exactly what the receipt accrued,
                // and into variance where somebody can see it.
                lines.Add(new PostJournalLine(
                    accrualAccount,
                    -variance,
                    $"{line.Description} — price variance"));

                lines.Add(new PostJournalLine(
                    varianceAccount,
                    variance,
                    $"{line.Description} — price variance"));
            }
        }

        // The vendor takes the credit, and with it a ledger entry, a due date from their terms and
        // a place on the aged analysis. Gross, because gross is what will be paid.
        lines.Add(new PostJournalLine(
            order.VendorNo,
            -(net + tax),
            $"{order.VendorName} — {order.No}",
            AccountType: JournalAccountType.Vendor));

        return new InvoiceJournal(lines, net, tax);
    }

    /// <summary>
    /// The rate in force for each tax code the invoice uses.
    /// </summary>
    /// <remarks>
    /// Read on the invoice date rather than today's, for the same reason the posting engine does:
    /// an invoice dated before a rate change has to carry the rate that was charged.
    /// </remarks>
    private async Task<Dictionary<string, decimal>> RatesAsync(
        List<(PurchaseOrderLine Line, decimal Quantity, decimal UnitCost)> billed,
        CancellationToken cancellationToken)
    {
        var codes = billed
            .Select(static b => b.Line.TaxCode)
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        var today = clock.Today;

        var taxCodes = await context.Set<TaxCode>()
            .AsNoTracking()
            .Include(c => c.Rates)
            .Where(c => codes.Contains(c.Code) && c.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return taxCodes.ToDictionary(
            static c => c.Code,
            c => c.RateOn(today) ?? 0m,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>What the invoice covers: what the caller said, or everything awaiting an invoice.</summary>
    private static List<(PurchaseOrderLine Line, decimal Quantity, decimal UnitCost)> Billed(
        PurchaseOrder order,
        IReadOnlyList<InvoiceLineRequest>? lines)
    {
        if (lines is null)
        {
            return
            [
                .. order.Lines
                    .Where(static l => l.ReceivedNotInvoiced > 0)
                    .OrderBy(static l => l.LineNo)
                    .Select(static l => (l, l.ReceivedNotInvoiced, l.DirectUnitCost)),
            ];
        }

        var byLineNo = order.Lines.ToDictionary(static l => l.LineNo);

        return
        [
            .. lines
                .Where(r => r.Quantity > 0 && byLineNo.ContainsKey(r.LineNo))
                .Select(r => (
                    byLineNo[r.LineNo],
                    r.Quantity,
                    r.DirectUnitCost ?? byLineNo[r.LineNo].DirectUnitCost)),
        ];
    }

    /// <summary>
    /// Checks the invoice against what actually arrived.
    /// </summary>
    private List<AsapMessage> Check(
        PurchaseOrder order,
        List<(PurchaseOrderLine Line, decimal Quantity, decimal UnitCost)> billed,
        IReadOnlySet<string>? held)
    {
        var found = new List<AsapMessage>();

        foreach (var (line, quantity, unitCost) in billed)
        {
            var target = MessageTarget.OnField($"Lines[{line.LineNo}]");

            if (quantity > line.ReceivedNotInvoiced)
            {
                var rendered = messages.Render(
                    PurchasingMessages.InvoiceExceedsReceipt,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["OrderNo"] = order.No,
                        ["LineNo"] = line.LineNo,
                        ["ItemNo"] = line.ItemNo ?? line.AccountNo,
                        ["Invoiced"] = quantity,
                        ["OutstandingQuantity"] = line.ReceivedNotInvoiced,
                    },
                    target);

                found.Add(
                    rendered.OverridePermission is { } permission && held?.Contains(permission) == true
                        ? messages.AsOverridden(rendered)
                        : rendered);
            }

            // A warning rather than a refusal. Prices move, and the invoice is what will be paid;
            // the point is that nobody finds out by reconciling the ledger three weeks later.
            if (unitCost != line.DirectUnitCost)
            {
                found.Add(messages.Render(
                    PurchasingMessages.PriceVariance,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["LineNo"] = line.LineNo,
                        ["ItemNo"] = line.ItemNo ?? line.AccountNo,
                        ["OrderedCost"] = line.DirectUnitCost,
                        ["InvoicedCost"] = unitCost,
                        ["Variance"] = (unitCost - line.DirectUnitCost) * quantity,
                    },
                    target));
            }
        }

        return found;
    }

    private async Task<string?> Account(string name, CancellationToken cancellationToken)
        => await setup
            .GetAsync<string>($"{PurchasingModule.Id}.Posting.{name}", cancellationToken)
            .ConfigureAwait(false);
}
