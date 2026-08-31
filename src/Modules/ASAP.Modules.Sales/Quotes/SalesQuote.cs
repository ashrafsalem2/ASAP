using ASAP.Modules.Sales.Orders;
using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Sales.Quotes;

/// <summary>Where a quote stands.</summary>
public enum SalesQuoteStatus
{
    /// <summary>Being prepared. Nothing has been sent to the customer.</summary>
    Draft = 0,

    /// <summary>Sent, and waiting on an answer.</summary>
    Sent = 1,

    /// <summary>Accepted, and turned into an order.</summary>
    Accepted = 2,

    /// <summary>The customer said no. Kept, because why we lost is worth knowing.</summary>
    Declined = 3,

    /// <summary>Nobody answered before it ran out.</summary>
    Expired = 4,
}

/// <summary>
/// A price offered to a customer, before anybody has committed to anything.
/// </summary>
/// <remarks>
/// <para>
/// A quote is a promise about price, and nothing else. It reserves no stock, moves nothing and
/// posts nothing. That is why quoting for goods that are not on the shelf is ordinary rather than
/// an error: a lead time exists precisely so that somebody can sell what has not arrived yet.
/// </para>
/// <para>
/// It carries an expiry, and the expiry is the point. Costs move, suppliers put prices up, and a
/// quote with no end date is a price the company is bound to for ever — the one nobody remembers
/// is always found by a customer holding a piece of paper from two years ago.
/// </para>
/// <para>
/// It is held apart from the sales order rather than being a status on one. A quote that lived as
/// an order status would appear in the order book, the open-orders report and the shipping queue,
/// and every one of those would have to remember to exclude it. Forgetting once makes a report
/// wrong in a way nobody notices, because the numbers still look like numbers.
/// </para>
/// </remarks>
public sealed class SalesQuote : CompanyEntity
{
    /// <summary>The quote number, issued from a number series.</summary>
    public required string No { get; set; }

    /// <summary>The customer it was offered to.</summary>
    public required string CustomerNo { get; set; }

    /// <summary>Their name when it was quoted, copied so a reprint reads as it did.</summary>
    public required string CustomerName { get; set; }

    /// <summary>Where it stands.</summary>
    public SalesQuoteStatus Status { get; set; } = SalesQuoteStatus.Draft;

    /// <summary>The day it was quoted.</summary>
    public DateOnly QuoteDate { get; set; }

    /// <summary>
    /// The last day the prices on it stand.
    /// </summary>
    /// <remarks>
    /// Always set. A quote that never ran out would be a price nobody could ever withdraw.
    /// </remarks>
    public DateOnly ValidUntil { get; set; }

    /// <summary>Where it would ship from, if it becomes an order.</summary>
    public string? LocationCode { get; set; }

    /// <summary>Their own reference.</summary>
    public string? CustomerOrderNo { get; set; }

    /// <summary>A note.</summary>
    public string? Description { get; set; }

    /// <summary>The order it became, once accepted.</summary>
    public string? OrderNo { get; set; }

    /// <summary>Why the customer said no, where they said.</summary>
    public string? DeclineReason { get; set; }

    /// <summary>What is on it.</summary>
    public ICollection<SalesQuoteLine> Lines { get; set; } = [];

    /// <summary>What it comes to after discount, before tax.</summary>
    public decimal TotalAmount => Lines.Sum(static l => l.LineAmount);

    /// <summary>Whether the prices still stand on a given day.</summary>
    /// <param name="on">The day.</param>
    /// <returns>True while it is in force.</returns>
    public bool StandsOn(DateOnly on) => on <= ValidUntil;

    /// <summary>Whether its lines may still be changed.</summary>
    /// <remarks>
    /// An accepted quote is frozen. It is the record of what the customer agreed to, and the order
    /// beside it is the record of what was done about that — editing either afterwards would leave
    /// nothing able to say what was actually offered.
    /// </remarks>
    public bool IsEditable => Status is SalesQuoteStatus.Draft or SalesQuoteStatus.Sent;
}

/// <summary>One line of a quote.</summary>
/// <remarks>
/// The same shape as a sales order line, minus everything about fulfilment. Nothing here has
/// shipped, been invoiced or come back, because nothing has happened yet.
/// </remarks>
public sealed class SalesQuoteLine : CompanyEntity
{
    /// <summary>The quote this line belongs to.</summary>
    public Guid SalesQuoteId { get; set; }

    /// <summary>Navigation to the quote.</summary>
    public SalesQuote? SalesQuote { get; set; }

    /// <summary>Position on the quote, in tens.</summary>
    public int LineNo { get; set; }

    /// <summary>Whether this quotes stock or a charge.</summary>
    public SalesLineType Type { get; set; }

    /// <summary>The item number, on an item line.</summary>
    public string? ItemNo { get; set; }

    /// <summary>Which variant, on an item stocked as separate colours or sizes.</summary>
    public string? VariantCode { get; set; }

    /// <summary>The account number, on a charge line.</summary>
    public string? AccountNo { get; set; }

    /// <summary>What it is, as it should read on the quote.</summary>
    public required string Description { get; set; }

    /// <summary>Where this line would ship from, when it differs from the quote.</summary>
    public string? LocationCode { get; set; }

    /// <summary>How much was quoted for.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The price offered per unit, before tax and before any discount.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>A discount off this line, as a percentage.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>The tax code, which decides what would be charged on top.</summary>
    public string? TaxCode { get; set; }

    /// <summary>What one unit comes to after its discount.</summary>
    public decimal NetUnitPrice => UnitPrice * (1m - (DiscountPercent / 100m));

    /// <summary>What the line comes to before tax.</summary>
    public decimal LineAmount => Quantity * NetUnitPrice;
}
