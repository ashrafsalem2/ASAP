using ASAP.Platform.Kernel.Entities;

namespace ASAP.Platform.Core.Tenancy;

/// <summary>What a branch is for, which decides what ASAP lets it do.</summary>
public enum BranchKind
{
    /// <summary>Head office. Sees every branch, and is where policy is set.</summary>
    HeadOffice = 0,

    /// <summary>A shop with tills. Sells, and holds stock it sells from.</summary>
    Store = 1,

    /// <summary>A warehouse. Holds and moves stock, but does not sell.</summary>
    Warehouse = 2,

    /// <summary>An office with neither stock nor tills, such as a regional admin site.</summary>
    Office = 3,
}

/// <summary>
/// A physical place a company operates from: a shop, a warehouse, head office.
/// </summary>
/// <remarks>
/// <para>
/// A branch is where the point of sale runs, where stock physically sits, and where employees
/// are posted. Branch-scoped data is visible to that branch and to head office; it is not
/// visible sideways to a sibling branch, so one shop cannot read another till.
/// </para>
/// <para>
/// A store branch keeps working when the link to head office drops and reconciles afterwards,
/// which is why it carries its own synchronisation state rather than assuming it is always online.
/// </para>
/// </remarks>
public sealed class Branch : CompanyEntity
{
    /// <summary>Short stable code, for example <c>RUH-01</c>.</summary>
    public required string Code { get; set; }

    /// <summary>Branch name.</summary>
    public required string Name { get; set; }

    /// <summary>Branch name in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>What this branch is for.</summary>
    public BranchKind Kind { get; set; } = BranchKind.Store;

    /// <summary>Street address, for documents and delivery.</summary>
    public string? Address { get; set; }

    /// <summary>City the branch is in.</summary>
    public string? City { get; set; }

    /// <summary>Contact telephone number.</summary>
    public string? Phone { get; set; }

    /// <summary>
    /// IANA time zone, when the branch is in a different one from the tenant. Null means it
    /// follows the tenant.
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// The inventory location stock at this branch is held under. Null for a branch that holds
    /// no stock, such as an office. Points at an inventory location once that module is present;
    /// held as a bare key so the platform need not depend on the Inventory module.
    /// </summary>
    public Guid? DefaultLocationId { get; set; }

    /// <summary>Whether the branch may trade.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When head office last received data from this branch, in UTC. Null means never. The
    /// branch monitor uses this to show which shops have gone quiet.
    /// </summary>
    public DateTime? LastSyncedAtUtc { get; set; }
}
