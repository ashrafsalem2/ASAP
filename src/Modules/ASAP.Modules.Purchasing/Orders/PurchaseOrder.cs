using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Purchasing.Orders;

/// <summary>Where a purchase order stands.</summary>
public enum PurchaseOrderStatus
{
    /// <summary>Being prepared. Nothing has been committed to the vendor.</summary>
    Open = 0,

    /// <summary>Sent to the vendor and awaiting delivery.</summary>
    Released = 1,

    /// <summary>
    /// Waiting for somebody with the authority to sign for it.
    /// </summary>
    /// <remarks>
    /// A status of its own rather than a flag on Open, because the two mean different things to
    /// the person who raised it: an open order is theirs to finish, and one waiting for approval
    /// is out of their hands.
    /// </remarks>
    PendingApproval = 6,

    /// <summary>Turned down, with a reason. Kept, because somebody will ask.</summary>
    Rejected = 7,

    /// <summary>Some of it has arrived.</summary>
    PartiallyReceived = 2,

    /// <summary>All of it has arrived, and some or none of it is invoiced.</summary>
    Received = 3,

    /// <summary>Everything received has been invoiced. The order is finished.</summary>
    Invoiced = 4,

    /// <summary>Abandoned. Kept rather than deleted, because somebody will ask why.</summary>
    Cancelled = 5,
}

/// <summary>What a purchase order line is buying.</summary>
public enum PurchaseLineType
{
    /// <summary>Stock. Receiving it moves inventory and values it.</summary>
    Item = 0,

    /// <summary>
    /// A cost with no stock behind it: rent, a subscription, professional fees. It reaches the
    /// general ledger directly and never touches the item ledger.
    /// </summary>
    GlAccount = 1,
}

/// <summary>
/// An order placed with a vendor.
/// </summary>
/// <remarks>
/// <para>
/// The order itself posts nothing. It is a statement of intent, and the two things that do post --
/// the receipt and the invoice — are tracked separately on each line rather than as a single
/// status on the header. That separation is the whole point of the document.
/// </para>
/// <para>
/// Goods and paperwork arrive on their own schedules. A lorry turns up with eight of the ten
/// ordered; the invoice comes a fortnight later for all ten; a credit note follows. Any model that
/// assumes receiving and invoicing happen together, or in that order, or exactly once, breaks on
/// an ordinary Tuesday. Holding quantity received and quantity invoiced independently on the line
/// is what makes the three-way match — order, goods, invoice — something the system can actually
/// perform.
/// </para>
/// </remarks>
public sealed class PurchaseOrder : CompanyEntity
{
    /// <summary>The order number, issued from a number series.</summary>
    public required string No { get; set; }

    /// <summary>The vendor's key in the Finance module.</summary>
    public Guid VendorId { get; set; }

    /// <summary>The vendor number, copied at creation so a list needs no join.</summary>
    public required string VendorNo { get; set; }

    /// <summary>Their name as it stood when the order was raised.</summary>
    public required string VendorName { get; set; }

    /// <summary>When the order was placed.</summary>
    public DateOnly OrderDate { get; set; }

    /// <summary>When the goods are expected.</summary>
    public DateOnly? ExpectedReceiptDate { get; set; }

    /// <summary>Where the goods are going. Every item line receives here.</summary>
    public string? LocationCode { get; set; }

    /// <summary>Where the order stands.</summary>
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Open;

    /// <summary>The vendor's own reference for this order.</summary>
    public string? VendorOrderNo { get; set; }

    /// <summary>A note for whoever handles it.</summary>
    public string? Description { get; set; }

    /// <summary>The lines on the order.</summary>
    public ICollection<PurchaseOrderLine> Lines { get; set; } = [];

    /// <summary>Whether lines may still be added or changed.</summary>
    /// <remarks>
    /// Closed as soon as anything is received. Editing a line that goods have already arrived
    /// against would silently restate what was received, and the received quantity is a fact
    /// about the world rather than a figure the order gets to decide.
    /// </remarks>
    public bool IsEditable => Status is PurchaseOrderStatus.Open or PurchaseOrderStatus.Released;

    /// <summary>What the order comes to, which is what an approval limit is measured against.</summary>
    public decimal TotalAmount => Lines.Sum(static l => l.LineAmount);

    /// <summary>Who raised it, so they can be stopped from approving it themselves.</summary>
    public Guid? RaisedByUserId { get; set; }

    /// <summary>Who signed for it, on an order that needed signing.</summary>
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>Their name, copied so the record reads without a join.</summary>
    public string? ApprovedByUserName { get; set; }

    /// <summary>When they signed.</summary>
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>
    /// What the order came to when it was approved.
    /// </summary>
    /// <remarks>
    /// Frozen at the moment of signing. An approval is authority for an amount, not for an order
    /// number, so anything that changes the total afterwards has to ask again -- otherwise a
    /// five thousand order approved on Monday becomes a five hundred thousand order on Tuesday
    /// with a signature still attached to it.
    /// </remarks>
    public decimal? ApprovedAmount { get; set; }

    /// <summary>Why it was turned down, where it was.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Whether anything on the order is still to arrive.</summary>
    public bool HasOutstandingReceipt => Lines.Any(static l => l.OutstandingToReceive > 0);

    /// <summary>Whether anything received is still waiting for its invoice.</summary>
    public bool HasOutstandingInvoice => Lines.Any(static l => l.ReceivedNotInvoiced > 0);
}

/// <summary>One thing being bought.</summary>
public sealed class PurchaseOrderLine : CompanyEntity
{
    /// <summary>The order this line belongs to.</summary>
    public Guid PurchaseOrderId { get; set; }

    /// <summary>Navigation to the order.</summary>
    public PurchaseOrder? PurchaseOrder { get; set; }

    /// <summary>Position on the order, in tens so a line can be inserted between two others.</summary>
    public int LineNo { get; set; }

    /// <summary>Whether this buys stock or a cost.</summary>
    public PurchaseLineType Type { get; set; }

    /// <summary>The item number, on an item line.</summary>
    public string? ItemNo { get; set; }

    /// <summary>
    /// Which variant, on an item stocked as separate colours or sizes.
    /// </summary>
    /// <remarks>
    /// Required there and refused elsewhere, exactly as it is on a stock movement. A purchase order
    /// for shirts that does not say which size is not an order anybody can receive against, and the
    /// goods-in door is a poor place to discover that.
    /// </remarks>
    public string? VariantCode { get; set; }

    /// <summary>The account number, on a cost line.</summary>
    public string? AccountNo { get; set; }

    /// <summary>What it is, as it should read on the ledger.</summary>
    public required string Description { get; set; }

    /// <summary>Where this line's goods are going, when it differs from the order.</summary>
    public string? LocationCode { get; set; }

    /// <summary>How much was ordered.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The agreed price per unit, before tax.</summary>
    public decimal DirectUnitCost { get; set; }

    /// <summary>The tax code, which decides what is added and what may be reclaimed.</summary>
    public string? TaxCode { get; set; }

    /// <summary>How much has arrived.</summary>
    public decimal QuantityReceived { get; set; }

    /// <summary>How much has been invoiced.</summary>
    public decimal QuantityInvoiced { get; set; }

    /// <summary>What the line comes to before tax.</summary>
    public decimal LineAmount => Quantity * DirectUnitCost;

    /// <summary>How much is still to arrive.</summary>
    public decimal OutstandingToReceive => Quantity - QuantityReceived;

    /// <summary>
    /// How much has arrived but not yet been invoiced.
    /// </summary>
    /// <remarks>
    /// The figure the goods-received-not-invoiced accrual is built from, and the reason a receipt
    /// posts to its own account rather than straight to payables: the company owes for these goods
    /// from the moment they arrive, and knowing that before the paperwork catches up is the
    /// difference between a balance sheet that is right at month end and one that is a fortnight
    /// behind.
    /// </remarks>
    public decimal ReceivedNotInvoiced => QuantityReceived - QuantityInvoiced;
}
