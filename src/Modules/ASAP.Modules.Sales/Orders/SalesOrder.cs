using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Sales.Orders;

/// <summary>Where a sales order stands.</summary>
public enum SalesOrderStatus
{
    /// <summary>Being prepared. Nothing has been promised to the customer.</summary>
    Open = 0,

    /// <summary>Confirmed with the customer and awaiting despatch.</summary>
    Released = 1,

    /// <summary>Some of it has gone.</summary>
    PartiallyShipped = 2,

    /// <summary>All of it has gone, and some or none of it is invoiced.</summary>
    Shipped = 3,

    /// <summary>Everything shipped has been invoiced. The order is finished.</summary>
    Invoiced = 4,

    /// <summary>Abandoned. Kept rather than deleted, because somebody will ask why.</summary>
    Cancelled = 5,
}

/// <summary>What a sales order line is selling.</summary>
public enum SalesLineType
{
    /// <summary>Stock. Shipping it moves inventory and charges its cost to cost of sales.</summary>
    Item = 0,

    /// <summary>
    /// A charge with no stock behind it: delivery, installation, a service. It reaches the general
    /// ledger directly and never touches the item ledger.
    /// </summary>
    GlAccount = 1,
}

/// <summary>
/// An order taken from a customer.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of a purchase order, and it keeps the same shape for the same reason: goods and
/// paperwork go out on their own schedules. A part load leaves on Tuesday, the invoice covers the
/// whole order on Friday, a credit note follows. Quantity shipped and quantity invoiced are held
/// separately on each line so all of that can be represented.
/// </para>
/// <para>
/// Where it differs from purchasing is what each step posts. A shipment charges the cost of what
/// left to cost of sales, valued by the costing engine rather than by anything on this document —
/// the price the customer pays and the cost the goods carry are unrelated numbers, and confusing
/// them is how a margin report comes to be fiction.
/// </para>
/// </remarks>
public sealed class SalesOrder : CompanyEntity
{
    /// <summary>The order number, issued from a number series.</summary>
    public required string No { get; set; }

    /// <summary>The customer's key in the Finance module.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>The customer number, copied at creation so a list needs no join.</summary>
    public required string CustomerNo { get; set; }

    /// <summary>Their name as it stood when the order was taken.</summary>
    public required string CustomerName { get; set; }

    /// <summary>When the order was taken.</summary>
    public DateOnly OrderDate { get; set; }

    /// <summary>When the customer expects it.</summary>
    public DateOnly? RequestedDeliveryDate { get; set; }

    /// <summary>Where the goods ship from. Every item line takes stock from here.</summary>
    public string? LocationCode { get; set; }

    /// <summary>Where the order stands.</summary>
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Open;

    /// <summary>The customer's own order number, which is what they will quote back.</summary>
    public string? CustomerOrderNo { get; set; }

    /// <summary>A note for whoever picks and packs it.</summary>
    public string? Description { get; set; }

    /// <summary>The lines on the order.</summary>
    public ICollection<SalesOrderLine> Lines { get; set; } = [];

    /// <summary>Whether lines may still be added or changed.</summary>
    /// <remarks>
    /// Closed as soon as anything ships. What has left the building is a fact, and an order that
    /// could restate it afterwards would let somebody quietly change what a customer received.
    /// </remarks>
    public bool IsEditable => Status is SalesOrderStatus.Open or SalesOrderStatus.Released;

    /// <summary>Whether anything on the order is still to go out.</summary>
    public bool HasOutstandingShipment => Lines.Any(static l => l.OutstandingToShip > 0);

    /// <summary>Whether anything shipped is still waiting to be invoiced.</summary>
    public bool HasOutstandingInvoice => Lines.Any(static l => l.ShippedNotInvoiced > 0);
}

/// <summary>One thing being sold.</summary>
public sealed class SalesOrderLine : CompanyEntity
{
    /// <summary>The order this line belongs to.</summary>
    public Guid SalesOrderId { get; set; }

    /// <summary>Navigation to the order.</summary>
    public SalesOrder? SalesOrder { get; set; }

    /// <summary>Position on the order, in tens so a line can be inserted between two others.</summary>
    public int LineNo { get; set; }

    /// <summary>Whether this sells stock or a charge.</summary>
    public SalesLineType Type { get; set; }

    /// <summary>The item number, on an item line.</summary>
    public string? ItemNo { get; set; }

    /// <summary>
    /// Which variant, on an item stocked as separate colours or sizes.
    /// </summary>
    /// <remarks>
    /// A sales order for shirts that does not say which size cannot be shipped, and the picking
    /// shelf is a poor place to find that out. Required on a variant item and refused elsewhere,
    /// exactly as it is on the stock movement the shipment eventually posts.
    /// </remarks>
    public string? VariantCode { get; set; }

    /// <summary>The account number, on a charge line.</summary>
    public string? AccountNo { get; set; }

    /// <summary>What it is, as it should read on the invoice.</summary>
    public required string Description { get; set; }

    /// <summary>Where this line ships from, when it differs from the order.</summary>
    public string? LocationCode { get; set; }

    /// <summary>How much was ordered.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The agreed price per unit, before tax and before any discount.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// A discount off this line, as a percentage.
    /// </summary>
    /// <remarks>
    /// Kept as the percentage rather than folded into the price, so an invoice can show what was
    /// given away. A discount absorbed into the unit price is a discount nobody can report on.
    /// </remarks>
    public decimal DiscountPercent { get; set; }

    /// <summary>The tax code, which decides what is charged on top.</summary>
    public string? TaxCode { get; set; }

    /// <summary>How much has gone out.</summary>
    public decimal QuantityShipped { get; set; }

    /// <summary>How much has been invoiced.</summary>
    public decimal QuantityInvoiced { get; set; }

    /// <summary>What one unit comes to after its discount.</summary>
    public decimal NetUnitPrice => UnitPrice * (1m - (DiscountPercent / 100m));

    /// <summary>What the line comes to before tax.</summary>
    public decimal LineAmount => Quantity * NetUnitPrice;

    /// <summary>What the discount on this line is worth.</summary>
    public decimal DiscountAmount => Quantity * UnitPrice * (DiscountPercent / 100m);

    /// <summary>How much is still to go out.</summary>
    public decimal OutstandingToShip => Quantity - QuantityShipped;

    /// <summary>
    /// How much has gone out but not yet been invoiced.
    /// </summary>
    /// <remarks>
    /// The customer has the goods and has not been asked to pay for them. It is the first thing to
    /// look at when revenue for a month seems low, and the reason shipping and invoicing are
    /// tracked apart rather than as one act.
    /// </remarks>
    public decimal ShippedNotInvoiced => QuantityShipped - QuantityInvoiced;
}
