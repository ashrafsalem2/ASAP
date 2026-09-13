using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Transfers;

/// <summary>Where a transfer request stands.</summary>
public enum TransferRequestStatus
{
    /// <summary>Being written. Nobody has been asked yet.</summary>
    Draft = 0,

    /// <summary>Asked, and waiting on whoever holds the stock.</summary>
    Submitted = 1,

    /// <summary>Agreed, in whole or in part, and ready to be sent.</summary>
    Approved = 2,

    /// <summary>Turned down.</summary>
    Rejected = 3,

    /// <summary>Every approved line has become a transfer.</summary>
    Fulfilled = 4,

    /// <summary>Withdrawn before it produced anything.</summary>
    Cancelled = 5,
}

/// <summary>
/// A branch asking for stock from somewhere that has it.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as a purchase requisition, and for the same reason. A branch cannot help itself
/// to a warehouse's stock any more than a department can help itself to the company's money: the
/// place holding the goods knows what else is promised out of them and what is arriving, and the
/// branch asking does not.
/// </para>
/// <para>
/// Nothing is reserved when a request is approved. The goods can still be sold from under it, and
/// that is deliberate: an approved request somebody forgot about would otherwise hold stock off
/// the shelf indefinitely. The transfer order is what commits, which is why approving produces
/// one rather than being one.
/// </para>
/// </remarks>
public sealed class TransferRequest : CompanyEntity
{
    /// <summary>The request number.</summary>
    public required string No { get; set; }

    /// <summary>Where the goods are being asked from.</summary>
    public required string FromLocationCode { get; set; }

    /// <summary>Where they are wanted.</summary>
    public required string ToLocationCode { get; set; }

    /// <summary>When it was raised.</summary>
    public DateOnly RequestDate { get; set; }

    /// <summary>When they are wanted by.</summary>
    public DateOnly? NeededByDate { get; set; }

    /// <summary>Where it stands.</summary>
    public TransferRequestStatus Status { get; set; } = TransferRequestStatus.Draft;

    /// <summary>Why the branch wants them, which is what whoever holds the stock reads.</summary>
    public string? Reason { get; set; }

    /// <summary>Who asked.</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>Their name, so a list reads without a join.</summary>
    public string? RequestedByUserName { get; set; }

    /// <summary>Who agreed to it.</summary>
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>Their name.</summary>
    public string? ApprovedByUserName { get; set; }

    /// <summary>Why it was turned down, where it was.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>What is being asked for.</summary>
    public ICollection<TransferRequestLine> Lines { get; set; } = [];

    /// <summary>Whether it may still be changed.</summary>
    public bool IsEditable => Status is TransferRequestStatus.Draft;

    /// <summary>Whether anything on it is still to be sent.</summary>
    public bool HasOutstanding => Status is TransferRequestStatus.Approved
        && Lines.Any(static l => l.OutstandingToTransfer > 0m);
}

/// <summary>One thing being asked for.</summary>
public sealed class TransferRequestLine : CompanyEntity
{
    /// <summary>The request this belongs to.</summary>
    public Guid TransferRequestId { get; set; }

    /// <summary>The request, for loading.</summary>
    public TransferRequest? TransferRequest { get; set; }

    /// <summary>Position on the request.</summary>
    public int LineNo { get; set; }

    /// <summary>What is wanted.</summary>
    public required string ItemNo { get; set; }

    /// <summary>Which variant, on an item that has them.</summary>
    public string? VariantCode { get; set; }

    /// <summary>How much the branch asked for.</summary>
    public decimal QuantityRequested { get; set; }

    /// <summary>
    /// How much whoever holds the stock agreed to.
    /// </summary>
    /// <remarks>
    /// Never more than was asked for. Somebody who asked for ten and was sent twenty did not ask
    /// for twenty, and a warehouse clearing its shelves into a branch that cannot sell them has
    /// moved a problem rather than solved one.
    /// </remarks>
    public decimal QuantityApproved { get; set; }

    /// <summary>How much has actually gone onto a transfer.</summary>
    public decimal QuantityTransferred { get; set; }

    /// <summary>What is agreed and not yet sent.</summary>
    public decimal OutstandingToTransfer => QuantityApproved - QuantityTransferred;

    /// <summary>A note from either side.</summary>
    public string? Note { get; set; }
}
