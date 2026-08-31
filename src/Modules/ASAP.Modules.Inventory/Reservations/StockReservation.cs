using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Reservations;

/// <summary>
/// Stock spoken for by a particular document, and not yet gone.
/// </summary>
/// <remarks>
/// <para>
/// A reservation is not a movement. It posts nothing, values nothing and does not change what is
/// on hand by so much as a unit. What it changes is what is <em>available</em>, and the difference
/// between those two is the whole idea: on hand is a fact about the shelf, available is that fact
/// less what has already been promised to somebody else.
/// </para>
/// <para>
/// Without it an order promises goods and nothing stops the next order promising the same ones.
/// Both look fine until the second van is loaded, and the person who finds out is a customer.
/// </para>
/// <para>
/// The outstanding quantity is a stored column rather than a subtraction worked out in memory,
/// because every availability check has to sum it in the database. A property the server has to
/// materialise rows to evaluate would turn one arithmetic question into a table scan on every
/// line of every stock movement in the company.
/// </para>
/// </remarks>
public sealed class StockReservation : CompanyEntity
{
    /// <summary>The item being held.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Its number, copied so a list reads without a join.</summary>
    public required string ItemNo { get; set; }

    /// <summary>The variant being held, on an item that has them.</summary>
    public Guid? VariantId { get; set; }

    /// <summary>That variant's code.</summary>
    public string? VariantCode { get; set; }

    /// <summary>Where the stock is being held.</summary>
    public Guid LocationId { get; set; }

    /// <summary>That location's code.</summary>
    public required string LocationCode { get; set; }

    /// <summary>
    /// The document the stock is being held for.
    /// </summary>
    /// <remarks>
    /// The most important field here. Stock reserved for a document is unavailable to everybody
    /// except that document -- otherwise an order could not ship the goods it reserved, which
    /// would make the whole thing an elaborate way of preventing sales.
    /// </remarks>
    public required string DocumentNo { get; set; }

    /// <summary>Which line of it, where the document has lines.</summary>
    public int? DocumentLineNo { get; set; }

    /// <summary>Which module the document belongs to, for a list that reads sensibly.</summary>
    public string? SourceCode { get; set; }

    /// <summary>How much was reserved to begin with.</summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// How much is still being held.
    /// </summary>
    /// <remarks>
    /// Falls as the goods ship against the document, and is set to nought when somebody releases
    /// it. The original quantity stays beside it so a reservation can still say what it was for
    /// after it has been spent.
    /// </remarks>
    public decimal QuantityOutstanding { get; set; }

    /// <summary>Why it was released, where somebody said.</summary>
    public string? ReleaseReason { get; set; }

    /// <summary>A note from whoever reserved it.</summary>
    public string? Note { get; set; }

    /// <summary>How much has gone against it.</summary>
    public decimal QuantityFulfilled => Quantity - QuantityOutstanding;

    /// <summary>Whether it is still holding anything.</summary>
    public bool IsOutstanding => QuantityOutstanding > 0m;
}
