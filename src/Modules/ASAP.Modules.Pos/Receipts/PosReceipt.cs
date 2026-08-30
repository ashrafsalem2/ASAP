using ASAP.Modules.Pos.Sessions;
using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Pos.Receipts;

/// <summary>Where a receipt stands.</summary>
public enum PosReceiptStatus
{
    /// <summary>
    /// Set aside so the till can serve somebody else. Nothing has posted and nothing is reserved.
    /// </summary>
    Parked = 0,

    /// <summary>Paid for and posted. Stock has gone and the money is accounted for.</summary>
    Posted = 1,

    /// <summary>
    /// Cancelled before it posted. Kept rather than deleted, because a till that can make
    /// transactions disappear is a till nobody can audit.
    /// </summary>
    Voided = 2,
}

/// <summary>How a receipt was paid for.</summary>
public enum TenderKind
{
    /// <summary>Notes and coins. The only kind that reaches the drawer.</summary>
    Cash = 0,

    /// <summary>A card, settled later by the acquirer. Held in a clearing account until it lands.</summary>
    Card = 1,

    /// <summary>A gift card or credit note being redeemed.</summary>
    Voucher = 2,

    /// <summary>Charged to the customer's account. A sale on credit, made at a till.</summary>
    OnAccount = 3,
}

/// <summary>What a receipt line is selling.</summary>
public enum PosLineType
{
    /// <summary>Stock. Selling it moves inventory and charges its cost to cost of sales.</summary>
    Item = 0,

    /// <summary>A charge with no stock behind it: a delivery fee, a service.</summary>
    GlAccount = 1,
}

/// <summary>
/// A sale made at a till.
/// </summary>
/// <remarks>
/// <para>
/// A receipt is a sales invoice with a cash drawer attached. It posts the same way one does —
/// revenue at list with the discount as a contra, tax on both so it lands on what the customer
/// actually pays, stock out at what the goods cost rather than what they sold for. The P&amp;L
/// must not be able to tell which door a sale came through, or the margin report becomes an
/// argument about channels instead of a number.
/// </para>
/// <para>
/// What it adds is the money. An order is a promise and an invoice is a debt; a receipt is
/// settled where it stands, in one or several tenders, possibly with change handed back. Those
/// are the only parts of this that a sales invoice has no equivalent for.
/// </para>
/// <para>
/// A return is the same document with negative quantities. Modelling it as its own kind would
/// mean writing the whole thing twice and then discovering that an exchange — two shirts back,
/// one jacket out, the difference in cash — is neither.
/// </para>
/// </remarks>
public sealed class PosReceipt : CompanyEntity
{
    /// <summary>The receipt number, issued from a number series.</summary>
    public required string No { get; set; }

    /// <summary>The session it was taken during.</summary>
    public Guid SessionId { get; set; }

    /// <summary>The session, when loaded.</summary>
    public PosSession? Session { get; set; }

    /// <summary>The till it was taken at.</summary>
    public required string StationCode { get; set; }

    /// <summary>Who the sale is recorded against, which for a walk-in is the station's default.</summary>
    public required string CustomerNo { get; set; }

    /// <summary>Their name at the time.</summary>
    public required string CustomerName { get; set; }

    /// <summary>Where the stock came off.</summary>
    public required string LocationCode { get; set; }

    /// <summary>When it was rung up.</summary>
    public DateTime TakenAtUtc { get; set; }

    /// <summary>The business day it trades under.</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>Where it stands.</summary>
    public PosReceiptStatus Status { get; set; } = PosReceiptStatus.Parked;

    /// <summary>What was keyed to identify a parked sale when it is recalled.</summary>
    public string? ParkedAs { get; set; }

    /// <summary>The receipt this one reverses, when it is a return against a known sale.</summary>
    public string? ReturnsReceiptNo { get; set; }

    /// <summary>What the goods came to, after discount and before tax.</summary>
    public decimal NetAmount { get; set; }

    /// <summary>What was given away by whoever was at the till.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// What was given away by a promotion.
    /// </summary>
    /// <remarks>
    /// Held apart from the discount above and posted to a different account. Both are money the
    /// company chose not to take; only one of them is a campaign somebody planned and should be
    /// able to total the cost of.
    /// </remarks>
    public decimal PromotionAmount { get; set; }

    /// <summary>Tax charged.</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>What was rounded off the total to make it payable in the coins that exist.</summary>
    public decimal RoundingAmount { get; set; }

    /// <summary>What the goods cost, charged to cost of sales.</summary>
    public decimal CostAmount { get; set; }

    /// <summary>Change handed back, when cash tendered exceeded the total.</summary>
    public decimal ChangeGiven { get; set; }

    /// <summary>The transaction the entries posted under.</summary>
    public long? TransactionNo { get; set; }

    /// <summary>Who rang it up.</summary>
    public Guid? CashierId { get; set; }

    /// <summary>The lines on it.</summary>
    public ICollection<PosReceiptLine> Lines { get; set; } = [];

    /// <summary>How it was paid for.</summary>
    public ICollection<PosTender> Tenders { get; set; } = [];


    /// <summary>What the customer owes, tax and rounding included.</summary>
    public decimal TotalAmount => NetAmount + TaxAmount + RoundingAmount;

    /// <summary>What has been put towards it.</summary>
    public decimal TenderedAmount => Tenders.Sum(static t => t.Amount);

    /// <summary>
    /// What is still to be paid.
    /// </summary>
    /// <remarks>
    /// Negative when more cash was handed over than the total, which is not a shortfall but the
    /// change owed back. The two are the same arithmetic seen from opposite sides, and a till
    /// that clamped this at zero could not work out what to give back.
    /// </remarks>
    public decimal OutstandingAmount => TotalAmount - TenderedAmount;

    /// <summary>Whether it may still be changed.</summary>
    public bool IsEditable => Status is PosReceiptStatus.Parked;

    /// <summary>Whether it takes goods back rather than selling them.</summary>
    public bool IsReturn => Lines.Sum(static l => l.Quantity) < 0m;
}

/// <summary>One thing on a receipt.</summary>
public sealed class PosReceiptLine : CompanyEntity
{
    /// <summary>The receipt it belongs to.</summary>
    public Guid PosReceiptId { get; set; }

    /// <summary>The receipt, when loaded.</summary>
    public PosReceipt? PosReceipt { get; set; }

    /// <summary>Its position on the receipt.</summary>
    public int LineNo { get; set; }

    /// <summary>Whether it sells stock or a charge.</summary>
    public PosLineType Type { get; set; }

    /// <summary>The item, on an item line.</summary>
    public string? ItemNo { get; set; }

    /// <summary>The account, on a charge line.</summary>
    public string? AccountNo { get; set; }

    /// <summary>What it says on the receipt.</summary>
    public required string Description { get; set; }

    /// <summary>
    /// How much, in the item's base unit. Negative takes goods back.
    /// </summary>
    /// <remarks>
    /// Base units, always, whatever was rung. Stock leaves in base units and the price is per
    /// base unit, so a case of twelve is stored as twelve and charged as twelve. What the cashier
    /// actually scanned is on <see cref="UnitCode"/> beside it, for the receipt to print.
    /// </remarks>
    public decimal Quantity { get; set; }

    /// <summary>The unit it was rung in, when that was not the base unit.</summary>
    public string? UnitCode { get; set; }

    /// <summary>
    /// How many base units that unit held at the moment of sale.
    /// </summary>
    /// <remarks>
    /// Frozen on the line, like the cost. A company that redefines its case from twelve to six
    /// must not thereby restate what somebody bought last year.
    /// </remarks>
    public decimal QuantityPerUnit { get; set; } = 1m;

    /// <summary>The price on the shelf, before any discount.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>A discount off this line, held as a percentage so it stays reportable.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>The offer that applied to this line, when one did.</summary>
    public string? OfferCode { get; set; }

    /// <summary>
    /// What that offer took off the line, in money.
    /// </summary>
    /// <remarks>
    /// An amount rather than a percentage, because a buy-three-get-one-free is not a percentage
    /// of anything a customer would recognise and a receipt claiming it was would be putting a
    /// number on paper that no arithmetic produced.
    /// </remarks>
    public decimal OfferDiscountAmount { get; set; }

    /// <summary>The tax charged.</summary>
    public string? TaxCode { get; set; }

    /// <summary>
    /// What the goods cost when they went out, per unit.
    /// </summary>
    /// <remarks>
    /// Kept on the line because a margin is only answerable against the cost at the time. Costs
    /// move; reporting last quarter's promotion against today's cost would say the campaign lost
    /// money it never lost, or made money it never made.
    /// <para>
    /// This is what the costing engine said at the moment of sale, which for an item going
    /// negative is an estimate until a receipt settles it. That is the same figure the margin
    /// floor was checked against, so a report and a refusal can never disagree.
    /// </para>
    /// <para>
    /// Null where nobody recorded it — a receipt written before this column existed, or a charge
    /// line with no goods behind it. Deliberately not zero: a margin report that could not tell
    /// "cost nothing" from "cost unknown" reported a hundred per cent on every historic receipt,
    /// which is a confident answer produced entirely by missing data.
    /// </para>
    /// </remarks>
    public decimal? UnitCostAtSale { get; set; }


    /// <summary>What each unit actually goes for.</summary>
    public decimal NetUnitPrice => UnitPrice * (1m - (DiscountPercent / 100m));

    /// <summary>What the line comes to, after every discount and before tax.</summary>
    public decimal LineAmount => (Quantity * NetUnitPrice) - OfferDiscountAmount;

    /// <summary>What was given away on this line.</summary>
    public decimal DiscountAmount => Quantity * UnitPrice * (DiscountPercent / 100m);
}

/// <summary>Money put towards a receipt.</summary>
/// <remarks>
/// Several are allowed on one receipt, because people pay for a hundred riyals of shopping with
/// a sixty-riyal gift card and the rest in cash. A single-tender model turns that ordinary
/// transaction into two receipts and a story.
/// </remarks>
public sealed class PosTender : CompanyEntity
{
    /// <summary>The receipt it pays for.</summary>
    public Guid PosReceiptId { get; set; }

    /// <summary>The receipt, when loaded.</summary>
    public PosReceipt? PosReceipt { get; set; }

    /// <summary>Its position, so the order they were offered in survives.</summary>
    public int LineNo { get; set; }

    /// <summary>What kind of money it is.</summary>
    public TenderKind Kind { get; set; }

    /// <summary>
    /// How much was handed over.
    /// </summary>
    /// <remarks>
    /// The full amount, including any part that comes back as change. What the drawer keeps is
    /// worked out at the receipt, not here: a note is a note whatever it was for.
    /// </remarks>
    public decimal Amount { get; set; }

    /// <summary>The card's last four, the voucher number, whatever identifies it afterwards.</summary>
    public string? Reference { get; set; }

}
