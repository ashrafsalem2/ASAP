using ASAP.Modules.Finance.Ledger;
using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Parties;

/// <summary>
/// One posted entry on a customer or vendor account.
/// </summary>
/// <remarks>
/// <para>
/// The subsidiary ledger. Every entry here has a matching general ledger entry on the control
/// account, posted in the same transaction, so the two can never disagree -- which is why the
/// control accounts ship with direct posting switched off.
/// </para>
/// <para>
/// <see cref="RemainingAmount"/> is what makes this more than a copy of the general ledger. An
/// invoice starts with its whole amount outstanding and falls as payments are applied to it; a
/// payment starts with its whole amount unapplied and falls as it is spread across invoices. When
/// it reaches zero the entry closes. That single field is what the aged analysis, the statement
/// and the "what do they still owe" question all read, and it is why applying a payment is a real
/// operation rather than a note in a description field.
/// </para>
/// <para>
/// Like the general ledger, nothing here is updated except <see cref="RemainingAmount"/> and
/// <see cref="IsOpen"/>, and nothing is ever deleted.
/// </para>
/// </remarks>
public abstract class PartyLedgerEntry : LedgerEntity
{
    /// <summary>The party the entry belongs to.</summary>
    public Guid PartyId { get; set; }

    /// <summary>The party number, copied at posting so a report needs no join.</summary>
    public required string PartyNo { get; set; }

    /// <summary>The party name as it stood when the entry posted.</summary>
    public required string PartyName { get; set; }

    /// <summary>The date the entry belongs to for reporting.</summary>
    public DateOnly PostingDate { get; set; }

    /// <summary>
    /// When payment falls due, worked out from the posting date and the party's terms. The one
    /// field the whole aged analysis turns on.
    /// </summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Groups this entry with the general ledger entries posted alongside it.</summary>
    public long TransactionNo { get; set; }

    /// <summary>What kind of document produced it.</summary>
    public GlDocumentType DocumentType { get; set; }

    /// <summary>The document number.</summary>
    public string? DocumentNo { get; set; }

    /// <summary>The party's own reference, such as the number on their invoice to us.</summary>
    public string? ExternalDocumentNo { get; set; }

    /// <summary>What the entry says on a statement.</summary>
    public required string Description { get; set; }

    /// <summary>
    /// The signed amount in company currency, on the same convention as the general ledger:
    /// positive debits the party, negative credits them.
    /// </summary>
    /// <remarks>
    /// A sales invoice debits the customer, a receipt credits them. Read on a customer, a positive
    /// balance is money owed to the company; on a vendor it is money the company has overpaid.
    /// </remarks>
    public decimal Amount { get; set; }

    /// <summary>
    /// How much of <see cref="Amount"/> is still unsettled, on the same sign as the amount.
    /// </summary>
    public decimal RemainingAmount { get; set; }

    /// <summary>
    /// Whether anything is still outstanding on this entry.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="RemainingAmount"/> but stored, because "show me the open entries"
    /// is the single most common query against this table and a filtered index on a bit column is
    /// far cheaper than one on a decimal comparison.
    /// </remarks>
    public bool IsOpen { get; set; } = true;

    /// <summary>The control account the matching general ledger entry landed on.</summary>
    public required string ControlAccountNo { get; set; }

    /// <summary>Where the entry came from, for example <c>SALES</c> or <c>GENJNL</c>.</summary>
    public required string SourceCode { get; set; }

    /// <summary>The transaction currency, when it differs from the company base currency.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>
    /// The amount in the transaction currency, when that differs from the base.
    /// </summary>
    /// <remarks>
    /// What the customer actually owes. The base figure beside it is what that was worth on the
    /// day the invoice was raised, and it stops being what they owe the moment the rate moves.
    /// A statement in the customer's own currency is read from this; the ledger is read from
    /// the other.
    /// </remarks>
    public decimal? AmountInCurrency { get; set; }

    /// <summary>
    /// How much of <see cref="AmountInCurrency"/> is still unsettled.
    /// </summary>
    /// <remarks>
    /// Settlement is decided here rather than on the base amount, for a foreign entry. An
    /// invoice of USD 1,000 is cleared by a payment of USD 1,000 whatever the two were worth in
    /// riyals on their two days; measured in riyals the payment would fall short or overshoot,
    /// leave the invoice fractionally open, and put a chaser in front of a customer who has paid
    /// in full. The riyal difference is real and is posted as an exchange difference, which is
    /// what it is -- not as an unpaid balance, which it is not.
    /// </remarks>
    public decimal? RemainingAmountInCurrency { get; set; }

    /// <summary>Branch the entry originated at, or null at head office.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>When the entry was fully settled.</summary>
    public DateOnly? ClosedOn { get; set; }

    /// <summary>Which ledger this entry belongs to.</summary>
    public abstract PartyKind Kind { get; }

    /// <summary>How much has been settled so far.</summary>
    public decimal AppliedAmount => Amount - RemainingAmount;

    /// <summary>
    /// How many days past due the entry is on a given date, or zero when it is not yet due.
    /// </summary>
    /// <param name="asAt">The date being aged against.</param>
    /// <returns>Days overdue, never negative.</returns>
    public int DaysOverdue(DateOnly asAt)
        => asAt <= DueDate ? 0 : asAt.DayNumber - DueDate.DayNumber;
}

/// <summary>One posted entry on a customer account.</summary>
public sealed class CustomerLedgerEntry : PartyLedgerEntry
{
    /// <inheritdoc />
    public override PartyKind Kind => PartyKind.Customer;
}

/// <summary>One posted entry on a vendor account.</summary>
public sealed class VendorLedgerEntry : PartyLedgerEntry
{
    /// <inheritdoc />
    public override PartyKind Kind => PartyKind.Vendor;
}
