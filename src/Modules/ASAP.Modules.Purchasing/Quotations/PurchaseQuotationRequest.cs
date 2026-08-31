using ASAP.Modules.Purchasing.Orders;
using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Purchasing.Quotations;

/// <summary>Where a request for quotation stands.</summary>
public enum QuotationRequestStatus
{
    /// <summary>Being written. Nobody has been asked anything.</summary>
    Draft = 0,

    /// <summary>Out with the vendors, waiting on answers.</summary>
    Sent = 1,

    /// <summary>Answers are in and somebody is deciding.</summary>
    Closed = 2,

    /// <summary>Lines have been awarded and orders may be raised.</summary>
    Awarded = 3,

    /// <summary>Abandoned.</summary>
    Cancelled = 4,
}

/// <summary>
/// The same question, asked of several vendors at once.
/// </summary>
/// <remarks>
/// <para>
/// A requisition says what is needed. A request for quotation asks what it would cost, of more
/// than one supplier, so that the answers can be put side by side. Nothing here commits anybody:
/// the vendors are quoting and the company is comparing.
/// </para>
/// <para>
/// Awarding is per line, because real buying is. The bolts go to one supplier and the nuts to
/// another, and forcing a whole request onto one vendor would either lose the better price or
/// split the request into several that nobody can compare.
/// </para>
/// <para>
/// The rule worth knowing is what happens when the cheapest quote is not the one chosen. That is
/// a legitimate decision -- a fortnight's lead time is worth paying for when the shelf is empty --
/// but it is also the decision somebody asks about a year later, so it cannot be made silently.
/// Awarding to anything other than the lowest price is refused unless a reason is given.
/// </para>
/// </remarks>
public sealed class PurchaseQuotationRequest : CompanyEntity
{
    /// <summary>The request number, issued from a number series.</summary>
    public required string No { get; set; }

    /// <summary>Where it stands.</summary>
    public QuotationRequestStatus Status { get; set; } = QuotationRequestStatus.Draft;

    /// <summary>The day it was raised.</summary>
    public DateOnly RequestDate { get; set; }

    /// <summary>The day answers are wanted by.</summary>
    public DateOnly? RespondByDate { get; set; }

    /// <summary>When the goods themselves are wanted.</summary>
    public DateOnly? NeededByDate { get; set; }

    /// <summary>Where the goods are wanted.</summary>
    public string? LocationCode { get; set; }

    /// <summary>The requisition this arose from, where it arose from one.</summary>
    public string? RequisitionNo { get; set; }

    /// <summary>What it is for.</summary>
    public string? Description { get; set; }

    /// <summary>Why it was abandoned, where it was.</summary>
    public string? CancellationReason { get; set; }

    /// <summary>What is being asked about.</summary>
    public ICollection<PurchaseQuotationRequestLine> Lines { get; set; } = [];

    /// <summary>Who was asked.</summary>
    public ICollection<PurchaseQuotationInvitation> Invitations { get; set; } = [];

    /// <summary>What the vendors said.</summary>
    public ICollection<PurchaseQuotationResponse> Responses { get; set; } = [];

    /// <summary>Whether its lines may still be changed.</summary>
    public bool IsEditable => Status is QuotationRequestStatus.Draft;

    /// <summary>Whether anything on it is still waiting to be awarded.</summary>
    public bool HasUnawardedLines => Lines.Any(static l => l.AwardedVendorNo is null);

    /// <summary>Whether any award is still waiting to become an order.</summary>
    public bool HasUnorderedAwards
        => Lines.Any(static l => l.AwardedVendorNo is not null && l.AwardedOrderNo is null);
}

/// <summary>One thing being asked about.</summary>
public sealed class PurchaseQuotationRequestLine : CompanyEntity
{
    /// <summary>The request this line belongs to.</summary>
    public Guid PurchaseQuotationRequestId { get; set; }

    /// <summary>Navigation to the request.</summary>
    public PurchaseQuotationRequest? PurchaseQuotationRequest { get; set; }

    /// <summary>Position on it, in tens.</summary>
    public int LineNo { get; set; }

    /// <summary>Whether it asks about stock or a cost.</summary>
    public PurchaseLineType Type { get; set; }

    /// <summary>The item number, on an item line.</summary>
    public string? ItemNo { get; set; }

    /// <summary>Which variant, on an item that has them.</summary>
    public string? VariantCode { get; set; }

    /// <summary>The account number, on a cost line.</summary>
    public string? AccountNo { get; set; }

    /// <summary>What is being asked about, in words.</summary>
    public required string Description { get; set; }

    /// <summary>Where it is wanted, when it differs from the request.</summary>
    public string? LocationCode { get; set; }

    /// <summary>How much is wanted.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The vendor this line was awarded to, once somebody decided.</summary>
    public string? AwardedVendorNo { get; set; }

    /// <summary>What they quoted, copied so the award reads without a join.</summary>
    public decimal? AwardedUnitCost { get; set; }

    /// <summary>
    /// Why this vendor rather than the cheapest one.
    /// </summary>
    /// <remarks>
    /// Required whenever the award is not the lowest price. The decision is legitimate and the
    /// silence is not: a year later somebody asks why the dearer quote won, and a blank field is
    /// the difference between an answer and an investigation.
    /// </remarks>
    public string? AwardReason { get; set; }

    /// <summary>The order the award became.</summary>
    public string? AwardedOrderNo { get; set; }
}

/// <summary>A vendor who was asked.</summary>
/// <remarks>
/// Kept apart from the answers, because being asked and staying silent is information. A vendor
/// with no responses and no decline is one who did not reply, and that is worth knowing before
/// asking them again.
/// </remarks>
public sealed class PurchaseQuotationInvitation : CompanyEntity
{
    /// <summary>The request they were asked about.</summary>
    public Guid PurchaseQuotationRequestId { get; set; }

    /// <summary>Navigation to the request.</summary>
    public PurchaseQuotationRequest? PurchaseQuotationRequest { get; set; }

    /// <summary>Who was asked.</summary>
    public required string VendorNo { get; set; }

    /// <summary>Their name at the time.</summary>
    public required string VendorName { get; set; }

    /// <summary>When they answered, where they did.</summary>
    public DateTime? RespondedAtUtc { get; set; }

    /// <summary>Why they said no, where they said so.</summary>
    public string? DeclinedReason { get; set; }

    /// <summary>Whether they answered at all.</summary>
    public bool HasAnswered => RespondedAtUtc is not null || DeclinedReason is not null;
}

/// <summary>What one vendor said about one line.</summary>
public sealed class PurchaseQuotationResponse : CompanyEntity
{
    /// <summary>The request.</summary>
    public Guid PurchaseQuotationRequestId { get; set; }

    /// <summary>Navigation to the request.</summary>
    public PurchaseQuotationRequest? PurchaseQuotationRequest { get; set; }

    /// <summary>Which line they were answering.</summary>
    public int LineNo { get; set; }

    /// <summary>Who answered.</summary>
    public required string VendorNo { get; set; }

    /// <summary>What they would charge per unit.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// How many days they say it takes.
    /// </summary>
    /// <remarks>
    /// Held beside the price because the two together are the quote. The cheapest supplier who
    /// takes six weeks is not the best answer for a shelf that is empty now, and a comparison
    /// showing only money would make that invisible.
    /// </remarks>
    public int? LeadTimeDays { get; set; }

    /// <summary>Anything the vendor said about it.</summary>
    public string? Note { get; set; }

    /// <summary>What the line comes to at their price.</summary>
    public decimal LineAmount { get; set; }
}
