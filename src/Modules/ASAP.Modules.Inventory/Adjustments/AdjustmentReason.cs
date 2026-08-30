using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Adjustments;

/// <summary>Which way a reason may move stock.</summary>
public enum AdjustmentDirection
{
    /// <summary>Either way. A count difference goes both ways and cannot say which in advance.</summary>
    Either = 0,

    /// <summary>Only upwards. Goods found, a miscount in the company's favour.</summary>
    IncreaseOnly = 1,

    /// <summary>Only downwards. Breakage, theft, expiry, a sample given away.</summary>
    DecreaseOnly = 2,
}

/// <summary>
/// Why stock was adjusted.
/// </summary>
/// <remarks>
/// <para>
/// Without one, every write-off is a bare negative adjustment and breakage, theft and expiry are
/// indistinguishable. They have the same effect on quantity and almost nothing else in common:
/// breakage is a warehouse conversation, theft is a security one, expiry is a buying one, and each
/// belongs in a different expense account. A single figure covering all three answers none of
/// those questions.
/// </para>
/// <para>
/// So a reason carries the account the loss lands in. That is what makes it more than a label: the
/// person adjusting stock says "breakage" and the cost reaches the right account without them
/// knowing which one it is.
/// </para>
/// </remarks>
public sealed class AdjustmentReason : CompanyEntity
{
    /// <summary>Its code, for example <c>BREAKAGE</c>.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>
    /// Where the value lands, or null to use the item category's variance account.
    /// </summary>
    /// <remarks>
    /// The point of the whole thing. Shrinkage, breakage and goods given away as samples are three
    /// different lines in a set of accounts, and the person at the shelf should not have to know
    /// which is which.
    /// </remarks>
    public string? ContraAccountNo { get; set; }

    /// <summary>
    /// Which way it may move stock.
    /// </summary>
    /// <remarks>
    /// Breakage cannot increase stock and goods found cannot decrease it. A reason used the wrong
    /// way round is a keying error that produces a perfectly valid-looking entry, so it is worth
    /// catching at the point it is made rather than in a report that nobody reconciles.
    /// </remarks>
    public AdjustmentDirection Direction { get; set; }

    /// <summary>
    /// Whether the person adjusting has to say something as well as choosing this.
    /// </summary>
    /// <remarks>
    /// On for the ones that will be asked about later. A theft write-off with nothing written
    /// against it is a row somebody has to reconstruct from memory months afterwards.
    /// </remarks>
    public bool RequiresNote { get; set; }

    /// <summary>Whether it may still be chosen.</summary>
    /// <remarks>
    /// Withdrawn rather than deleted, because entries already posted against it keep pointing at
    /// it and a report covering last year still has to be able to name it.
    /// </remarks>
    public bool IsActive { get; set; } = true;

    /// <summary>Whether this reason may be used for a movement of the given sign.</summary>
    /// <param name="quantity">The signed quantity.</param>
    /// <returns>True when the direction permits it.</returns>
    public bool Permits(decimal quantity)
        => Direction switch
        {
            AdjustmentDirection.IncreaseOnly => quantity > 0m,
            AdjustmentDirection.DecreaseOnly => quantity < 0m,
            _ => true,
        };
}
