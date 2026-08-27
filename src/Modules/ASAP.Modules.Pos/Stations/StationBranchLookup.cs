using ASAP.Modules.Inventory.Locations;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Pos.Stations;

/// <summary>
/// Which branch a till stands in.
/// </summary>
/// <remarks>
/// The station's own branch first, because that is somebody stating it. Where it is not set, the
/// branch of the location the till sells out of, which is the same answer arrived at the long way
/// and is right often enough to be worth trying before giving up. Getting this wrong does not
/// fail; it quietly reports the whole chain's takings at head office.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="locations">Says which branch a location belongs to.</param>
public sealed class StationBranchLookup(AsapDbContext context, LocationBranchLookup locations)
{
    private readonly Dictionary<string, Guid?> known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The branch a till belongs to.
    /// </summary>
    /// <param name="stationCode">The till.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The branch, or null where neither the station nor its location names one.</returns>
    public async Task<Guid?> BranchOfAsync(
        string? stationCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stationCode))
        {
            return null;
        }

        if (this.known.TryGetValue(stationCode, out var cached))
        {
            return cached;
        }

        var station = await context.Set<PosStation>()
            .AsNoTracking()
            .Where(s => s.Code == stationCode)
            .Select(static s => new { s.BranchId, s.LocationCode })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var branchId = station is null
            ? null
            : station.BranchId
              ?? await locations
                  .BranchOfAsync(station.LocationCode, cancellationToken)
                  .ConfigureAwait(false);

        this.known[stationCode] = branchId;

        return branchId;
    }
}
