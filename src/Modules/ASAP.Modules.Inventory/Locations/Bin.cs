using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Locations;

/// <summary>
/// A place inside a location: an aisle, a shelf, a pallet position.
/// </summary>
/// <remarks>
/// <para>
/// A bin is a refinement of a location, never a substitute for one. Every stock figure, every
/// valuation and every cost layer is per location and stays that way; the bin only says where
/// inside that location the goods are standing.
/// </para>
/// <para>
/// That line matters because the alternative is tempting and wrong. Costing per bin would mean the
/// same item at the same location having two costs depending on which shelf it was picked from,
/// and a stock figure that has to be summed across bins before it can be compared to the ledger.
/// Bins answer "where is it", not "how much is there" and never "what is it worth".
/// </para>
/// <para>
/// Which is also why a location without bins is a complete, working location. A shop with one
/// stockroom does not need to name a shelf, and being made to would be a cost with no answer
/// behind it.
/// </para>
/// </remarks>
public sealed class Bin : CompanyEntity
{
    /// <summary>The location it is inside.</summary>
    public Guid LocationId { get; set; }

    /// <summary>The location, when loaded.</summary>
    public Location? Location { get; set; }

    /// <summary>Bin code, for example <c>A-01-3</c>. Unique within its location.</summary>
    /// <remarks>
    /// Within its location, not within the company: two warehouses both having an <c>A-01</c> is
    /// ordinary, and forcing them apart would make every code carry the warehouse name twice.
    /// </remarks>
    public required string Code { get; set; }

    /// <summary>What it is called, when a code is not enough.</summary>
    public string? Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>
    /// Where goods land when they arrive and nobody said where to put them.
    /// </summary>
    /// <remarks>
    /// One per location at most. Without it, switching a location to bins would refuse every
    /// receipt until somebody had walked the aisles, which is how a feature meant to help a
    /// warehouse stops it working instead.
    /// </remarks>
    public bool IsReceiving { get; set; }

    /// <summary>
    /// The order a picker walks the bins in. Lower is walked first.
    /// </summary>
    /// <remarks>
    /// A number rather than the code's alphabetical order, because the shortest walk through a
    /// warehouse is a fact about its floor plan and not about how its shelves happen to be named.
    /// </remarks>
    public int PickOrder { get; set; }

    /// <summary>Whether the bin may be used at all.</summary>
    /// <remarks>
    /// Blocked rather than deleted, for a shelf being restacked or a position under repair. What
    /// is already in it stays counted, because it is still physically there.
    /// </remarks>
    public bool IsBlocked { get; set; }
}
