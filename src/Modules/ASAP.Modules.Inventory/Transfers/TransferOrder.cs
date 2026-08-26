using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Transfers;

/// <summary>How far along a transfer is.</summary>
public enum TransferStatus
{
    /// <summary>Being prepared. Nothing has moved.</summary>
    Open = 0,

    /// <summary>
    /// Approved and waiting to be picked. Distinct from open because the branch requesting the
    /// goods and the branch sending them are usually not the same people.
    /// </summary>
    Released = 1,

    /// <summary>Goods have left the source and are in transit.</summary>
    Shipped = 2,

    /// <summary>Some lines have arrived and others have not.</summary>
    PartiallyReceived = 3,

    /// <summary>Everything has arrived.</summary>
    Received = 4,

    /// <summary>Abandoned before anything shipped.</summary>
    Cancelled = 5,
}

/// <summary>
/// A movement of stock from one location to another, usually between branches.
/// </summary>
/// <remarks>
/// <para>
/// A transfer happens in two steps with a gap between them, and that gap is the whole reason the
/// document exists. Goods leave Riyadh on Monday and reach Jeddah on Wednesday; for those two days
/// they belong to the company, sit on the balance sheet, and are at neither branch. Treating a
/// transfer as a single instantaneous movement makes them vanish from the valuation for the length
/// of the journey, and the inventory account disagrees with the stock report until they land.
/// </para>
/// <para>
/// So shipping moves stock from the source to an in-transit location, and receiving moves it from
/// there to the destination. What is in transit is always visible, always valued, and always
/// attributable to a document.
/// </para>
/// </remarks>
public sealed class TransferOrder : CompanyEntity
{
    /// <summary>The transfer number, for example <c>TR-2026-00042</c>.</summary>
    public required string No { get; set; }

    /// <summary>Where the goods are leaving.</summary>
    public Guid FromLocationId { get; set; }

    /// <summary>Source location code, copied so the document reads without a join.</summary>
    public required string FromLocationCode { get; set; }

    /// <summary>Where the goods are going.</summary>
    public Guid ToLocationId { get; set; }

    /// <summary>Destination location code.</summary>
    public required string ToLocationCode { get; set; }

    /// <summary>
    /// Where the goods sit while they travel. Falls back to the company's in-transit location.
    /// </summary>
    public Guid? InTransitLocationId { get; set; }

    /// <summary>How far along the transfer is.</summary>
    public TransferStatus Status { get; set; } = TransferStatus.Open;

    /// <summary>When the goods are expected to leave.</summary>
    public DateOnly ShipmentDate { get; set; }

    /// <summary>When they are expected to arrive.</summary>
    public DateOnly? ExpectedReceiptDate { get; set; }

    /// <summary>When they actually left.</summary>
    public DateOnly? ShippedOn { get; set; }

    /// <summary>When they actually arrived.</summary>
    public DateOnly? ReceivedOn { get; set; }

    /// <summary>Why the transfer was raised, or anything the receiving branch should know.</summary>
    public string? Description { get; set; }

    /// <summary>The transaction the shipment was posted under.</summary>
    public long? ShipmentTransactionNo { get; set; }

    /// <summary>The transaction the receipt was posted under.</summary>
    public long? ReceiptTransactionNo { get; set; }

    /// <summary>What is being moved.</summary>
    public ICollection<TransferOrderLine> Lines { get; set; } = [];

    /// <summary>Whether the transfer can still be edited.</summary>
    public bool IsEditable => Status is TransferStatus.Open or TransferStatus.Released;

    /// <summary>Whether anything has left the source yet.</summary>
    public bool HasShipped => Status is TransferStatus.Shipped
        or TransferStatus.PartiallyReceived
        or TransferStatus.Received;
}

/// <summary>One item on a transfer.</summary>
public sealed class TransferOrderLine : CompanyEntity
{
    /// <summary>The transfer this line belongs to.</summary>
    public Guid TransferOrderId { get; set; }

    /// <summary>Navigation to the transfer.</summary>
    public TransferOrder? TransferOrder { get; set; }

    /// <summary>Position on the document.</summary>
    public int LineNo { get; set; }

    /// <summary>The item being moved.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Item number, copied so the line reads without a join.</summary>
    public required string ItemNo { get; set; }

    /// <summary>What it is, as it was described when the transfer was raised.</summary>
    public required string Description { get; set; }

    /// <summary>How much is being moved.</summary>
    public decimal Quantity { get; set; }

    /// <summary>How much has actually left the source.</summary>
    public decimal QuantityShipped { get; set; }

    /// <summary>
    /// How much has arrived at the destination.
    /// </summary>
    /// <remarks>
    /// Tracked separately from what shipped, because the two genuinely differ. A pallet arrives
    /// short, a box is damaged in transit, and the difference is a real loss that has to be
    /// visible rather than quietly reconciled away.
    /// </remarks>
    public decimal QuantityReceived { get; set; }

    /// <summary>What is still at the source waiting to go.</summary>
    public decimal OutstandingToShip => Quantity - QuantityShipped;

    /// <summary>What has left but not arrived.</summary>
    public decimal InTransit => QuantityShipped - QuantityReceived;
}
