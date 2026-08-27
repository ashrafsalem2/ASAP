using ASAP.Modules.Finance.Ledger;
using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Tax;

/// <summary>
/// One posted tax figure, recorded so a return can be built from the ledger rather than
/// reconstructed from it.
/// </summary>
/// <remarks>
/// <para>
/// A tax return is not a query over the general ledger, however much it looks like one. The
/// balance on the VAT account is a net number that tells you nothing about which supplies were
/// standard-rated, which were zero-rated, which were exempt, and what the taxable base was in
/// each case -- and every one of those is a separate box on the form. Zero-rated sales are the
/// clearest case: they move the tax account by nothing at all, and still have to be declared.
/// </para>
/// <para>
/// So each taxed line writes its own row here, carrying the base, the tax, the rate as it stood
/// that day, and who the other party was. That last field is what makes a return auditable: the
/// authority asks which customers made up a figure, and the answer is a filter rather than an
/// afternoon.
/// </para>
/// <para>
/// Immutable like every other ledger entity. A correction is another entry.
/// </para>
/// </remarks>
public sealed class TaxEntry : LedgerEntity
{
    /// <summary>The date the figure belongs to, which decides the return period.</summary>
    public DateOnly PostingDate { get; set; }

    /// <summary>Groups this with the general ledger entries posted alongside it.</summary>
    public long TransactionNo { get; set; }

    /// <summary>Whether this is tax charged out or tax paid in.</summary>
    public TaxDirection Direction { get; set; }

    /// <summary>The tax code used.</summary>
    public Guid TaxCodeId { get; set; }

    /// <summary>The code, copied at posting so a return needs no join.</summary>
    public required string TaxCodeNo { get; set; }

    /// <summary>How the code behaved, copied because a code can be reclassified later.</summary>
    public TaxKind Kind { get; set; }

    /// <summary>
    /// The percentage applied, copied at posting. A rate change must never restate a figure that
    /// has already been declared.
    /// </summary>
    public decimal Percentage { get; set; }

    /// <summary>The taxable amount, before tax. The base of most boxes on a return.</summary>
    public decimal BaseAmount { get; set; }

    /// <summary>The tax itself.</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>What kind of document produced it.</summary>
    public GlDocumentType DocumentType { get; set; }

    /// <summary>The document number.</summary>
    public string? DocumentNo { get; set; }

    /// <summary>The other side's reference, such as the number on a vendor's invoice.</summary>
    public string? ExternalDocumentNo { get; set; }

    /// <summary>The customer or vendor number, when there was one.</summary>
    public string? PartyNo { get; set; }

    /// <summary>Their name at the time, so a return can be read without joining.</summary>
    public string? PartyName { get; set; }

    /// <summary>Their tax registration number, which an audited return has to show.</summary>
    public string? PartyTaxRegistrationNo { get; set; }

    /// <summary>The account the tax landed on.</summary>
    public string? TaxAccountNo { get; set; }

    /// <summary>Where the entry came from, for example <c>SALES</c> or <c>GENJNL</c>.</summary>
    public required string SourceCode { get; set; }

    /// <summary>Branch the entry originated at, or null at head office.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// True once this figure has been included in a filed return.
    /// </summary>
    /// <remarks>
    /// Set when a return is marked as filed, and what stops a late entry from quietly changing a
    /// period that has already been declared to the authority. The entry is still posted -- it
    /// simply belongs to the next return as an adjustment rather than to the one already gone.
    /// </remarks>
    public bool IsClosed { get; set; }

    /// <summary>Which filed return this figure went into.</summary>
    public Guid? TaxReturnId { get; set; }
}
