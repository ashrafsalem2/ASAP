using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Items;

/// <summary>A variant as somebody sets it up.</summary>
/// <param name="Code">Its code, unique within its item.</param>
/// <param name="Description">What this version is called.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="Barcode">Its own barcode, which is usually the important one.</param>
/// <param name="SortOrder">Where it sits in a list, so sizes read in size order.</param>
/// <param name="IsBlocked">Whether it is withdrawn from use.</param>
public readonly record struct ItemVariantRequest(
    string Code,
    string Description,
    string? DescriptionArabic = null,
    string? Barcode = null,
    int SortOrder = 0,
    bool IsBlocked = false);

/// <summary>What one variant is holding at one location.</summary>
/// <param name="VariantCode">The variant.</param>
/// <param name="Description">What it is called.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="LocationCode">Where.</param>
/// <param name="Quantity">How much of it is there.</param>
public readonly record struct VariantStockRow(
    string VariantCode,
    string Description,
    string? DescriptionArabic,
    string LocationCode,
    decimal Quantity);

/// <summary>
/// The colours, sizes and flavours an item is stocked as.
/// </summary>
/// <remarks>
/// Turning variants on for an item is safe: nothing is recorded under a variant yet, so nothing
/// changes. Turning them off is not, which is why it is refused while stock still stands under
/// them -- every one of those entries would point at a variant nothing reads, and the item's cost
/// layers would silently merge colours that were never interchangeable.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
public sealed class ItemVariantService(AsapDbContext context, IMessageCatalog messages)
{
    /// <summary>
    /// The variants of one item, in the order somebody would read them.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Its variants, or an empty list when it has none.</returns>
    public async Task<IReadOnlyList<ItemVariant>> VariantsAsync(
        string itemNo,
        CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(itemNo, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return [];
        }

        return await context.Set<ItemVariant>()
            .AsNoTracking()
            .Where(v => v.ItemId == item.Id)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Turns variants on or off for an item.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="hasVariants">Whether it is stocked as separate versions.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The item as saved, or why it was refused.</returns>
    /// <remarks>
    /// Off is the dangerous direction. Stock recorded under a variant keeps its variant on the
    /// entry whatever the flag says, so switching off with stock outstanding leaves entries
    /// pointing at something nothing reads and merges cost layers that were never the same goods.
    /// </remarks>
    public async Task<Result<Item>> SetHasVariantsAsync(
        string itemNo,
        bool hasVariants,
        CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(itemNo, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return Result<Item>.Failure(messages.Render(
                InventoryMessages.ItemNotFound,
                Args(("ItemNo", itemNo?.Trim().ToUpperInvariant() ?? string.Empty))));
        }

        if (!hasVariants && item.HasVariants)
        {
            var held = await context.Set<ItemLedgerEntry>()
                .Where(e => e.ItemId == item.Id && e.VariantId != null)
                .GroupBy(static e => e.VariantId)
                .Select(static g => new { VariantId = g.Key, Quantity = g.Sum(static e => e.Quantity) })
                .Where(static g => g.Quantity != 0m)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (held.Count > 0)
            {
                return Result<Item>.Failure(messages.Render(
                    InventoryMessages.VariantsStillHoldStock,
                    Args(
                        ("ItemNo", item.No),
                        ("VariantCount", held.Count),
                        ("Quantity", held.Sum(static h => h.Quantity)))));
            }
        }

        item.HasVariants = hasVariants;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<Item>.Success(item);
    }

    /// <summary>
    /// Adds a variant to an item, or changes one already there.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="request">The variant.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The variant as saved, or why it was refused.</returns>
    public async Task<Result<ItemVariant>> SaveAsync(
        string itemNo,
        ItemVariantRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(itemNo, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return Result<ItemVariant>.Failure(messages.Render(
                InventoryMessages.ItemNotFound,
                Args(("ItemNo", itemNo?.Trim().ToUpperInvariant() ?? string.Empty))));
        }

        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length == 0)
        {
            return Result<ItemVariant>.Failure(
                messages.Render(InventoryMessages.VariantCodeRequired, Args()));
        }

        var barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();

        if (barcode is not null)
        {
            var clash = await ClashAsync(item.Id, code, barcode, cancellationToken).ConfigureAwait(false);

            if (clash is not null)
            {
                return Result<ItemVariant>.Failure(clash);
            }
        }

        var existing = await context.Set<ItemVariant>()
            .FirstOrDefaultAsync(v => v.ItemId == item.Id && v.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new ItemVariant
            {
                TenantId = item.TenantId,
                CompanyId = item.CompanyId,
                ItemId = item.Id,
                Code = code,
                Description = request.Description?.Trim() ?? code,
                DescriptionArabic = request.DescriptionArabic?.Trim(),
                Barcode = barcode,
                SortOrder = request.SortOrder,
                IsBlocked = request.IsBlocked,
            };

            context.Set<ItemVariant>().Add(existing);
        }
        else
        {
            existing.Description = request.Description?.Trim() ?? existing.Description;
            existing.DescriptionArabic = request.DescriptionArabic?.Trim();
            existing.Barcode = barcode;
            existing.SortOrder = request.SortOrder;
            existing.IsBlocked = request.IsBlocked;
        }

        // Adding a variant to an item that had none is the moment it starts having them. Making
        // somebody set a flag as well would be a second step whose only job is to agree with the
        // first, and the failure mode is a variant nothing can be posted against.
        item.HasVariants = true;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ItemVariant>.Success(existing);
    }

    /// <summary>
    /// What each variant of an item is holding, by location.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A row per variant and location that holds something.</returns>
    /// <remarks>
    /// The question a shop actually asks: not "how many shirts" but "have we got that one in
    /// medium". A total across variants answers the first and is useless for the second, which is
    /// the entire reason variants exist.
    /// </remarks>
    public async Task<IReadOnlyList<VariantStockRow>> StockAsync(
        string itemNo,
        CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(itemNo, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return [];
        }

        var totals = await context.Set<ItemLedgerEntry>()
            .Where(e => e.ItemId == item.Id && e.VariantId != null)
            .GroupBy(static e => new { e.VariantId, e.LocationCode })
            .Select(static g => new
            {
                g.Key.VariantId,
                g.Key.LocationCode,
                Quantity = g.Sum(static e => e.Quantity),
            })
            .Where(static g => g.Quantity != 0m)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (totals.Count == 0)
        {
            return [];
        }

        var variants = await context.Set<ItemVariant>()
            .AsNoTracking()
            .Where(v => v.ItemId == item.Id)
            .ToDictionaryAsync(static v => v.Id, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. totals
                .Where(t => variants.ContainsKey(t.VariantId!.Value))
                .Select(t =>
                {
                    var variant = variants[t.VariantId!.Value];

                    return (variant.SortOrder, Row: new VariantStockRow(
                        variant.Code,
                        variant.Description,
                        variant.DescriptionArabic,
                        t.LocationCode,
                        t.Quantity));
                })
                .OrderBy(static x => x.SortOrder)
                .ThenBy(static x => x.Row.VariantCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static x => x.Row.LocationCode, StringComparer.OrdinalIgnoreCase)
                .Select(static x => x.Row),
        ];
    }

    /// <summary>Says who already carries that barcode, when somebody does.</summary>
    /// <remarks>
    /// Three tables now: items, their units, and their variants. A scanner does not know which one
    /// a code came from, and two rows sharing one barcode makes a scan return whichever the
    /// database reached first.
    /// </remarks>
    private async Task<AsapMessage?> ClashAsync(
        Guid itemId,
        string code,
        string barcode,
        CancellationToken cancellationToken)
    {
        var onItem = await context.Set<Item>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Barcode == barcode, cancellationToken)
            .ConfigureAwait(false);

        if (onItem is not null)
        {
            return messages.Render(
                InventoryMessages.BarcodeAlreadyInUse,
                Args(("Barcode", barcode), ("ItemNo", onItem.No), ("UnitCode", onItem.BaseUnitOfMeasure)));
        }

        var onUnit = await context.Set<ItemUnit>()
            .AsNoTracking()
            .Include(u => u.Item)
            .FirstOrDefaultAsync(u => u.Barcode == barcode, cancellationToken)
            .ConfigureAwait(false);

        if (onUnit is not null)
        {
            return messages.Render(
                InventoryMessages.BarcodeAlreadyInUse,
                Args(
                    ("Barcode", barcode),
                    ("ItemNo", onUnit.Item?.No ?? string.Empty),
                    ("UnitCode", onUnit.UnitCode)));
        }

        var onVariant = await context.Set<ItemVariant>()
            .AsNoTracking()
            .Include(v => v.Item)
            .FirstOrDefaultAsync(
                v => v.Barcode == barcode && (v.ItemId != itemId || v.Code != code),
                cancellationToken)
            .ConfigureAwait(false);

        return onVariant is null
            ? null
            : messages.Render(
                InventoryMessages.BarcodeAlreadyInUse,
                Args(
                    ("Barcode", barcode),
                    ("ItemNo", onVariant.Item?.No ?? string.Empty),
                    ("UnitCode", onVariant.Code)));
    }

    private Task<Item?> FindAsync(string itemNo, CancellationToken cancellationToken)
    {
        var normalised = itemNo?.Trim().ToUpperInvariant() ?? string.Empty;

        return context.Set<Item>().FirstOrDefaultAsync(i => i.No == normalised, cancellationToken);
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
