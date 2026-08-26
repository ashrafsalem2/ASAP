using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Ledger;

/// <summary>
/// What kind of document produced an entry. Lets a report separate an invoice from a payment
/// without joining back to the module that raised it.
/// </summary>
public enum GlDocumentType
{
    /// <summary>A manual journal with no document behind it.</summary>
    None = 0,

    /// <summary>A sales or purchase invoice.</summary>
    Invoice = 1,

    /// <summary>A credit memo reversing part or all of an invoice.</summary>
    CreditMemo = 2,

    /// <summary>Money received or paid.</summary>
    Payment = 3,

    /// <summary>A refund of a payment.</summary>
    Refund = 4,

    /// <summary>An inventory movement, revaluation or adjustment.</summary>
    InventoryAdjustment = 5,

    /// <summary>A payroll posting.</summary>
    Payroll = 6,

    /// <summary>A point of sale receipt.</summary>
    PosReceipt = 7,

    /// <summary>The year-end transfer of the result to retained earnings.</summary>
    YearEndClose = 8,
}

/// <summary>
/// A posted general ledger entry.
/// </summary>
/// <remarks>
/// <para>
/// This is the permanent record. Nothing updates or deletes one: the type derives from
/// <see cref="LedgerEntity"/>, which carries no soft-delete fields, so there is no code path that
/// could. A mistake is corrected by posting a reversal, which leaves the error and the correction
/// both visible, because an audit trail that can be tidied up is not an audit trail.
/// </para>
/// <para>
/// Amount is signed and is the figure everything calculates from. Debit and credit are carried
/// alongside it, always positive and always with one of them zero, because that is how every
/// report, every trial balance and every accountant expects to read a ledger. Storing both is a
/// deliberate denormalisation that removes a CASE expression from every reporting query in the
/// system.
/// </para>
/// </remarks>
public sealed class GlEntry : LedgerEntity
{
    /// <summary>
    /// The date the entry belongs to for reporting. Not when it was keyed: an invoice entered in
    /// March can legitimately post to February, and it is this date that decides the period.
    /// </summary>
    public DateOnly PostingDate { get; set; }

    /// <summary>
    /// Groups every entry written by one posting run. All the entries of a single sales invoice
    /// share one, which is what makes "show me the whole transaction" a single indexed query.
    /// </summary>
    public long TransactionNo { get; set; }

    /// <summary>The account the entry lands on.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Account number, copied at posting so a report needs no join and history survives renumbering.</summary>
    public required string AccountNo { get; set; }

    /// <summary>What kind of document produced it.</summary>
    public GlDocumentType DocumentType { get; set; }

    /// <summary>The document number, for example <c>INV-2026-00042</c>.</summary>
    public string? DocumentNo { get; set; }

    /// <summary>The line description shown on the ledger.</summary>
    public required string Description { get; set; }

    /// <summary>
    /// The signed amount in company currency. Positive is a debit, negative a credit, and the
    /// entries of one transaction always sum to zero.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>The debit side, always positive or zero.</summary>
    public decimal DebitAmount { get; set; }

    /// <summary>The credit side, always positive or zero.</summary>
    public decimal CreditAmount { get; set; }

    /// <summary>The transaction currency, when it differs from the company base currency.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>The amount in the transaction currency, when that differs from the base.</summary>
    public decimal? AmountInCurrency { get; set; }

    /// <summary>The rate used to convert. Kept so a historical entry can be explained years later.</summary>
    public decimal? ExchangeRate { get; set; }

    /// <summary>The dimension combination this entry was posted with.</summary>
    public Guid? DimensionSetId { get; set; }

    /// <summary>
    /// The first shortcut dimension value, copied onto the entry.
    /// </summary>
    /// <remarks>
    /// Denormalised on purpose. Filtering a million entries by department should be an index seek
    /// on this table, not a join through the dimension set entries for every candidate row.
    /// </remarks>
    public Guid? ShortcutDimension1Id { get; set; }

    /// <summary>The second shortcut dimension value, copied for the same reason.</summary>
    public Guid? ShortcutDimension2Id { get; set; }

    /// <summary>
    /// Where the entry came from, for example <c>SALES</c>, <c>POS</c> or <c>GENJNL</c>. The
    /// first question asked of an unexpected entry is which part of the system wrote it.
    /// </summary>
    public required string SourceCode { get; set; }

    /// <summary>Branch the entry originated at, or null at head office.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// True once this entry has been reversed. The entry itself is untouched; this only records
    /// that a reversing entry exists.
    /// </summary>
    public bool IsReversed { get; set; }

    /// <summary>The entry that reversed this one.</summary>
    public Guid? ReversedByEntryId { get; set; }

    /// <summary>
    /// The entry this one reverses, when it is itself a reversal. Set on the correcting entry, so
    /// the pair can be read from either end.
    /// </summary>
    public Guid? ReversalOfEntryId { get; set; }

    /// <summary>
    /// Builds a balanced pair of debit and credit figures from a signed amount.
    /// </summary>
    /// <param name="amount">The signed amount. Positive debits, negative credits.</param>
    /// <remarks>
    /// The single place the sign convention is applied, so it cannot be written one way in the
    /// journal poster and another way in the inventory poster.
    /// </remarks>
    public static (decimal Debit, decimal Credit) Split(decimal amount)
        => amount >= 0 ? (amount, 0m) : (0m, -amount);
}
