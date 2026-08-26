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
}
