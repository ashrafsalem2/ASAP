using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Tax;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Sales.Orders;

/// <summary>How much of one line is being invoiced.</summary>
/// <param name="LineNo">The order line.</param>
/// <param name="Quantity">How much the invoice covers. Always positive.</param>
public readonly record struct SalesInvoiceLineRequest(int LineNo, decimal Quantity);

/// <summary>What an invoice posted.</summary>
/// <param name="OrderNo">The order invoiced.</param>
/// <param name="TransactionNo">The transaction the entries were posted under.</param>
/// <param name="DocumentNo">The invoice number ASAP issued.</param>
/// <param name="NetAmount">The goods and charges, after discount and before tax.</param>
/// <param name="DiscountAmount">What was given away.</param>
/// <param name="TaxAmount">The tax charged.</param>
/// <param name="TotalAmount">What the customer owes.</param>
/// <param name="Status">Where the order stands now.</param>
public readonly record struct SalesInvoiceReceipt(
    string OrderNo,
    long TransactionNo,
    string DocumentNo,
    decimal NetAmount,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    SalesOrderStatus Status);

/// <summary>
/// Turns goods that have shipped into a debt the customer owes.
/// </summary>
/// <remarks>
/// <para>
/// The posting is the mirror of a purchase invoice, read from the other side:
/// </para>
/// <code>
///   Customer (receivables)   debit    the total, and an entry on their account
///   Sales revenue            credit   what was sold, at list
///   Discounts given          debit    what was given away, kept visible
///   VAT payable              credit   added by the tax engine from the line's code
/// </code>
/// <para>
/// Revenue is credited at list and the discount debited separately, rather than netting the two.
/// The profit is identical either way; the difference is that one of them can answer how much the
/// company discounted last quarter and the other cannot.
/// </para>
/// <para>
/// Cost of sales is not posted here. It was charged when the goods shipped, which is when the
/// company stopped owning them — and an invoice raised a fortnight later must not restate it.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="orders">Loads the order.</param>
/// <param name="documents">Posts the journal, as a document rather than by hand.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="overrides">Records every protection this invoice pushed past.</param>
/// <param name="numbers">Issues the invoice number.</param>
/// <param name="setup">Supplies the revenue and discount accounts.</param>
/// <param name="clock">Supplies the date the tax rate is read on.</param>
/// <param name="logger">Records invoices.</param>
public sealed class SalesInvoiceService(
    AsapDbContext context,
    SalesOrderService orders,
    DocumentPostingService documents,
    IMessageCatalog messages,
    OverrideAuditor overrides,
    INumberSeriesService numbers,
    ISetupService setup,
    IClock clock,
    ILogger<SalesInvoiceService> logger)
{
    /// <summary>
    /// Posts an invoice against an order.
    /// </summary>
    /// <param name="orderNo">The order being invoiced.</param>
    /// <param name="lines">What the invoice covers, or null for everything shipped and unbilled.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was posted, or every reason it was refused.</returns>
    public async Task<Result<SalesInvoiceReceipt>> PostAsync(
        string orderNo,
        IReadOnlyList<SalesInvoiceLineRequest>? lines = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        var order = await orders.LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result<SalesInvoiceReceipt>.FailureFrom(orders.NotFound(orderNo));
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OrderNo"] = order.No,
            ["Status"] = order.Status.ToString(),
        };

        var billed = Billed(order, lines);

        if (billed.Count == 0)
        {
            return Result<SalesInvoiceReceipt>.Failure(
                messages.Render(SalesMessages.NothingToInvoice, arguments));
        }

        var found = Check(order, billed, heldOverridePermissions);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<SalesInvoiceReceipt>.Failure(found);
        }

        var revenueAccount = await Account("RevenueAccount", cancellationToken).ConfigureAwait(false);
        var discountAccount = await Account("DiscountAccount", cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(revenueAccount))
        {
            return Result<SalesInvoiceReceipt>.Failure(
                messages.Render(SalesMessages.NoRevenueAccount, arguments));
        }

        var today = clock.Today;
        var seriesCode = await SeriesCodeAsync(cancellationToken).ConfigureAwait(false);
        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<SalesInvoiceReceipt>.FailureFrom(numbered);
        }

        var rates = await RatesAsync(billed, cancellationToken).ConfigureAwait(false);
        var journal = BuildJournal(order, billed, revenueAccount, discountAccount, rates);

        var posted = await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: order.No,
                    Lines: journal.Lines,
                    SourceCode: "SALES",

                    // Not a person keying a journal. Sales owns the revenue and discount accounts
                    // it is writing to, and the whole reason those accounts refuse direct posting
                    // is to leave the writing to this.
                    IsManualEntry: false,
                    DocumentType: GlDocumentType.Invoice,
                    DocumentNo: numbered.Value,
                    Description: $"{order.CustomerName} — {order.No}",
                    OverrideReason: overrideReason),
                cancellationToken)
            .ConfigureAwait(false);

        if (posted.Failed)
        {
            return Result<SalesInvoiceReceipt>.FailureFrom(posted);
        }

        foreach (var (line, quantity) in billed)
        {
            line.QuantityInvoiced += quantity;
        }

        order.Status = order.HasOutstandingShipment
            ? SalesOrderStatus.PartiallyShipped
            : order.HasOutstandingInvoice
                ? SalesOrderStatus.Shipped
                : SalesOrderStatus.Invoiced;

        overrides.Record(found, "Sales.Invoice", numbered.Value, overrideReason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Posted sales invoice {InvoiceNo} against order {OrderNo}, now {Status}.",
            numbered.Value,
            order.No,
            order.Status);

        return Result<SalesInvoiceReceipt>.Success(
            new SalesInvoiceReceipt(
                order.No,
                posted.Value.TransactionNo,
                numbered.Value,
                journal.NetAmount,
                journal.DiscountAmount,
                journal.TaxAmount,
                journal.NetAmount + journal.TaxAmount,
                order.Status),
            [.. found, .. posted.Messages]);
    }

    /// <summary>The journal an invoice becomes, with the figures it reports.</summary>
    private readonly record struct InvoiceJournal(
        List<PostJournalLine> Lines,
        decimal NetAmount,
        decimal DiscountAmount,
        decimal TaxAmount);

    /// <summary>
    /// Turns the billed lines into a journal.
    /// </summary>
    /// <remarks>
    /// The tax code sits on the revenue line, whose amount is the net after discount — which is
    /// what the customer is charged tax on. Putting it on the gross would tax money nobody ever
    /// asked for.
    /// </remarks>
    private static InvoiceJournal BuildJournal(
        SalesOrder order,
        List<(SalesOrderLine Line, decimal Quantity)> billed,
        string revenueAccount,
        string? discountAccount,
        IReadOnlyDictionary<string, decimal> rates)
    {
        var lines = new List<PostJournalLine>();
        var net = 0m;
        var discount = 0m;
        var tax = 0m;

        foreach (var (line, quantity) in billed)
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

            // Revenue at list where a discount is shown separately, at net where it is not.
            lines.Add(new PostJournalLine(
                account,
                -(showDiscount ? lineNet + lineDiscount : lineNet),
                line.Description,
                TaxCode: line.TaxCode));

            if (showDiscount)
            {
                // The discount carries the tax code too. Neither line alone is the taxable
                // amount -- revenue is at list and the discount is a contra -- so taxing each and
                // letting them offset is what leaves tax charged on what the customer actually
                // pays. Taxing only one of them charges tax on a figure nobody was billed.
                lines.Add(new PostJournalLine(
                    discountAccount!,
                    lineDiscount,
                    $"{line.Description} — discount",
                    TaxCode: line.TaxCode));
            }
        }

        // The customer takes the debit, and with it a ledger entry, a due date from their terms,
        // a place on the aged analysis and a credit-limit check on the way through.
        lines.Add(new PostJournalLine(
            order.CustomerNo,
            net + tax,
            $"{order.CustomerName} — {order.No}",
            AccountType: JournalAccountType.Customer,
            ExternalDocumentNo: order.CustomerOrderNo));

        return new InvoiceJournal(lines, net, discount, tax);
    }

    /// <summary>What the invoice covers: what the caller said, or everything shipped and unbilled.</summary>
    private static List<(SalesOrderLine Line, decimal Quantity)> Billed(
        SalesOrder order,
        IReadOnlyList<SalesInvoiceLineRequest>? lines)
    {
        if (lines is null)
        {
            return
            [
                .. order.Lines
                    .Where(static l => l.ShippedNotInvoiced > 0)
                    .OrderBy(static l => l.LineNo)
                    .Select(static l => (l, l.ShippedNotInvoiced)),
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

    private List<AsapMessage> Check(
        SalesOrder order,
        List<(SalesOrderLine Line, decimal Quantity)> billed,
        IReadOnlySet<string>? held)
    {
        var found = new List<AsapMessage>();

        foreach (var (line, quantity) in billed.Where(b => b.Quantity > b.Line.ShippedNotInvoiced))
        {
            var rendered = messages.Render(
                SalesMessages.InvoiceExceedsShipment,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OrderNo"] = order.No,
                    ["LineNo"] = line.LineNo,
                    ["ItemNo"] = line.ItemNo ?? line.AccountNo,
                    ["Invoiced"] = quantity,
                    ["Outstanding"] = line.ShippedNotInvoiced,
                },
                MessageTarget.OnField($"Lines[{line.LineNo}]"));

            found.Add(
                rendered.OverridePermission is { } permission && held?.Contains(permission) == true
                    ? messages.AsOverridden(rendered)
                    : rendered);
        }

        return found;
    }

    private async Task<Dictionary<string, decimal>> RatesAsync(
        List<(SalesOrderLine Line, decimal Quantity)> billed,
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

    private async Task<string> SeriesCodeAsync(CancellationToken cancellationToken)
        => await setup
               .GetAsync<string>($"{SalesModule.Id}.Invoices.NumberSeries", cancellationToken)
               .ConfigureAwait(false)
           ?? "SALES-INV";

    private async Task<string?> Account(string name, CancellationToken cancellationToken)
        => await setup
            .GetAsync<string>($"{SalesModule.Id}.Posting.{name}", cancellationToken)
            .ConfigureAwait(false);
}
