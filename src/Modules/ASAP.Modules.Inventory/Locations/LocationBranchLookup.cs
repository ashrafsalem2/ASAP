using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Locations;

/// <summary>
/// Which branch a location belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Small enough to inline and worth having once anyway, because every module that posts a
/// document needs the same answer to the same question and would otherwise each get it slightly
/// differently. A sale, a purchase and a stock movement all happen somewhere, and the ledger
/// entry has to say where — otherwise it says wherever the person who posted it happened to be
/// signed in, which for anything posted at head office is every shop's takings at head office.
/// </para>
/// <para>
/// Cached for the life of the request. A receipt with forty lines asks about one location forty
/// times, and the answer cannot change while it is being posted.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
public sealed class LocationBranchLookup(AsapDbContext context)
{
    private readonly Dictionary<string, Guid?> known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The branch a location belongs to.
    /// </summary>
    /// <param name="locationCode">The location, or null when the document names none.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The branch, or null where the document names no location or the location names no branch.
    /// Null is a real answer: a central warehouse belongs to the company rather than to a shop.
    /// </returns>
    public async Task<Guid?> BranchOfAsync(
        string? locationCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(locationCode))
        {
            return null;
        }

        if (this.known.TryGetValue(locationCode, out var cached))
        {
            return cached;
        }

        var branchId = await context.Set<Location>()
            .AsNoTracking()
            .Where(l => l.Code == locationCode)
            .Select(static l => l.BranchId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        this.known[locationCode] = branchId;

        return branchId;
    }
}
