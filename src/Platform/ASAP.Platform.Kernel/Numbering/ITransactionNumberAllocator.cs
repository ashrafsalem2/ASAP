namespace ASAP.Platform.Kernel.Numbering;

/// <summary>
/// Issues the number that groups every entry written by one posting.
/// </summary>
/// <remarks>
/// <para>
/// A platform service rather than a Finance one, because a transaction number spans modules. One
/// sale writes an item ledger entry, a value entry and four general ledger entries, and they all
/// carry the same number -- which is what makes "show me this whole transaction" a single query
/// instead of a reconstruction.
/// </para>
/// <para>
/// The first draft put the counter in the Finance schema and had Inventory read it, which would
/// have meant Inventory could not run without Finance installed. Modules meet through the platform
/// or through events, never through each other's tables.
/// </para>
/// <para>
/// Allocated once per posting rather than once per entry. A counter per entry would serialise
/// every line of every receipt on one row, which at till volume is the difference between a queue
/// moving and a queue not.
/// </para>
/// </remarks>
public interface ITransactionNumberAllocator
{
    /// <summary>
    /// Takes the next transaction number for the active company.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The allocated number.</returns>
    /// <remarks>
    /// A single atomic statement, not a read followed by a write. Two tills posting at the same
    /// moment would otherwise both read the same last number and both claim the next one,
    /// producing two transactions that share an identifier -- which nothing downstream could
    /// untangle, since the whole point of the number is that it groups one posting.
    /// </remarks>
    Task<long> NextAsync(CancellationToken cancellationToken = default);
}
