using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Pos.Sessions;

/// <summary>Where a till session stands.</summary>
public enum PosSessionStatus
{
    /// <summary>Trading. Receipts may be taken against it.</summary>
    Open = 0,

    /// <summary>
    /// Counted and finished. The drawer has been declared and any difference posted.
    /// </summary>
    Closed = 1,
}

/// <summary>
/// One cashier's turn at one till, from opening float to final count.
/// </summary>
/// <remarks>
/// <para>
/// The session is the unit everything about cash hangs off. A drawer is counted at the end of a
/// turn and the count either agrees with what was taken or it does not, and the difference has to
/// belong to somebody. Tie it to the day and two cashiers share the blame; tie it to the till and
/// the person who was standing there is anonymous. It is tied to the turn.
/// </para>
/// <para>
/// Everything here is money that passed through the drawer, which is not the same as what was
/// sold. A card payment is a sale and never touches the drawer; change given is not a sale and
/// leaves it. Keeping the two apart is what lets the variance mean something.
/// </para>
/// </remarks>
public sealed class PosSession : CompanyEntity
{
    /// <summary>The session number, issued from a number series.</summary>
    public required string No { get; set; }

    /// <summary>The till this turn was worked at.</summary>
    public required string StationCode { get; set; }

    /// <summary>Who worked it.</summary>
    public Guid? CashierId { get; set; }

    /// <summary>Their name at the time, kept so the record still reads after they leave.</summary>
    public string? CashierName { get; set; }

    /// <summary>When the drawer was opened.</summary>
    public DateTime OpenedAtUtc { get; set; }

    /// <summary>The business day it trades under.</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>What was in the drawer at the start.</summary>
    public decimal OpeningFloat { get; set; }

    /// <summary>Cash taken in, before any change was given out.</summary>
    public decimal CashTendered { get; set; }

    /// <summary>Change handed back. Money that entered the drawer and left it again.</summary>
    public decimal ChangeGiven { get; set; }

    /// <summary>Cash paid out on returns.</summary>
    public decimal CashRefunded { get; set; }

    /// <summary>Everything taken by card, which never reaches the drawer.</summary>
    public decimal CardTaken { get; set; }

    /// <summary>Everything charged to a customer account rather than paid for.</summary>
    public decimal OnAccountTaken { get; set; }

    /// <summary>What the receipts came to, net of tax.</summary>
    public decimal NetSales { get; set; }

    /// <summary>Tax charged across the session.</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>How many receipts were posted.</summary>
    public int ReceiptCount { get; set; }

    /// <summary>When it was counted and closed.</summary>
    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>Who closed it, which need not be the cashier.</summary>
    public Guid? ClosedBy { get; set; }

    /// <summary>What the cashier counted, entered at close.</summary>
    public decimal? DeclaredCash { get; set; }

    /// <summary>How many times an X reading was taken. Recorded because a till read repeatedly
    /// before a short count is worth somebody noticing.</summary>
    public int ReadingCount { get; set; }

    /// <summary>Where it stands.</summary>
    public PosSessionStatus Status { get; set; } = PosSessionStatus.Open;

    /// <summary>The transaction the closing entries posted under.</summary>
    public long? ClosingTransactionNo { get; set; }


    /// <summary>
    /// What should be in the drawer.
    /// </summary>
    /// <remarks>
    /// Only cash, and only cash that is still there: the float it started with, plus what was
    /// taken in cash, less the change handed back and anything refunded. Card takings are not in
    /// the drawer and must not be counted as though they were, which is the mistake that makes
    /// every till look short by the day's card sales.
    /// </remarks>
    public decimal ExpectedCash
        => OpeningFloat + CashTendered - ChangeGiven - CashRefunded;

    /// <summary>
    /// What the count came to less what it should have been, or null while the drawer is open.
    /// </summary>
    /// <remarks>
    /// Negative is short and positive is over. Both matter: a till that is repeatedly over is
    /// not an honest till that got lucky, it is one where somebody is mis-keying.
    /// </remarks>
    public decimal? Variance
        => DeclaredCash is { } declared ? declared - ExpectedCash : null;

    /// <summary>Everything sold through this session, tax included.</summary>
    public decimal GrossSales => NetSales + TaxAmount;

    /// <summary>Whether receipts may still be taken against it.</summary>
    public bool IsOpen => Status is PosSessionStatus.Open;
}
