using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Locations;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Inventory.Seed;

/// <summary>
/// Gives a new company somewhere to put stock and a few items to put there.
/// </summary>
/// <remarks>
/// Locations are derived from the branches that already exist rather than invented, so a company
/// with three branches gets three shop-floor locations named after them plus one in-transit
/// location for goods between them. Without the in-transit location a transfer would make stock
/// disappear from the valuation for the length of the journey.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="logger">Reports what was created.</param>
public sealed partial class InventorySeeder(AsapDbContext context, ILogger<InventorySeeder> logger)
{
    /// <summary>Seeds Inventory for one company, if it has nothing yet.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The company to set up.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>True when it seeded, false when the company already had locations.</returns>
    public async Task<bool> SeedAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var alreadySet = await context.Set<Location>()
            .IgnoreQueryFilters()
            .AnyAsync(l => l.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        // Ahead of the early return, and checking for itself. A company that already has
        // locations is exactly the one that would otherwise never receive units.
        await SeedUnitsAsync(tenantId, companyId, cancellationToken).ConfigureAwait(false);
        await SeedAdjustmentReasonsAsync(tenantId, companyId, cancellationToken).ConfigureAwait(false);

        if (alreadySet)
        {
            return false;
        }

        var branches = await context.Branches
            .IgnoreQueryFilters()
            .Where(b => b.CompanyId == companyId && !b.IsDeleted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var branch in branches.Where(static b => b.Kind is not BranchKind.Office))
        {
            context.Set<Location>().Add(new Location
            {
                TenantId = tenantId,
                CompanyId = companyId,
                Code = branch.Code,
                Name = $"{branch.Name} stock",
                NameArabic = branch.NameArabic is { } arabic ? $"مخزون {arabic}" : null,
                BranchId = branch.Id,
                IsSellable = branch.Kind is BranchKind.Store,
            });
        }

        // Goods leave one location and do not arrive at the next until they land, which can be
        // days. Somewhere has to hold them meanwhile or the inventory account disagrees with the
        // balance sheet for the length of every journey.
        context.Set<Location>().Add(new Location
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Code = "TRANSIT",
            Name = "In transit",
            NameArabic = "في الطريق",
            IsSellable = false,
            IsInTransit = true,
        });

        var category = new ItemCategory
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Code = "GENERAL",
            Name = "General goods",
            NameArabic = "بضائع عامة",
            InventoryAccountNo = "1400",
            CostOfGoodsSoldAccountNo = "5100",
            SalesAccountNo = "4100",
            VarianceAccountNo = "5300",
        };

        context.Set<ItemCategory>().Add(category);

        SeedItems(tenantId, companyId, category);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded {LocationCount} inventory location(s) for company {Company}.",
            branches.Count + 1,
            companyId);

        return true;
    }

    /// <summary>
    /// Adds the reasons a company writes stock off for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shipped, unlike a box's contents, because breakage and theft and expiry mean the same thing
    /// in every shop and a company should not have to invent the words. The accounts they point at
    /// are shipped for the same reason: a write-off that lands nowhere in particular is what this
    /// whole list exists to stop.
    /// </para>
    /// <para>
    /// Directions are set the way the world works. Breakage cannot increase stock and goods found
    /// cannot decrease it. A count difference is the only one that genuinely goes either way,
    /// which is why it is the only one left open.
    /// </para>
    /// </remarks>
    private async Task SeedAdjustmentReasonsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (await context.Set<Adjustments.AdjustmentReason>().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        Add("COUNT", "Stock count difference", "فرق جرد", "5200", Adjustments.AdjustmentDirection.Either);
        Add("BREAKAGE", "Broken or damaged", "تالف أو مكسور", "5210", Adjustments.AdjustmentDirection.DecreaseOnly);

        // A note is demanded on this one and no other. Everything else here is an accident; this
        // is the one somebody will be asked about, and a row nobody wrote anything against is a
        // row that has to be reconstructed from memory months afterwards.
        Add("THEFT", "Missing or stolen", "مفقود أو مسروق", "5220", Adjustments.AdjustmentDirection.DecreaseOnly, requiresNote: true);

        Add("EXPIRY", "Past its date", "منتهي الصلاحية", "5230", Adjustments.AdjustmentDirection.DecreaseOnly);
        Add("SAMPLE", "Given away as a sample", "عينة مجانية", "5240", Adjustments.AdjustmentDirection.DecreaseOnly);
        Add("FOUND", "Found on the shelf", "بضاعة موجودة", "5200", Adjustments.AdjustmentDirection.IncreaseOnly);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        void Add(
            string code,
            string name,
            string arabic,
            string account,
            Adjustments.AdjustmentDirection direction,
            bool requiresNote = false)
            => context.Set<Adjustments.AdjustmentReason>().Add(new Adjustments.AdjustmentReason
            {
                TenantId = tenantId,
                CompanyId = companyId,
                Code = code,
                Name = name,
                NameArabic = arabic,
                ContraAccountNo = account,
                Direction = direction,
                RequiresNote = requiresNote,
            });
    }
}
