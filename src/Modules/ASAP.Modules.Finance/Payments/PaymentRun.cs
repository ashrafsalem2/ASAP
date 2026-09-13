using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Payments;

/// <summary>Where a payment run stands.</summary>
public enum PaymentRunStatus
{
    /// <summary>Proposed. Lines may still be taken off.</summary>
    Draft = 0,

    /// <summary>The file has been made and may be with the bank. Nothing can be changed.</summary>
    Exported = 1,

    /// <summary>The payments are in the ledger and the invoices are settled.</summary>
    Posted = 2,

    /// <summary>Abandoned before anything posted.</summary>
    Cancelled = 3,
}

/// <summary>
/// A batch of vendor payments from one bank account on one day.
/// </summary>
/// <remarks>
/// <para>
/// Three steps with a hard line between each. A draft is a proposal anybody can argue with. An
/// exported run is a file that may already be at the bank, so nothing on it can change — a line
/// taken off after export is a payment the ledger forgets and the bank still makes. A posted run
/// is in the ledger.
/// </para>
/// <para>
/// Export and posting are separate because they happen on different days to different people.
/// The file goes to the bank; the bank may reject a line; somebody posts what the bank actually
/// paid. A system that posted on export would be recording payments on the strength of a file.
/// </para>
/// </remarks>
public sealed class PaymentRun : CompanyEntity
{
    /// <summary>The run number, which is also the file's message id.</summary>
    public required string No { get; set; }

    /// <summary>The bank account the money leaves.</summary>
    public required string BankAccountCode { get; set; }

    /// <summary>The day the bank should pay.</summary>
    public DateOnly PaymentDate { get; set; }

    /// <summary>Invoices due on or before this were proposed.</summary>
    public DateOnly DueByDate { get; set; }

    /// <summary>What every payment is in.</summary>
    public required string CurrencyCode { get; set; }

    /// <summary>Where it stands.</summary>
    public PaymentRunStatus Status { get; set; } = PaymentRunStatus.Draft;

    /// <summary>Who proposed it.</summary>
    public string? CreatedByUserName { get; set; }

    /// <summary>When the file was made.</summary>
    public DateTime? ExportedAtUtc { get; set; }

    /// <summary>Who made the file.</summary>
    public string? ExportedByUserName { get; set; }

    /// <summary>The file, kept exactly as it was handed over.</summary>
    public string? FileContent { get; set; }

    /// <summary>
    /// The file's SHA-256.
    /// </summary>
    /// <remarks>
    /// Recorded because the file sits on somebody's desktop between export and upload, and a file
    /// that was opened and "tidied" on the way is a file paying people the system did not pay.
    /// The bank's upload screen usually shows a hash; this is what to compare it with.
    /// </remarks>
    public string? FileSha256 { get; set; }

    /// <summary>The transaction the payments posted under.</summary>
    public long? TransactionNo { get; set; }

    /// <summary>Who paid each vendor, and what for.</summary>
    public ICollection<PaymentRunLine> Lines { get; set; } = [];

    /// <summary>What the run pays in total.</summary>
    public decimal TotalAmount => Lines.Sum(static l => l.Amount);
}

/// <summary>One transfer: everything one vendor is being paid in the run.</summary>
public sealed class PaymentRunLine : CompanyEntity
{
    /// <summary>The run this belongs to.</summary>
    public Guid PaymentRunId { get; set; }

    /// <summary>The run, for loading.</summary>
    public PaymentRun? PaymentRun { get; set; }

    /// <summary>Position in the run.</summary>
    public int LineNo { get; set; }

    /// <summary>Who is being paid.</summary>
    public required string VendorNo { get; set; }

    /// <summary>Their name as it goes on the file.</summary>
    public required string VendorName { get; set; }

    /// <summary>
    /// Where the money goes, copied at the moment the run was proposed.
    /// </summary>
    /// <remarks>
    /// Copied rather than read at export, so that somebody changing a vendor's bank details
    /// between proposal and export changes nothing already reviewed. A changed IBAN on a vendor
    /// card the day before a payment run is the classic shape of payment fraud, and the run
    /// should not quietly follow it.
    /// </remarks>
    public string? Iban { get; set; }

    /// <summary>The vendor's bank's BIC, copied at proposal.</summary>
    public string? Bic { get; set; }

    /// <summary>What the transfer is for, as it reaches the vendor's statement.</summary>
    public required string Reference { get; set; }

    /// <summary>What this transfer pays in total.</summary>
    public decimal Amount { get; set; }

    /// <summary>The invoices this transfer settles.</summary>
    public ICollection<PaymentRunInvoice> Invoices { get; set; } = [];
}

/// <summary>One invoice a transfer settles, and how much of it.</summary>
public sealed class PaymentRunInvoice : CompanyEntity
{
    /// <summary>The transfer this belongs to.</summary>
    public Guid PaymentRunLineId { get; set; }

    /// <summary>The transfer, for loading.</summary>
    public PaymentRunLine? PaymentRunLine { get; set; }

    /// <summary>The vendor ledger entry being paid.</summary>
    public Guid VendorLedgerEntryId { get; set; }

    /// <summary>Its document number, for the remittance and the screen.</summary>
    public string? DocumentNo { get; set; }

    /// <summary>When it fell due.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>How much of it is being paid, as a positive amount.</summary>
    public decimal Amount { get; set; }
}
