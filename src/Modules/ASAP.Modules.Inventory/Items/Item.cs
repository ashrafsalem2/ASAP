using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Inventory.Items;

/// <summary>
/// How the cost of what leaves stock is worked out.
/// </summary>
/// <remarks>
/// Fixed per item and locked once anything has posted, because the method decides what every
/// existing value entry meant. Changing it afterwards would not recalculate history; it would
/// leave old entries valued one way and new ones another, and no report could reconcile the two.
/// </remarks>
public enum CostingMethod
{
    /// <summary>
    /// First in, first out. What leaves carries the cost of the oldest stock still on hand.
    /// The default, and what most goods want.
    /// </summary>
    Fifo = 0,

    /// <summary>
    /// Weighted average over everything on hand. Suited to goods that are genuinely
    /// interchangeable and bought at drifting prices -- fuel, grain, loose stock.
    /// </summary>
    Average = 1,

    /// <summary>
    /// A fixed cost set by the business, with the difference from what was actually paid posted
    /// to a variance account. Used in manufacturing, where the variance is the number management
    /// actually wants to see.
    /// </summary>
    Standard = 2,

    /// <summary>
    /// Each unit carries its own cost, tracked by serial or lot. For goods where the individual
    /// item genuinely differs: vehicles, jewellery, machinery.
    /// </summary>
    Specific = 3,
}

/// <summary>Whether an item is stocked, or is a service with no stock at all.</summary>
public enum ItemKind
{
    /// <summary>Physical goods that occupy a location and carry a cost.</summary>
    Inventory = 0,

    /// <summary>Labour or a fee. Sells and posts to finance, but never moves stock.</summary>
    Service = 1,

    /// <summary>
    /// A cost added to other items rather than sold on its own -- freight, duty, insurance.
    /// Spread across the goods it relates to so the landed cost is right.
    /// </summary>
    Charge = 2,
}

/// <summary>
/// Something the business buys, holds or sells.
/// </summary>
public sealed class Item : CompanyEntity
{
    /// <summary>Item number, for example <c>ITEM-1001</c>.</summary>
    public required string No { get; set; }

    /// <summary>Description shown on documents and lists.</summary>
    public required string Description { get; set; }

    /// <summary>Description in Arabic, as it appears on an Arabic invoice.</summary>
    public string? DescriptionArabic { get; set; }

    /// <summary>Whether it is stocked, a service, or a charge spread over other items.</summary>
    public ItemKind Kind { get; set; } = ItemKind.Inventory;

    /// <summary>The category it belongs to, for reporting and for defaulting its posting accounts.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Navigation to the category.</summary>
    public ItemCategory? Category { get; set; }

    /// <summary>
    /// Whether this item is stocked as separate colours, sizes or flavours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off, and nothing about the item changes: every entry carries no variant and the arithmetic
    /// is exactly what it always was. On, every movement has to say which variant, because a
    /// variant splits the stock, the cost layers and the valuation again, and a movement that does
    /// not say which one would have to be guessed at.
    /// </para>
    /// <para>
    /// Guessing is the one thing that must never happen here. It does not fail: it costs a blue
    /// shirt against a red receipt, and the only symptom is a margin quietly wrong on both.
    /// </para>
    /// </remarks>
    public bool HasVariants { get; set; }

    /// <summary>The unit it is counted in, for example <c>PCS</c> or <c>KG</c>.</summary>
    public required string BaseUnitOfMeasure { get; set; }

    /// <summary>How the cost of what leaves stock is worked out.</summary>
    public CostingMethod CostingMethod { get; set; } = CostingMethod.Fifo;

    /// <summary>
    /// The fixed cost used when <see cref="CostingMethod"/> is
    /// <see cref="Inventory.Items.CostingMethod.Standard"/>.
    /// </summary>
    public decimal StandardCost { get; set; }

    /// <summary>
    /// Cost per unit of everything currently on hand, maintained as entries post.
    /// </summary>
    /// <remarks>
    /// Denormalised for speed, and it is also the figure used to value an outbound movement made
    /// when there is no stock to draw from. That second use is why it is kept current rather than
    /// computed on demand.
    /// </remarks>
    public decimal UnitCost { get; set; }

    /// <summary>What the last receipt of this item actually cost per unit.</summary>
    public decimal LastDirectCost { get; set; }

    /// <summary>The list price before any discount.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Total quantity on hand across every location. Maintained as entries post.</summary>
    public decimal QuantityOnHand { get; set; }

    /// <summary>Primary barcode, for scanning at a till or on a receiving bay.</summary>
    public string? Barcode { get; set; }

    /// <summary>Level at which the item should be reordered.</summary>
    public decimal ReorderPoint { get; set; }

    /// <summary>How much to order when the reorder point is reached.</summary>
    public decimal ReorderQuantity { get; set; }

    /// <summary>Whether the item may be bought, sold or moved at all.</summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Whether the item may be sold when there is none on hand, overriding the company setting.
    /// Null follows the company.
    /// </summary>
    /// <remarks>
    /// Per item as well as per company because the answer genuinely differs by item. A shop may be
    /// happy to sell a loose-weight good it knows is on the shelf but not yet received in the
    /// system, and unwilling to do the same for a serialised appliance.
    /// </remarks>
    public bool? AllowNegativeInventory { get; set; }

    /// <summary>Whether the item is tracked by serial number.</summary>
    public bool IsSerialTracked { get; set; }

    /// <summary>Whether the item is tracked by lot or batch.</summary>
    public bool IsLotTracked { get; set; }

    /// <summary>True once anything has posted, which locks the costing method.</summary>
    public bool HasLedgerEntries { get; set; }
}

/// <summary>
/// A grouping of items, used for reporting and for defaulting the accounts they post to.
/// </summary>
/// <remarks>
/// Posting accounts live on the category rather than on each item, so a company with twelve
/// thousand items maintains six sets of accounts rather than twelve thousand. An item may still
/// override, but almost none do.
/// </remarks>
public sealed class ItemCategory : CompanyEntity
{
    /// <summary>Category code, for example <c>ELEC</c>.</summary>
    public required string Code { get; set; }

    /// <summary>Category name.</summary>
    public required string Name { get; set; }

    /// <summary>Category name in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>The category this one sits under, for a hierarchy.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>The balance sheet account stock in this category is held on.</summary>
    public string? InventoryAccountNo { get; set; }

    /// <summary>The account the cost of what is sold from this category is charged to.</summary>
    public string? CostOfGoodsSoldAccountNo { get; set; }

    /// <summary>The account revenue from this category is credited to.</summary>
    public string? SalesAccountNo { get; set; }

    /// <summary>
    /// Where the difference between estimated and settled cost lands when stock went negative and
    /// was later received.
    /// </summary>
    public string? VarianceAccountNo { get; set; }
}
