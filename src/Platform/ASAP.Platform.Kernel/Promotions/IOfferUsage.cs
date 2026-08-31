namespace ASAP.Platform.Kernel.Promotions;

/// <summary>One line somewhere that an offer took money off.</summary>
/// <param name="OfferCode">The offer that applied.</param>
/// <param name="SoldOn">The day it sold.</param>
/// <param name="DocumentNo">The document it sold on, so a cost can be found for it.</param>
/// <param name="ItemNo">What sold.</param>
/// <param name="Quantity">How much.</param>
/// <param name="DiscountAmount">What the offer gave away. Positive.</param>
/// <param name="NetAmount">What the customer actually paid for the line, after the offer.</param>
/// <param name="UnitCostAtSale">
/// What the goods cost per unit at the moment they sold, or nothing where nobody recorded it.
/// Deliberately nullable: a margin report that cannot tell "cost nothing" from "cost unknown"
/// reports a hundred per cent on every line it has no figure for, which is a confident answer
/// produced entirely by missing data.
/// </param>
/// <param name="SourceCode">Which module the document belongs to.</param>
public readonly record struct OfferUsageLine(
    string OfferCode,
    DateOnly SoldOn,
    string DocumentNo,
    string ItemNo,
    decimal Quantity,
    decimal DiscountAmount,
    decimal NetAmount,
    decimal? UnitCostAtSale,
    string SourceCode);

/// <summary>
/// Answers where an offer was actually used.
/// </summary>
/// <remarks>
/// <para>
/// Promotions decides what an offer does and refuses the ones that would sell below cost. It has
/// no idea what happened afterwards, because the documents an offer lands on belong to whichever
/// module sold the goods -- and those modules depend on Promotions rather than the other way
/// about, so Promotions cannot go and look.
/// </para>
/// <para>
/// So it asks. Every module that sells under an offer answers for its own documents, exactly as
/// every module owning a party answers <c>IDocumentParties</c>. A report built any other way would
/// either reach across the module graph the wrong way or quietly cover only one of the doors a
/// sale can come through.
/// </para>
/// </remarks>
public interface IOfferUsage
{
    /// <summary>Which module is answering, for a report that has to say where a figure came from.</summary>
    string SourceCode { get; }

    /// <summary>
    /// Every line in a period that an offer applied to.
    /// </summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The lines, or nothing where this module sold none under an offer.</returns>
    Task<IReadOnlyList<OfferUsageLine>> BetweenAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
