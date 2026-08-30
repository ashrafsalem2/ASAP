using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Ledger;

/// <summary>Why a cost was recorded.</summary>
public enum ValueEntryType
{
    /// <summary>The cost of the movement itself.</summary>
    DirectCost = 0,

    /// <summary>Freight, duty or another charge added to the goods after the fact.</summary>
    IndirectCost = 1,

    /// <summary>
    /// A correction to a cost already posted. This is what settles an estimate once the real
    /// figure is known.
    /// </summary>
    Revaluation = 2,

    /// <summary>The difference between a standard cost and what was actually paid.</summary>
    Variance = 3,

    /// <summary>A rounding difference left over when a cost is spread across units.</summary>
    Rounding = 4,
}

/// <summary>
/// What a stock movement was worth.
/// </summary>
/// <remarks>
/// <para>
/// Many value entries can point at one item ledger entry, and that is the whole point. A sale made
/// out of stock that was not there gets a <see cref="ValueEntryType.DirectCost"/> entry carrying
/// an estimate, and later a <see cref="ValueEntryType.Revaluation"/> entry carrying the
/// difference once the goods arrive and the real cost is known. Both stay, so the estimate and
/// its correction are visible, and the sum is the truth.
/// </para>
/// <para>
/// <see cref="IsExpected"/> marks a cost that is not final. Expected cost is excluded from the
/// figures posted to the general ledger until it settles, which is what keeps the inventory
/// account in agreement with the stock valuation report rather than drifting from it by the size
/// of everything currently estimated.
/// </para>
/// </remarks>
public sealed class ValueEntry : LedgerEntity
{
    /// <summary>The movement this cost belongs to.</summary>
    public Guid ItemLedgerEntryId { get; set; }

    /// <summary>Navigation to the movement.</summary>
    public ItemLedgerEntry? ItemLedgerEntry { get; set; }

    /// <summary>The item, copied so cost can be reported without joining.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Item number, copied for the same reason.</summary>
    public required string ItemNo { get; set; }

    /// <summary>Why this cost was recorded.</summary>
    public ValueEntryType EntryType { get; set; }

    /// <summary>What kind of movement it values.</summary>
    public ItemLedgerEntryType ItemLedgerEntryType { get; set; }

    /// <summary>The date the cost is reported in.</summary>
    public DateOnly PostingDate { get; set; }

    /// <summary>The quantity this cost covers, signed the same way as the movement.</summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// The cost, signed. Positive adds to the value of stock, negative takes away.
    /// </summary>
    public decimal CostAmount { get; set; }

    /// <summary>Cost per unit, kept for reporting rather than recalculated everywhere.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>What the goods sold for, on an outbound movement. Zero on a receipt.</summary>
    public decimal SalesAmount { get; set; }

    /// <summary>
    /// True while the cost is an estimate rather than a settled figure.
    /// </summary>
    /// <remarks>
    /// Set on a sale made from stock that was not there, and cleared when the receipt arrives and
    /// the adjustment runs. Expected cost stays out of the general ledger until it settles.
    /// </remarks>
    public bool IsExpected { get; set; }

    /// <summary>The document that caused it.</summary>
    public string? DocumentNo { get; set; }

    /// <summary>The transaction grouping every entry written by one posting.</summary>
    public long TransactionNo { get; set; }

    /// <summary>Where it came from, for example <c>POS</c> or <c>PURCH</c>.</summary>
    public required string SourceCode { get; set; }

    /// <summary>The general ledger transaction this cost was posted as, once it has been.</summary>
    public long? GlTransactionNo { get; set; }

    /// <summary>Whether this cost has reached the general ledger yet.</summary>
    public bool IsPostedToGl { get; set; }

    /// <summary>The dimension combination the cost carries.</summary>
    public Guid? DimensionSetId { get; set; }

    /// <summary>Branch the cost belongs to.</summary>
    public Guid? BranchId { get; set; }
}

/// <summary>
/// Records that a quantity leaving stock was taken from a particular receipt.
/// </summary>
/// <remarks>
/// <para>
/// This is what FIFO actually produces: not a calculation but a record of which receipt covered
/// which issue. Keeping it means the cost of a sale can always be explained -- "these forty came
/// from the receipt of the eighth at 12.50, these ten from the twelfth at 13.00" -- rather than
/// being a number that appeared from an average nobody can reconstruct.
/// </para>
/// <para>
/// It is also what makes negative stock recoverable. A sale with no receipt to draw from has no
/// application row, so the routine that settles cost knows exactly which entries are still waiting
/// and which receipt to match them against when one arrives.
/// </para>
/// </remarks>
public sealed class ItemApplicationEntry : LedgerEntity
{
    /// <summary>The item this application concerns.</summary>
    public Guid ItemId { get; set; }

    /// <summary>The entry that took stock out.</summary>
    /// <summary>
    /// The variant, where the item has them.
    /// </summary>
    /// <remarks>
    /// Carried on the application rather than looked up from the outbound entry, because this is
    /// what the settlement routine searches receipts by and a query that had to join to find it
    /// would be the place somebody later dropped the join. A blue shortfall is settled by a blue
    /// receipt or by nothing.
    /// </remarks>
    public Guid? VariantId { get; set; }

    public Guid OutboundEntryId { get; set; }

    /// <summary>The receipt it was taken from. Null while the stock has not arrived yet.</summary>
    public Guid? InboundEntryId { get; set; }

    /// <summary>How many units of the outbound entry this row accounts for. Always positive.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The date the application was made.</summary>
    public DateOnly PostingDate { get; set; }

    /// <summary>
    /// True when the outbound entry ran ahead of its receipt, so the cost on it is an estimate
    /// waiting to be settled.
    /// </summary>
    public bool IsOutstanding { get; set; }
}
