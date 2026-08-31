using ASAP.Modules.Purchasing.Orders;
using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Purchasing.Requisitions;

/// <summary>Where a requisition stands.</summary>
public enum PurchaseRequisitionStatus
{
    /// <summary>Being written. Nobody has been asked for anything.</summary>
    Draft = 0,

    /// <summary>Sent for approval and waiting on an answer.</summary>
    Submitted = 1,

    /// <summary>Signed for. Orders may be raised from it.</summary>
    Approved = 2,

    /// <summary>Turned down. Kept, because why is worth knowing.</summary>
    Rejected = 3,

    /// <summary>Everything on it has been ordered.</summary>
    Ordered = 4,

    /// <summary>Abandoned before it became anything.</summary>
    Cancelled = 5,
}

/// <summary>
/// A request for something to be bought.
/// </summary>
/// <remarks>
/// <para>
/// The thing a purchase order is not. An order names a vendor, a price and a commitment; a
/// requisition names <em>a need</em>. Who to buy from may not be known yet, and what it will cost
/// is a guess by whoever is asking.
/// </para>
/// <para>
/// One requisition becomes as many orders as it has vendors. A shop asking for paper, bolts and a
/// kettle is asking one question and will get three answers, and the requisition has to survive
/// being answered in pieces -- which is why each line tracks how much of it has been turned into
/// an order, and why that counter is the only thing standing between a line and being bought
/// twice.
/// </para>
/// <para>
/// Approving a requisition is not approving the orders that come out of it. The approval is
/// measured against an estimate somebody typed; the orders are measured against real prices from
/// a real vendor, through the order's own approval. Letting a requisition approved at an estimate
/// authorise an order at any price would make the estimate the control, and an estimate is the
/// one number on the document nobody has checked.
/// </para>
/// </remarks>
public sealed class PurchaseRequisition : CompanyEntity
{
    /// <summary>The requisition number, issued from a number series.</summary>
    public required string No { get; set; }

    /// <summary>Where it stands.</summary>
    public PurchaseRequisitionStatus Status { get; set; } = PurchaseRequisitionStatus.Draft;

    /// <summary>The day it was raised.</summary>
    public DateOnly RequisitionDate { get; set; }

    /// <summary>When the goods are wanted by.</summary>
    public DateOnly? NeededByDate { get; set; }

    /// <summary>Where the goods are wanted.</summary>
    public string? LocationCode { get; set; }

    /// <summary>What it is for.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Why it is needed.
    /// </summary>
    /// <remarks>
    /// Separate from the description because they answer different questions, and the second one
    /// is what an approver actually reads. "Twelve reams of paper" is a description; "the Jeddah
    /// shop has run out and the printer is the till receipt printer" is a reason to sign.
    /// </remarks>
    public string? Justification { get; set; }

    /// <summary>Who asked, so they can be stopped from approving it themselves.</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>Their name, copied so the record reads without a join.</summary>
    public string? RequestedByUserName { get; set; }

    /// <summary>Who signed for it.</summary>
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>Their name.</summary>
    public string? ApprovedByUserName { get; set; }

    /// <summary>When they signed.</summary>
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>
    /// What it was estimated at when it was approved.
    /// </summary>
    /// <remarks>
    /// Frozen at the moment of signing, exactly as it is on an order. An approval is authority for
    /// an amount rather than for a document number, so anything that moves the total afterwards
    /// has to ask again.
    /// </remarks>
    public decimal? ApprovedAmount { get; set; }

    /// <summary>Why it was turned down, where it was.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>What is being asked for.</summary>
    public ICollection<PurchaseRequisitionLine> Lines { get; set; } = [];

    /// <summary>What somebody thinks it will come to.</summary>
    public decimal EstimatedAmount => Lines.Sum(static l => l.EstimatedAmount);

    /// <summary>Whether its lines may still be changed.</summary>
    public bool IsEditable => Status is PurchaseRequisitionStatus.Draft;

    /// <summary>Whether orders may still be raised from it.</summary>
    public bool CanBeOrdered
        => Status is PurchaseRequisitionStatus.Approved
            && Lines.Any(static l => l.OutstandingToOrder > 0m);

    /// <summary>Whether anything on it is still waiting to be ordered.</summary>
    public bool HasOutstandingLines => Lines.Any(static l => l.OutstandingToOrder > 0m);
}

/// <summary>One thing being asked for.</summary>
public sealed class PurchaseRequisitionLine : CompanyEntity
{
    /// <summary>The requisition this line belongs to.</summary>
    public Guid PurchaseRequisitionId { get; set; }

    /// <summary>Navigation to the requisition.</summary>
    public PurchaseRequisition? PurchaseRequisition { get; set; }

    /// <summary>Position on it, in tens.</summary>
    public int LineNo { get; set; }

    /// <summary>Whether this asks for stock or a cost.</summary>
    public PurchaseLineType Type { get; set; }

    /// <summary>The item number, on an item line.</summary>
    public string? ItemNo { get; set; }

    /// <summary>Which variant, on an item that has them.</summary>
    public string? VariantCode { get; set; }

    /// <summary>The account number, on a cost line.</summary>
    public string? AccountNo { get; set; }

    /// <summary>What is wanted, in words.</summary>
    public required string Description { get; set; }

    /// <summary>Where this line's goods are wanted, when it differs from the requisition.</summary>
    public string? LocationCode { get; set; }

    /// <summary>How much is wanted.</summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// What whoever is asking thinks it costs.
    /// </summary>
    /// <remarks>
    /// A guess, and named as one. It is what the approval is measured against and it is not what
    /// anybody will be charged -- the order that follows carries the real price and goes through
    /// its own approval on that figure.
    /// </remarks>
    public decimal EstimatedUnitCost { get; set; }

    /// <summary>
    /// A vendor somebody suggests, where they have one in mind.
    /// </summary>
    /// <remarks>
    /// A suggestion and nothing more. The requisition does not commit the company to a vendor;
    /// whoever raises the order decides, and may well decide differently.
    /// </remarks>
    public string? SuggestedVendorNo { get; set; }

    /// <summary>
    /// How much of this line has been turned into an order.
    /// </summary>
    /// <remarks>
    /// The counter that stops a line being bought twice. One requisition can become several
    /// orders, so nothing else knows how much of a line is already committed.
    /// </remarks>
    public decimal QuantityOrdered { get; set; }

    /// <summary>What this line is estimated at.</summary>
    public decimal EstimatedAmount => Quantity * EstimatedUnitCost;

    /// <summary>How much of it is still waiting to be ordered.</summary>
    public decimal OutstandingToOrder => Quantity - QuantityOrdered;
}
