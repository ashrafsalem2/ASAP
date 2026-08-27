using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Pos.Stations;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Pos.Seed;

/// <summary>
/// Gives every shop a till to trade at.
/// </summary>
/// <remarks>
/// <para>
/// Derived from the branches that already exist rather than invented, exactly as the stock
/// locations are: a company with two shops gets two tills named after them, selling from the
/// stock those shops already hold. A till pointed at head office stock would be a shop selling
/// goods that are in a warehouse.
/// </para>
/// <para>
/// It also needs somebody to record walk-in sales against. Every entry on a tax return wants a
/// counterparty, and a queue of people paying cash will not be asked for their names, so the
/// cash-sales customer is what answers that. Without one the first receipt of the day is refused
/// for a reason nobody at a counter can act on.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="logger">Reports what was created.</param>
public sealed class PosSeeder(AsapDbContext context, ILogger<PosSeeder> logger)
{
    /// <summary>Seeds point of sale for one company, if it has no tills yet.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The company to set up.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>True when it created something, false when the company already had a till.</returns>
    public async Task<bool> SeedAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        // Before the till check, and on its own terms. A company upgraded to a version that
        // has print templates has tills already and no template, and would otherwise be a shop
        // whose receipts cannot print because the seeder decided it had nothing to do.
        await SeedReceiptTemplateAsync(tenantId, companyId, cancellationToken).ConfigureAwait(false);

        var alreadySet = await context.Set<PosStation>()
            .IgnoreQueryFilters()
            .AnyAsync(s => s.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadySet)
        {
            return false;
        }

        var branches = await context.Branches
            .IgnoreQueryFilters()
            .Where(b => b.CompanyId == companyId && !b.IsDeleted && b.Kind == BranchKind.Store)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (branches.Count == 0)
        {
            return false;
        }

        var sellable = await context.Set<Location>()
            .IgnoreQueryFilters()
            .Where(l => l.CompanyId == companyId && l.IsSellable && !l.IsBlocked)
            .Select(static l => new { l.Code, l.BranchId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sellable.Count == 0)
        {
            logger.LogInformation(
                "No sellable location for company {CompanyId}, so no till was created.",
                companyId);

            return false;
        }

        var cashCustomer = await CashCustomerAsync(tenantId, companyId, cancellationToken)
            .ConfigureAwait(false);

        var created = 0;

        foreach (var branch in branches)
        {
            // The shop's own stock where it has some, and any sellable location otherwise. A till
            // with nowhere to sell from is worse than one pointed somewhere approximate.
            var location = sellable.Find(l => l.BranchId == branch.Id) ?? sellable[0];

            context.Set<PosStation>().Add(new PosStation
            {
                TenantId = tenantId,
                CompanyId = companyId,
                Code = $"{branch.Code}-T1",
                Name = $"{branch.Name} till 1",
                NameArabic = $"نقطة بيع 1 - {branch.NameArabic ?? branch.Name}",
                BranchId = branch.Id,
                LocationCode = location.Code,
                DefaultCustomerNo = cashCustomer,
            });

            created++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Created {Count} till(s) for company {CompanyId}.", created, companyId);

        return created > 0;
    }

    /// <summary>
    /// The receipt layout a company starts with.
    /// </summary>
    /// <remarks>
    /// Shipped so a till can print on its first day, and editable so it does not have to stay
    /// this. Forty-two characters is the usual eighty-millimetre roll.
    /// </remarks>
    private async Task SeedReceiptTemplateAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var already = await context.Set<Printing.PrintTemplate>()
            .IgnoreQueryFilters()
            .AnyAsync(
                t => t.CompanyId == companyId && t.Kind == Printing.PrintTemplateKind.Receipt,
                cancellationToken)
            .ConfigureAwait(false);

        if (already)
        {
            return;
        }

        const string content = """
            {StationName,-42}
            ------------------------------------------
            Receipt {ReceiptNo}
            {TakenAtUtc}

            [[lines]]{Description,-42}
              {Quantity,-6:0.##} x {UnitPrice,-9:N2}{LineAmount,22:N2}
            [[/lines]]------------------------------------------
            Net{NetAmount,39:N2}
            Discount{DiscountAmount,34:N2}
            Tax{TaxAmount,39:N2}
            Rounding{RoundingAmount,34:N2}
            TOTAL{TotalAmount,37:N2}

            [[tenders]]{Kind,-22}{Amount,20:N2}
            [[/tenders]]Change{ChangeGiven,36:N2}

            ------------------------------------------
                     Thank you for shopping
                      Keep your receipt

            """;

        context.Set<Printing.PrintTemplate>().Add(new Printing.PrintTemplate
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Code = "RECEIPT",
            Name = "Till receipt",
            NameArabic = "إيصال الصندوق",
            Kind = Printing.PrintTemplateKind.Receipt,
            WidthInCharacters = 42,
            IsDefault = true,
            Content = content,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Seeded the receipt template for company {CompanyId}.", companyId);
    }

    /// <summary>
    /// Finds or creates the customer walk-in sales are recorded against.
    /// </summary>
    /// <remarks>
    /// Found first, because the Finance demo data already ships one and a second would split the
    /// shop's takings across two accounts that mean the same thing.
    /// </remarks>
    private async Task<string> CashCustomerAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var existing = await context.Set<Customer>()
            .IgnoreQueryFilters()
            .Where(c => c.CompanyId == companyId && c.Name.Contains("Cash"))
            .Select(static c => c.No)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var customer = new Customer
        {
            TenantId = tenantId,
            CompanyId = companyId,
            No = "C-CASH",
            Name = "Cash sales",
            NameArabic = "مبيعات نقدية",

            // Nothing is owed, so nothing is extended. A walk-in account with a credit limit
            // would be a limit shared by every person who ever paid cash.
            CreditLimit = 0m,
        };

        context.Set<Customer>().Add(customer);

        return customer.No;
    }
}
