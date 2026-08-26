using ASAP.Platform.Kernel.Events;

namespace ASAP.Platform.Kernel.Accounting;

/// <summary>One side of a posting: an amount against an account.</summary>
/// <param name="AccountNo">The general ledger account number.</param>
/// <param name="Amount">Signed. Positive debits the account, negative credits it.</param>
/// <param name="Description">What the entry should say on the ledger.</param>
public readonly record struct LedgerPostingLine(string AccountNo, decimal Amount, string Description);

/// <summary>
/// A module asking for value to be posted to the general ledger.
/// </summary>
/// <remarks>
/// <para>
/// This is how two modules do business without knowing about each other. Inventory has to put the
/// value of a stock movement into the ledger, and Finance owns the ledger -- but Inventory cannot
/// reference Finance and Finance cannot reference Inventory, because either reference would mean
/// one could not be sold without the other. So the contract lives in the kernel, which both
/// already depend on: Inventory raises this, Finance subscribes to it, and neither has heard of
/// the other.
/// </para>
/// <para>
/// The consequence is worth stating plainly, because it is the point of the whole architecture. On
/// an installation with Inventory and no Finance, nothing subscribes and nothing happens: stock
/// still moves, item ledger entries and value entries are still written, and there is simply no
/// general ledger for the value to reach. Install Finance later and the postings begin, with no
/// change to a line of Inventory code.
/// </para>
/// <para>
/// A domain event rather than an integration event, deliberately. Stock and its value have to
/// commit together -- an item ledger entry that survived while the ledger posting rolled back
/// would put the inventory account permanently out of step with the stock valuation, and nothing
/// would say when it happened.
/// </para>
/// </remarks>
public sealed class LedgerPostingRequested : IDomainEvent
{
    /// <inheritdoc />
    public string EventName => "Platform.LedgerPostingRequested";

    /// <summary>Which module is asking, for example <c>Inventory</c>.</summary>
    public required string SourceModule { get; init; }

    /// <summary>Where the entries came from, stamped on each, for example <c>POS</c>.</summary>
    public required string SourceCode { get; init; }

    /// <summary>The date the entries should be reported in.</summary>
    public DateOnly PostingDate { get; init; }

    /// <summary>The document behind the posting.</summary>
    public string? DocumentNo { get; init; }

    /// <summary>
    /// The transaction number the asking module used, so its entries and the ledger entries they
    /// caused can be read as one transaction.
    /// </summary>
    public long SourceTransactionNo { get; init; }

    /// <summary>
    /// The amounts to post. They must sum to zero: a module asking for an unbalanced posting has
    /// made a mistake, and the ledger will refuse it rather than absorb it.
    /// </summary>
    public required IReadOnlyList<LedgerPostingLine> Lines { get; init; }

    /// <summary>The dimension combination the entries should carry.</summary>
    public Guid? DimensionSetId { get; init; }

    /// <summary>
    /// True when nothing was posted because no module is listening.
    /// </summary>
    /// <remarks>
    /// Set by the handler that answers. The asking module reads it back to record that the value
    /// has not reached a ledger, which matters on an installation running Inventory alone: the
    /// value entries are still the truth, they simply have nowhere to be summarised to yet.
    /// </remarks>
    public bool WasHandled { get; set; }
}
