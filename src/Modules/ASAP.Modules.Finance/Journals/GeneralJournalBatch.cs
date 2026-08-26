using ASAP.Modules.Finance.Ledger;
using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Journals;

/// <summary>What a journal line posts against.</summary>
public enum JournalAccountType
{
    /// <summary>A general ledger account.</summary>
    GlAccount = 0,

    /// <summary>A customer, which also writes a customer ledger entry.</summary>
    Customer = 1,

    /// <summary>A vendor, which also writes a vendor ledger entry.</summary>
    Vendor = 2,

    /// <summary>A bank account, which also writes a bank ledger entry.</summary>
    BankAccount = 3,

    /// <summary>A fixed asset.</summary>
    FixedAsset = 4,
}

/// <summary>
/// A named tray of journal lines waiting to be posted.
/// </summary>
/// <remarks>
/// <para>
/// A batch is a working area, not a record: lines are entered, checked, corrected and finally
/// posted, at which point they become ledger entries and leave the batch. Separate batches let
/// two people prepare journals at once without treading on each other, and let one person keep an
/// unfinished month-end accrual open while posting something urgent.
/// </para>
/// <para>
/// Nothing in a batch has touched the ledger. Deleting a batch loses work but corrupts nothing.
/// </para>
/// </remarks>
public sealed class GeneralJournalBatch : CompanyEntity
{
    /// <summary>Short name, for example <c>DEFAULT</c> or <c>MONTHEND</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What the batch is for.</summary>
    public required string Description { get; set; }

    /// <summary>Number series that names the documents posted from this batch.</summary>
    public string? NumberSeriesCode { get; set; }

    /// <summary>
    /// Source code stamped on every entry posted from this batch, for example <c>GENJNL</c>.
    /// The first question asked of an unexpected ledger entry is which part of the system wrote it.
    /// </summary>
    public string SourceCode { get; set; } = "GENJNL";

    /// <summary>
    /// The account every line balances against when it names no balancing account of its own.
    /// A payments batch sets this to the bank account, so a clerk keys only one side of each line.
    /// </summary>
    public Guid? DefaultBalancingAccountId { get; set; }

    /// <summary>
    /// The user this batch belongs to, or null for a shared one. A personal batch is what stops
    /// two clerks from posting each other half-finished lines.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Lines waiting to be posted.</summary>
    public ICollection<GeneralJournalLine> Lines { get; set; } = [];
}

/// <summary>
/// One line waiting to be posted.
/// </summary>
/// <remarks>
/// A line carries a single signed amount rather than separate debit and credit inputs. The screen
/// still offers two columns, because that is how the work is done, but only one number reaches
/// the domain -- which removes the state where a line carries both and nothing knows which is
/// meant.
/// </remarks>
public sealed class GeneralJournalLine : CompanyEntity
{
    /// <summary>The batch this line sits in.</summary>
    public Guid BatchId { get; set; }

    /// <summary>Navigation to the batch.</summary>
    public GeneralJournalBatch? Batch { get; set; }

    /// <summary>Position within the batch. Kept sparse so a line can be inserted between two others.</summary>
    public int LineNo { get; set; }

    /// <summary>The date the entry will be reported in.</summary>
    public DateOnly PostingDate { get; set; }

    /// <summary>What kind of document this represents.</summary>
    public GlDocumentType DocumentType { get; set; }

    /// <summary>
    /// The document number. Taken from the batch number series at posting when left empty.
    /// </summary>
    public string? DocumentNo { get; set; }

    /// <summary>What the line posts against.</summary>
    public JournalAccountType AccountType { get; set; } = JournalAccountType.GlAccount;

    /// <summary>The account, customer, vendor or bank account.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>What the entry will say on the ledger.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// The signed amount. Positive debits the account, negative credits it, and the balancing
    /// account takes the opposite.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Transaction currency, or null for the company base currency.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Rate to the base currency. Taken from the exchange rate table when left null.</summary>
    public decimal? ExchangeRate { get; set; }

    /// <summary>What the line balances against. Falls back to the batch default.</summary>
    public JournalAccountType? BalancingAccountType { get; set; }

    /// <summary>The balancing account.</summary>
    public Guid? BalancingAccountId { get; set; }

    /// <summary>The dimension combination the entry will carry.</summary>
    public Guid? DimensionSetId { get; set; }

    /// <summary>The customer or vendor entry this line settles, for a payment.</summary>
    public Guid? AppliesToEntryId { get; set; }

    /// <summary>Free reference: a cheque number, a transfer reference, an external document number.</summary>
    public string? ExternalDocumentNo { get; set; }

    /// <summary>
    /// True when the line balances against another account, so posting it writes two entries
    /// rather than one and the line stands alone.
    /// </summary>
    public bool IsSelfBalancing => (BalancingAccountId ?? Batch?.DefaultBalancingAccountId) is not null;

    /// <summary>The debit and credit figures this line will produce.</summary>
    public (decimal Debit, decimal Credit) Split() => GlEntry.Split(Amount);
}
