using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Finance.Banking;

/// <summary>Where a statement has got to.</summary>
public enum BankStatementStatus
{
    /// <summary>Being worked on. Lines may be matched, unmatched and changed.</summary>
    Open = 0,

    /// <summary>
    /// Agreed and closed. Every difference between the bank and the books has been accounted for.
    /// </summary>
    Reconciled = 1,
}

/// <summary>
/// One bank statement, and the work of agreeing it with the ledger.
/// </summary>
/// <remarks>
/// <para>
/// A reconciliation is not a report. It is a claim — that the difference between what the bank
/// says and what the books say is made up entirely of items somebody can name — and the claim is
/// either proved or it is not. So this refuses to close until the arithmetic works, and when it
/// does not, it says by how much and what is left over.
/// </para>
/// <para>
/// The opening balance is not taken from the ledger. It is what the bank's statement says it is,
/// which is the whole point: if the two disagreed at the start of the period, that disagreement
/// is the previous reconciliation's business and has to have been settled there.
/// </para>
/// </remarks>
public sealed class BankStatement : CompanyEntity
{
    /// <summary>The bank account being reconciled.</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>Navigation to the account.</summary>
    public BankAccount? BankAccount { get; set; }

    /// <summary>The statement number the bank gave it.</summary>
    public required string No { get; set; }

    /// <summary>The day the statement was drawn up, which is the day it is reconciled to.</summary>
    public DateOnly StatementDate { get; set; }

    /// <summary>What the bank says the balance was at the start.</summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>What the bank says the balance was at the end.</summary>
    public decimal ClosingBalance { get; set; }

    /// <summary>Where it has got to.</summary>
    public BankStatementStatus Status { get; set; } = BankStatementStatus.Open;

    /// <summary>When it was agreed.</summary>
    public DateOnly? ReconciledOn { get; set; }

    /// <summary>Who agreed it.</summary>
    public Guid? ReconciledBy { get; set; }

    /// <summary>The lines the bank sent.</summary>
    public ICollection<BankStatementLine> Lines { get; set; } = [];

    /// <summary>What the statement itself says moved over the period.</summary>
    public decimal StatementMovement => ClosingBalance - OpeningBalance;

    /// <summary>What the lines add up to.</summary>
    /// <remarks>
    /// Should equal <see cref="StatementMovement"/>. When it does not, the statement was keyed or
    /// imported wrong, and no amount of matching will ever make it close — which is worth saying
    /// before somebody spends an afternoon looking for the difference in the ledger.
    /// </remarks>
    public decimal LineTotal => Lines.Sum(static l => l.Amount);

    /// <summary>Whether anything may still be changed.</summary>
    public bool IsEditable => Status is BankStatementStatus.Open;
}

/// <summary>
/// One line of a bank statement, and what in the ledger it turned out to be.
/// </summary>
/// <remarks>
/// The match is held here rather than on the ledger entry, and not for convenience. A posted
/// entry is never updated — see <see cref="Ledger.GlEntry"/> — so "has this entry been
/// reconciled" is answered by asking which statement line points at it. That keeps the ledger
/// exactly as immutable as it claims to be, and it means unmatching is deleting a pointer rather
/// than editing history.
/// </remarks>
public sealed class BankStatementLine : CompanyEntity
{
    /// <summary>The statement this belongs to.</summary>
    public Guid BankStatementId { get; set; }

    /// <summary>Navigation to the statement.</summary>
    public BankStatement? BankStatement { get; set; }

    /// <summary>The day the bank says it happened.</summary>
    public DateOnly TransactionDate { get; set; }

    /// <summary>What the bank calls it, which is usually all there is to go on.</summary>
    public required string Description { get; set; }

    /// <summary>The bank's own reference, when it gave one.</summary>
    public string? Reference { get; set; }

    /// <summary>
    /// The amount, signed the way the ledger account moves: positive money in, negative out.
    /// </summary>
    /// <remarks>
    /// Stated on the ledger's convention rather than the bank's, on purpose. A bank statement
    /// shows a deposit as a credit because the account is the bank's liability to you; the same
    /// deposit debits your cash account. Importing them in the bank's signs and flipping later
    /// is where reconciliations go wrong, so they are turned once, at the edge.
    /// </remarks>
    public decimal Amount { get; set; }

    /// <summary>The ledger entry this line turned out to be, once somebody has said so.</summary>
    public Guid? MatchedEntryId { get; set; }

    /// <summary>
    /// Why a line has no ledger entry, when that is the answer.
    /// </summary>
    /// <remarks>
    /// A bank charge nobody knew about is a real line with no entry behind it — until somebody
    /// posts one. Recording the reason is what stops it being silently ignored month after month.
    /// </remarks>
    public string? Note { get; set; }

    /// <summary>Whether this line has been accounted for.</summary>
    public bool IsMatched => MatchedEntryId is not null;
}
