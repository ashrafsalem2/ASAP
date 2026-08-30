using ASAP.Modules.Inventory.Items;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Sales.Pricing;

/// <summary>What a customer pays for something, and where the figure came from.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="UnitPrice">What one costs them, before the line discount.</param>
/// <param name="DiscountPercent">A discount off that price.</param>
/// <param name="PriceListCode">
/// The list it came from, or empty where nothing matched and the item's own price stands.
/// </param>
/// <param name="MinimumQuantity">The volume break this price required, where it required one.</param>
public readonly record struct ResolvedPrice(
    string ItemNo,
    decimal UnitPrice,
    decimal DiscountPercent,
    string PriceListCode,
    decimal MinimumQuantity);

/// <summary>A price list as somebody asks for it to be saved.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Lines">The prices on it, replacing whatever was there.</param>
/// <param name="ValidFrom">The first day it applies.</param>
/// <param name="ValidTo">The last day it applies.</param>
/// <param name="IsActive">Whether it may be used at all.</param>
public sealed record PriceListRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    IReadOnlyList<PriceListLineRequest>? Lines = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    bool IsActive = true);

/// <summary>One agreed price, as somebody asks for it to be saved.</summary>
/// <param name="ItemNo">What it is for.</param>
/// <param name="UnitPrice">What one costs.</param>
/// <param name="DiscountPercent">A discount off that.</param>
/// <param name="MinimumQuantity">The least that has to be bought for it. Nought means any.</param>
/// <param name="VariantCode">One variant, or null for all of them.</param>
/// <param name="UnitCode">One unit, or null for any.</param>
/// <param name="ValidFrom">The first day this line applies.</param>
/// <param name="ValidTo">The last day this line applies.</param>
public sealed record PriceListLineRequest(
    string ItemNo,
    decimal UnitPrice,
    decimal DiscountPercent = 0m,
    decimal MinimumQuantity = 0m,
    string? VariantCode = null,
    string? UnitCode = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null);

/// <summary>
/// Works out what a particular customer pays for a particular thing on a particular day.
/// </summary>
/// <remarks>
/// <para>
/// Four rules, and the last one is the only interesting one.
/// </para>
/// <para>
/// A customer with no list pays what is on the item. A list only applies on days it is in force,
/// and so does each line on it -- a price agreed for a quarter stops at the end of it without
/// anybody remembering, because the arrangement nobody remembers is the one still being honoured
/// two years later.
/// </para>
/// <para>
/// The most specific line that fits wins. A price for a colour beats one for the item; a price from
/// a hundred up beats one for any quantity, as long as a hundred are actually being bought. That is
/// what lets a general trade price and a volume break sit in the same list without either knowing
/// the other exists.
/// </para>
/// <para>
/// And two equally specific lines are refused rather than resolved. It is not a tie to break: it is
/// a contradiction somebody entered by accident, and picking one would make what a customer is
/// charged depend on which row the database happened to reach first. That is the kind of thing
/// nobody finds until an invoice is queried.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenancy">Says which company this is.</param>
public sealed class PricingService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenancy)
{
    /// <summary>
    /// What a customer pays for one item.
    /// </summary>
    /// <param name="customerNo">Who is buying.</param>
    /// <param name="itemNo">What they are buying.</param>
    /// <param name="quantity">How many, for a volume break.</param>
    /// <param name="on">The day the price applies.</param>
    /// <param name="variantCode">Which variant, where the item has them.</param>
    /// <param name="unitCode">Which unit it is being bought in.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The price, or why one could not be decided.</returns>
    public async Task<Result<ResolvedPrice>> PriceForAsync(
        string customerNo,
        string itemNo,
        decimal quantity,
        DateOnly on,
        string? variantCode = null,
        string? unitCode = null,
        CancellationToken cancellationToken = default)
    {
        var normalisedItem = itemNo?.Trim().ToUpperInvariant() ?? string.Empty;

        var item = await context.Set<Item>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.No == normalisedItem, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Result<ResolvedPrice>.Failure(messages.Render(
                Inventory.InventoryMessages.ItemNotFound,
                Args(("ItemNo", normalisedItem))));
        }

        var listCode = await ListForCustomerAsync(customerNo, cancellationToken).ConfigureAwait(false);

        if (listCode is null)
        {
            // No arrangement, so the counter price. That is the right answer for a walk-in rather
            // than a failure to find something.
            return Result<ResolvedPrice>.Success(
                new ResolvedPrice(item.No, item.UnitPrice, 0m, string.Empty, 0m));
        }

        var list = await context.Set<PriceList>()
            .AsNoTracking()
            .Include(l => l.Lines)
            .FirstOrDefaultAsync(l => l.Code == listCode, cancellationToken)
            .ConfigureAwait(false);

        if (list is null || !list.AppliesOn(on))
        {
            return Result<ResolvedPrice>.Success(
                new ResolvedPrice(item.No, item.UnitPrice, 0m, string.Empty, 0m));
        }

        var variant = variantCode?.Trim().ToUpperInvariant();
        var unit = unitCode?.Trim().ToUpperInvariant();

        var candidates = list.Lines
            .Where(l => string.Equals(l.ItemNo, item.No, StringComparison.OrdinalIgnoreCase))
            .Where(l => l.AppliesOn(on))
            .Where(l => l.MinimumQuantity <= quantity)
            .Where(l => l.VariantCode is not { Length: > 0 } v
                || string.Equals(v, variant, StringComparison.OrdinalIgnoreCase))
            .Where(l => l.UnitCode is not { Length: > 0 } u
                || string.Equals(u, unit, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            return Result<ResolvedPrice>.Success(
                new ResolvedPrice(item.No, item.UnitPrice, 0m, string.Empty, 0m));
        }

        var best = candidates.Max(static l => l.Specificity);
        var winners = candidates.Where(l => l.Specificity == best).ToList();

        if (winners.Count > 1)
        {
            // Not a tie to break. Two lines say different things about the same sale, and choosing
            // between them would make the price depend on row order.
            return Result<ResolvedPrice>.Failure(messages.Render(
                SalesMessages.PriceIsAmbiguous,
                Args(
                    ("ItemNo", item.No),
                    ("PriceListCode", list.Code),
                    ("Count", winners.Count),
                    ("Prices", string.Join(", ", winners.Select(static w => w.UnitPrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)))))));
        }

        var line = winners[0];

        return Result<ResolvedPrice>.Success(new ResolvedPrice(
            item.No,
            line.UnitPrice,
            line.DiscountPercent,
            list.Code,
            line.MinimumQuantity));
    }

    /// <summary>
    /// Puts a customer on a price list, or takes them off one.
    /// </summary>
    /// <param name="customerNo">The customer.</param>
    /// <param name="priceListCode">The list, or null to take them off.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether it was saved, or why not.</returns>
    public async Task<Result> AssignAsync(
        string customerNo,
        string? priceListCode,
        CancellationToken cancellationToken = default)
    {
        var customer = customerNo?.Trim().ToUpperInvariant() ?? string.Empty;

        if (customer.Length == 0)
        {
            return Result.Failure(messages.Render(SalesMessages.PriceListNeedsACustomer, Args()));
        }

        var existing = await context.Set<CustomerPriceList>()
            .FirstOrDefaultAsync(c => c.CustomerNo == customer, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(priceListCode))
        {
            if (existing is not null)
            {
                context.Set<CustomerPriceList>().Remove(existing);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return Result.Success();
        }

        var code = priceListCode.Trim().ToUpperInvariant();

        var list = await context.Set<PriceList>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure(messages.Render(
                SalesMessages.PriceListNotFound,
                Args(("PriceListCode", code))));
        }

        if (existing is null)
        {
            context.Set<CustomerPriceList>().Add(new CustomerPriceList
            {
                TenantId = tenancy.RequireTenantId(),
                CompanyId = tenancy.RequireCompanyId(),
                CustomerNo = customer,
                PriceListCode = code,
            });
        }
        else
        {
            existing.PriceListCode = code;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Writes a price list and everything on it.
    /// </summary>
    /// <remarks>
    /// The lines given replace the lines held. A price list is edited as a whole sheet rather than
    /// row by row, because that is how somebody negotiating a contract thinks about it, and because
    /// a half-applied sheet is a set of prices nobody agreed to.
    /// </remarks>
    /// <param name="request">The list and its prices.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The saved list, or why it could not be saved.</returns>
    public async Task<Result<PriceList>> SaveAsync(
        PriceListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length == 0)
        {
            return Result<PriceList>.Failure(messages.Render(
                SalesMessages.PriceListNotFound,
                Args(("PriceListCode", code))));
        }

        var list = await context.Set<PriceList>()
            .Include(l => l.Lines)
            .FirstOrDefaultAsync(l => l.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            list = new PriceList
            {
                TenantId = tenancy.RequireTenantId(),
                CompanyId = tenancy.RequireCompanyId(),
                Code = code,
                Name = request.Name,
            };

            context.Set<PriceList>().Add(list);
        }

        list.Name = request.Name;
        list.NameArabic = request.NameArabic;
        list.ValidFrom = request.ValidFrom;
        list.ValidTo = request.ValidTo;
        list.IsActive = request.IsActive;

        // Old lines are marked away through the set and new ones are added through the set, and
        // neither goes near the parent's collection. Emptying that collection makes EF treat every
        // line as an orphan of a required parent, and it then has two opinions about the same row
        // in one save -- a soft delete from us and a cascade from itself.
        context.Set<PriceListLine>().RemoveRange(list.Lines);

        foreach (var line in request.Lines ?? [])
        {
            context.Set<PriceListLine>().Add(new PriceListLine
            {
                TenantId = list.TenantId,
                CompanyId = list.CompanyId,
                PriceListId = list.Id,
                ItemNo = line.ItemNo.Trim().ToUpperInvariant(),
                VariantCode = Normalise(line.VariantCode),
                UnitCode = Normalise(line.UnitCode),
                MinimumQuantity = line.MinimumQuantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                ValidFrom = line.ValidFrom,
                ValidTo = line.ValidTo,
            });
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Read back rather than returned from memory: what is tracked still holds the lines that
        // were just marked away, and the caller is owed the sheet as it now stands.
        return Result<PriceList>.Success(
            await FindAsync(code, cancellationToken).ConfigureAwait(false) ?? list);
    }

    /// <summary>One list, with its lines.</summary>
    /// <param name="code">Its code.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The list, or null when nothing carries that code.</returns>
    public async Task<PriceList?> FindAsync(string code, CancellationToken cancellationToken = default)
    {
        var wanted = code?.Trim().ToUpperInvariant() ?? string.Empty;

        return await context.Set<PriceList>()
            .AsNoTracking()
            .Include(l => l.Lines)
            .FirstOrDefaultAsync(l => l.Code == wanted, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? Normalise(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    /// <summary>The lists, with their lines.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Every price list.</returns>
    public async Task<IReadOnlyList<PriceList>> ListsAsync(CancellationToken cancellationToken = default)
        => await context.Set<PriceList>()
            .AsNoTracking()
            .Include(l => l.Lines)
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Who is on which list.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Every assignment.</returns>
    public async Task<IReadOnlyList<CustomerPriceList>> AssignmentsAsync(
        CancellationToken cancellationToken = default)
        => await context.Set<CustomerPriceList>()
            .AsNoTracking()
            .OrderBy(c => c.CustomerNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<string?> ListForCustomerAsync(string customerNo, CancellationToken cancellationToken)
    {
        var customer = customerNo?.Trim().ToUpperInvariant() ?? string.Empty;

        if (customer.Length == 0)
        {
            return null;
        }

        return await context.Set<CustomerPriceList>()
            .AsNoTracking()
            .Where(c => c.CustomerNo == customer)
            .Select(static c => c.PriceListCode)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}
