using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Items;

/// <summary>How much to order when stock runs down.</summary>
public enum ReorderKind
{
    /// <summary>
    /// Order the same quantity every time, whatever the shortfall.
    /// </summary>
    /// <remarks>
    /// Right where the quantity is decided by something outside the shop — a pallet, a case, a
    /// minimum the vendor will ship. Ordering thirteen because thirteen is the shortfall is not an
    /// option when the vendor sells them in twelves.
    /// </remarks>
    FixedQuantity = 0,

    /// <summary>
    /// Order enough to bring stock back up to a maximum.
    /// </summary>
    /// <remarks>
    /// Right where the constraint is shelf space or cash rather than the vendor. The order varies
    /// with the shortfall, which is what keeps a slow week from filling the stockroom.
    /// </remarks>
    UpToMaximum = 1,
}

/// <summary>
/// When to reorder an item at one place, and how much.
/// </summary>
/// <remarks>
/// <para>
/// Per location, because a level that is right for a central warehouse is wrong for a branch that
/// gets a delivery twice a week. The figures on the item itself stay as the company-wide default
/// for anywhere with no policy of its own, so nothing has to be entered twice to keep working.
/// </para>
/// <para>
/// A policy says what to order, not when it will arrive. The lead time is here because the
/// worksheet has to date the order somehow, but it is a planning figure and nothing posts
/// against it.
/// </para>
/// </remarks>
public sealed class ReorderPolicy : CompanyEntity
{
    /// <summary>The item.</summary>
    public required string ItemNo { get; set; }

    /// <summary>Where it is stocked.</summary>
    public required string LocationCode { get; set; }

    /// <summary>Which variant, on an item that has them, or null for the item as a whole.</summary>
    public string? VariantCode { get; set; }

    /// <summary>How much to order once the point is reached.</summary>
    public ReorderKind Kind { get; set; } = ReorderKind.FixedQuantity;

    /// <summary>
    /// The level at or below which it should be reordered.
    /// </summary>
    /// <remarks>
    /// Measured against what is free and what is already coming, not against what is on the shelf.
    /// A shop with two left and forty on a lorry does not need forty more.
    /// </remarks>
    public decimal ReorderPoint { get; set; }

    /// <summary>How much to order, on a fixed-quantity policy.</summary>
    public decimal ReorderQuantity { get; set; }

    /// <summary>The level to order back up to, on an up-to-maximum policy.</summary>
    public decimal MaximumInventory { get; set; }

    /// <summary>The least the vendor will ship, or zero where there is no minimum.</summary>
    public decimal MinimumOrderQuantity { get; set; }

    /// <summary>
    /// The pack the item is sold in, or zero where it may be ordered singly.
    /// </summary>
    /// <remarks>
    /// A suggestion is rounded <em>up</em> to the next whole multiple, never down. Rounding down
    /// would leave the shop below the point it just decided it needed to be above, and the
    /// worksheet would suggest the same order again tomorrow.
    /// </remarks>
    public decimal OrderMultiple { get; set; }

    /// <summary>Days between placing an order and the goods arriving, for dating the order.</summary>
    public int LeadTimeDays { get; set; }

    /// <summary>A vendor this is normally bought from. A suggestion, not a commitment.</summary>
    public string? VendorNo { get; set; }

    /// <summary>Whether the worksheet still looks at it.</summary>
    public bool IsActive { get; set; } = true;
}
