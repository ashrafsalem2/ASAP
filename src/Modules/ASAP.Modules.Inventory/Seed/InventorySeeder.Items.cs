using ASAP.Modules.Inventory.Items;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// Adds the units a company counts, weighs and measures in, and a worked example of a case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The units themselves are shipped because <c>PCS</c>, <c>KG</c> and <c>BOX</c> mean the same
    /// thing everywhere and a company should not have to invent them. What a box holds is not
    /// shipped for the same reason a rate is not: it is a fact about a particular item, and one
    /// company's box of desk lamps is not another's.
    /// </para>
    /// <para>
    /// One exception, and it is deliberate: the demonstration item gets a case of twelve with its
    /// own barcode, so that scanning the case adds twelve rather than one and the behaviour can be
    /// seen rather than described.
    /// </para>
    /// </remarks>
    private async Task SeedUnitsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (!await context.Set<Items.UnitOfMeasure>().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            Add("PCS", "Pieces", "قطعة", 0);
            Add("BOX", "Box", "علبة", 0);
            Add("CASE", "Case", "كرتون", 0);
            Add("PALLET", "Pallet", "منصة", 0);

            // Three places for the things that are weighed, none for the things that are counted.
            // A till that accepts two and a half of something sold one at a time has taken an
            // order nobody can pick.
            Add("KG", "Kilogram", "كيلوغرام", 3);
            Add("L", "Litre", "لتر", 3);
            Add("M", "Metre", "متر", 2);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var lamp = await context.Set<Items.Item>()
            .FirstOrDefaultAsync(i => i.No == "ITEM-1001", cancellationToken)
            .ConfigureAwait(false);

        if (lamp is null
            || await context.Set<Items.ItemUnit>().AnyAsync(u => u.ItemId == lamp.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        context.Set<Items.ItemUnit>().Add(new Items.ItemUnit
        {
            TenantId = tenantId,
            CompanyId = companyId,
            ItemId = lamp.Id,
            UnitCode = "CASE",
            QuantityPerUnit = 12m,

            // Its own barcode, which is the whole point: scanning this adds twelve.
            Barcode = "6281001000012",
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        void Add(string code, string name, string arabic, int places)
            => context.Set<Items.UnitOfMeasure>().Add(new Items.UnitOfMeasure
            {
                TenantId = tenantId,
                CompanyId = companyId,
                Code = code,
                Name = name,
                NameArabic = arabic,
                DecimalPlaces = places,
            });
    }
}
