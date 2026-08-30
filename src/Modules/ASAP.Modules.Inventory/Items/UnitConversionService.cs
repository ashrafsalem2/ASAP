using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Items;

/// <summary>What a scan or a keyed quantity came to.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is called, so a till can show it without asking again.</param>
/// <param name="UnitCode">The unit that was scanned or named.</param>
/// <param name="Quantity">How many of that unit.</param>
/// <param name="BaseQuantity">The same amount in the unit everything is stored in.</param>
/// <param name="BaseUnitCode">What that unit is.</param>
public readonly record struct ResolvedQuantity(
    string ItemNo,
    string Description,
    string UnitCode,
    decimal Quantity,
    decimal BaseQuantity,
    string BaseUnitCode);

/// <summary>
/// Turns what somebody scanned or typed into a quantity in the unit stock is kept in.
/// </summary>
/// <remarks>
/// <para>
/// The one place units are allowed to matter. Everything below this — the item ledger, the
/// costing engine, every stock figure and every report — works in the base unit and nothing else,
/// because a stock figure with mixed units in it cannot be added up.
/// </para>
/// <para>
/// Which means the conversion happens once, at the edge, where somebody is standing at a till or
/// keying a purchase order. One multiplication there buys the ability to answer "how much is
/// there" everywhere else.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
public sealed class UnitConversionService(AsapDbContext context, IMessageCatalog messages)
{
    /// <summary>
    /// Finds what a barcode is, and how many it stands for.
    /// </summary>
    /// <param name="barcode">What the scanner sent.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The item and the quantity, or why the barcode meant nothing.</returns>
    /// <remarks>
    /// A unit's own barcode is looked for before the item's. That is what makes scanning a case
    /// add twelve rather than one — and getting the order the other way round would make every
    /// case barcode fall back to a single, which nobody would notice until a stock count.
    /// </remarks>
    public async Task<Result<ResolvedQuantity>> ScanAsync(
        string barcode,
        CancellationToken cancellationToken = default)
    {
        var scanned = barcode?.Trim() ?? string.Empty;

        if (scanned.Length == 0)
        {
            return Result<ResolvedQuantity>.Failure(
                messages.Render(InventoryMessages.BarcodeNotFound, Args(("Barcode", scanned))));
        }

        var unit = await context.Set<ItemUnit>()
            .AsNoTracking()
            .Include(u => u.Item)
            .FirstOrDefaultAsync(u => u.Barcode == scanned && u.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (unit?.Item is { } itemOfUnit)
        {
            return Result<ResolvedQuantity>.Success(new ResolvedQuantity(
                itemOfUnit.No,
                itemOfUnit.Description,
                unit.UnitCode,
                1m,
                unit.ToBase(1m),
                itemOfUnit.BaseUnitOfMeasure));
        }

        var item = await context.Set<Item>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Barcode == scanned, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Result<ResolvedQuantity>.Failure(
                messages.Render(InventoryMessages.BarcodeNotFound, Args(("Barcode", scanned))));
        }

        return Result<ResolvedQuantity>.Success(new ResolvedQuantity(
            item.No,
            item.Description,
            item.BaseUnitOfMeasure,
            1m,
            1m,
            item.BaseUnitOfMeasure));
    }

    /// <summary>
    /// Turns a quantity keyed in some unit into the base unit.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="unitCode">The unit it was keyed in, or null for the item's base unit.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The quantity in both units, or why it could not be converted.</returns>
    public async Task<Result<ResolvedQuantity>> ConvertAsync(
        string itemNo,
        string? unitCode,
        decimal quantity,
        CancellationToken cancellationToken = default)
    {
        var normalisedItem = itemNo.Trim().ToUpperInvariant();

        var item = await context.Set<Item>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.No == normalisedItem, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Result<ResolvedQuantity>.Failure(
                messages.Render(InventoryMessages.ItemNotFoundForUnit, Args(("ItemNo", normalisedItem))));
        }

        var wanted = unitCode?.Trim().ToUpperInvariant();

        // No unit named, or the base one named explicitly: nothing to convert, and no need to
        // have set anything up. An item sold only in its base unit should need no configuration.
        if (string.IsNullOrEmpty(wanted)
            || string.Equals(wanted, item.BaseUnitOfMeasure, StringComparison.OrdinalIgnoreCase))
        {
            return Result<ResolvedQuantity>.Success(new ResolvedQuantity(
                item.No, item.Description, item.BaseUnitOfMeasure, quantity, quantity, item.BaseUnitOfMeasure));
        }

        var unit = await context.Set<ItemUnit>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.ItemId == item.Id && u.UnitCode == wanted && u.IsActive,
                cancellationToken)
            .ConfigureAwait(false);

        if (unit is null)
        {
            // Named per item rather than globally, so the refusal is per item too. A box is set
            // up for the things that come in boxes, and saying so is more useful than a general
            // complaint about the word.
            return Result<ResolvedQuantity>.Failure(messages.Render(
                InventoryMessages.UnitNotSetUpForItem,
                Args(("ItemNo", item.No), ("UnitCode", wanted), ("BaseUnit", item.BaseUnitOfMeasure))));
        }

        if (unit.QuantityPerUnit <= 0m)
        {
            // A factor of nought turns every quantity in that unit into nought, which reads as a
            // clean zero rather than as an error and is the worst way for this to fail.
            return Result<ResolvedQuantity>.Failure(messages.Render(
                InventoryMessages.UnitFactorNotUsable,
                Args(("ItemNo", item.No), ("UnitCode", wanted))));
        }

        return Result<ResolvedQuantity>.Success(new ResolvedQuantity(
            item.No,
            item.Description,
            unit.UnitCode,
            quantity,
            unit.ToBase(quantity),
            item.BaseUnitOfMeasure));
    }

    /// <summary>
    /// The units an item may be handled in, base unit first.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The units, or an empty list when the item is not there.</returns>
    /// <remarks>
    /// The base unit is included whether or not somebody set it up as a row, because an item is
    /// always sellable in the unit it is counted in and a list that omitted it would be a list
    /// that refused the commonest case.
    /// </remarks>
    public async Task<IReadOnlyList<ItemUnit>> UnitsAsync(
        string itemNo,
        CancellationToken cancellationToken = default)
    {
        var normalised = itemNo.Trim().ToUpperInvariant();

        var item = await context.Set<Item>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.No == normalised, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return [];
        }

        var units = await context.Set<ItemUnit>()
            .AsNoTracking()
            .Where(u => u.ItemId == item.Id && u.IsActive)
            .OrderBy(u => u.QuantityPerUnit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!units.Exists(u => string.Equals(u.UnitCode, item.BaseUnitOfMeasure, StringComparison.OrdinalIgnoreCase)))
        {
            units.Insert(0, new ItemUnit
            {
                TenantId = item.TenantId,
                CompanyId = item.CompanyId,
                ItemId = item.Id,
                UnitCode = item.BaseUnitOfMeasure,
                QuantityPerUnit = 1m,
                Barcode = item.Barcode,
            });
        }

        return units;
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
