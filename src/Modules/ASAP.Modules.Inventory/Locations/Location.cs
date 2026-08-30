using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Locations;

/// <summary>
/// A place stock is held: a shop floor, a stockroom, a warehouse, a van.
/// </summary>
/// <remarks>
/// Separate from <see cref="ASAP.Platform.Core.Tenancy.Branch"/> because the two answer different
/// questions. A branch is where the business operates and where people are posted; a location is
/// where goods physically sit. One shop is one branch and often three locations -- the shelf, the
/// stockroom, and the bay where returns wait to be checked -- and stock moves between them without
/// ever leaving the branch.
/// </remarks>
public sealed class Location : CompanyEntity
{
    /// <summary>Location code, for example <c>RUH-SHOP</c>.</summary>
    public required string Code { get; set; }

    /// <summary>Location name.</summary>
    public required string Name { get; set; }

    /// <summary>Location name in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>The branch it belongs to. Null for a central warehouse serving every branch.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>Street address, for deliveries and transfer paperwork.</summary>
    public string? Address { get; set; }

    /// <summary>
    /// Whether stock at this location may be sold or shipped.
    /// </summary>
    /// <remarks>
    /// Off for a quarantine bay, where goods physically exist and are counted in the valuation but
    /// must not be promised to a customer until they are checked.
    /// </remarks>
    public bool IsSellable { get; set; } = true;

    /// <summary>
    /// Whether this location holds goods in transit between two others.
    /// </summary>
    /// <remarks>
    /// A transfer takes stock out of one location and does not put it into the next until it
    /// arrives, which can be days. Without somewhere to hold it in between, the goods vanish from
    /// the valuation for the length of the journey and the inventory account disagrees with the
    /// balance sheet until they land.
    /// </remarks>
    public bool IsInTransit { get; set; }

    /// <summary>
    /// Whether goods here are tracked down to a bin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default, and a location with it off is a complete working location. A shop with one
    /// stockroom does not need to name a shelf, and being made to would be a cost with nothing
    /// behind it.
    /// </para>
    /// <para>
    /// On, every movement here has to say which bin -- otherwise the bins hold a picture of the
    /// stock that is quietly wrong from the first receipt that skipped one, and nobody finds out
    /// until a picker is sent to an empty shelf.
    /// </para>
    /// </remarks>
    public bool UsesBins { get; set; }

    /// <summary>Whether the location may be used at all.</summary>
    public bool IsBlocked { get; set; }
}
