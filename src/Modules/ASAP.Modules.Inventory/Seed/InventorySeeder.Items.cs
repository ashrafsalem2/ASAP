using ASAP.Modules.Inventory.Items;

namespace ASAP.Modules.Inventory.Seed;

/// <summary>A handful of items so a new company has something to move.</summary>
public sealed partial class InventorySeeder
{
    /// <summary>
    /// Creates demo items covering the three costing methods.
    /// </summary>
    /// <remarks>
    /// Three methods rather than one, because the difference between them is the sort of thing
    /// that is easier to see than to read about. The FIFO item is the ordinary case; the average
    /// item is what a shop selling loose goods bought at drifting prices wants; the standard item
    /// shows a cost that does not move whatever is paid for it.
    /// </remarks>
    private void SeedItems(Guid tenantId, Guid companyId, ItemCategory category)
    {
        void Add(
            string no,
            string description,
            string arabic,
            CostingMethod method,
            decimal cost,
            decimal price,
            bool? allowNegative = null)
            => context.Set<Item>().Add(new Item
            {
                TenantId = tenantId,
                CompanyId = companyId,
                No = no,
                Description = description,
                DescriptionArabic = arabic,
                CategoryId = category.Id,
                BaseUnitOfMeasure = "PCS",
                CostingMethod = method,
                UnitCost = cost,
                LastDirectCost = cost,
                StandardCost = method is CostingMethod.Standard ? cost : 0m,
                UnitPrice = price,
                ReorderPoint = 10,
                ReorderQuantity = 50,
                AllowNegativeInventory = allowNegative,
                Barcode = $"628{no.Replace("ITEM-", string.Empty, StringComparison.Ordinal)}00000",
            });

        Add("ITEM-1001", "Desk lamp", "مصباح مكتب", CostingMethod.Fifo, 12.00m, 24.00m);
        Add("ITEM-1002", "Office chair", "كرسي مكتب", CostingMethod.Fifo, 145.00m, 299.00m);
        Add("ITEM-1003", "Printer paper, box", "ورق طباعة، صندوق", CostingMethod.Average, 38.50m, 65.00m);
        Add("ITEM-1004", "USB cable", "كابل USB", CostingMethod.Standard, 4.25m, 12.00m);

        // Permitted to go below zero where the company is not, because a shop can see loose stock
        // on the shelf long before the paperwork catches up with it.
        Add("ITEM-1005", "Bottled water, case", "مياه معبأة، صندوق", CostingMethod.Fifo, 9.00m, 18.00m, allowNegative: true);
    }
}
