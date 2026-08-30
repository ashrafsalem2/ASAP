namespace ASAP.Platform.Kernel.Documents;

/// <summary>Who a document was with.</summary>
/// <param name="DocumentNo">The document number as it appears on the ledger entries.</param>
/// <param name="PartyNo">The customer or vendor number.</param>
/// <param name="PartyName">Their name as it stood when the document was raised.</param>
public readonly record struct DocumentParty(string DocumentNo, string PartyNo, string PartyName);

/// <summary>
/// Says who was on the other side of a document, for modules that cannot see the one that owns it.
/// </summary>
/// <remarks>
/// <para>
/// The item ledger records what moved, not who wanted it: a stock entry carries a document number
/// and nothing about the party, because stock is stock whoever asked for it. That is the right
/// shape for the ledger and it leaves one gap, which is any report that has to group by customer.
/// </para>
/// <para>
/// The gap is awkward because a sale comes through more than one door. An invoice belongs to Sales
/// and a till receipt to Point of Sale, the two modules do not reference each other, and a margin
/// report that only understood one of them would quietly report half a company's trade.
/// </para>
/// <para>
/// So every module that owns a document with a party on it answers this, and whoever is asking
/// takes all the answers. A module that owns none simply registers nothing, and a report spanning
/// channels then covers exactly the channels that are installed.
/// </para>
/// </remarks>
public interface IDocumentParties
{
    /// <summary>
    /// Finds who each of these documents was with, ignoring any it does not recognise.
    /// </summary>
    /// <param name="documentNos">The document numbers to look up.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// One entry per document this module owns. Documents belonging to another module are left
    /// out rather than reported as unknown, because another implementation will answer for them.
    /// </returns>
    Task<IReadOnlyList<DocumentParty>> ForAsync(
        IReadOnlyCollection<string> documentNos,
        CancellationToken cancellationToken = default);
}
