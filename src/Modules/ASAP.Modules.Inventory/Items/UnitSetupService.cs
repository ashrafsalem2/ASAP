using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Items;

/// <summary>A unit as somebody sets it up.</summary>
/// <param name="Code">What it is called on a document.</param>
/// <param name="Name">Its name in English.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="DecimalPlaces">How many decimal places a quantity in it may carry.</param>
/// <param name="IsActive">Whether it may still be chosen.</param>
public readonly record struct UnitRequest(
    string Code,
    string Name,
    string? NameArabic,
    int DecimalPlaces,
    bool IsActive = true);

/// <summary>What one of a unit holds, for one item.</summary>
/// <param name="UnitCode">The unit.</param>
/// <param name="QuantityPerUnit">How many base units are in one of it.</param>
/// <param name="Barcode">Its own barcode, when it has one.</param>
/// <param name="IsActive">Whether it may still be chosen.</param>
public readonly record struct ItemUnitRequest(
    string UnitCode,
    decimal QuantityPerUnit,
    string? Barcode = null,
    bool IsActive = true);

/// <summary>
/// Lets a company say what it measures in, and what one item's box holds.
/// </summary>
/// <remarks>
/// <para>
/// Two halves of one setup, kept apart because they belong to different people. The unit list is
/// company-wide and rarely touched: <c>PCS</c> and <c>KG</c> mean the same thing everywhere.
/// What a box holds is per item and changes whenever a supplier changes their packing, so it sits
/// on the item and is maintained by whoever maintains items.
/// </para>
/// <para>
/// Every refusal here exists because the alternative is silent. A duplicate barcode does not fail:
/// it makes a scan return whichever row the database happened to reach first, and a shop finds out
/// at a stock count. A factor of nought does not fail either: it reads as a clean zero on every
/// report.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenancy">Says which company this is.</param>
public sealed class UnitSetupService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenancy)
{
    /// <summary>The most decimal places any unit may carry.</summary>
    /// <remarks>
    /// Five, which is what a quantity column holds. Allowing more would let a setup screen promise
    /// a precision the database rounds away, and a quantity that changes when it is saved is worse
    /// than one that was refused.
    /// </remarks>
    public const int MaximumDecimalPlaces = 5;

    /// <summary>
    /// Adds a unit to the company's list, or changes one already on it.
    /// </summary>
    /// <param name="request">The unit.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The unit as saved, or why it was refused.</returns>
    public async Task<Result<UnitOfMeasure>> SaveUnitAsync(
        UnitRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length == 0)
        {
            return Result<UnitOfMeasure>.Failure(messages.Render(
                InventoryMessages.UnitCodeRequired,
                Args()));
        }

        if (request.DecimalPlaces < 0 || request.DecimalPlaces > MaximumDecimalPlaces)
        {
            return Result<UnitOfMeasure>.Failure(messages.Render(
                InventoryMessages.DecimalPlacesOutOfRange,
                Args(
                    ("UnitCode", code),
                    ("DecimalPlaces", request.DecimalPlaces),
                    ("Maximum", MaximumDecimalPlaces))));
        }

        var existing = await context.Set<UnitOfMeasure>()
            .FirstOrDefaultAsync(u => u.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new UnitOfMeasure
            {
                TenantId = tenancy.RequireTenantId(),
                CompanyId = tenancy.RequireCompanyId(),
                Code = code,
                Name = request.Name?.Trim() ?? code,
                NameArabic = request.NameArabic?.Trim(),
                DecimalPlaces = request.DecimalPlaces,
                IsActive = request.IsActive,
            };

            context.Set<UnitOfMeasure>().Add(existing);
        }
        else
        {
            existing.Name = request.Name?.Trim() ?? existing.Name;
            existing.NameArabic = request.NameArabic?.Trim();
            existing.DecimalPlaces = request.DecimalPlaces;
            existing.IsActive = request.IsActive;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<UnitOfMeasure>.Success(existing);
    }

    /// <summary>
    /// Says what one of a unit holds, for one item.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="request">The unit and its factor.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The conversion as saved, or why it was refused.</returns>
    public async Task<Result<ItemUnit>> SaveItemUnitAsync(
        string itemNo,
        ItemUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalisedItem = itemNo?.Trim().ToUpperInvariant() ?? string.Empty;

        var item = await context.Set<Item>()
            .FirstOrDefaultAsync(i => i.No == normalisedItem, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Result<ItemUnit>.Failure(messages.Render(
                InventoryMessages.ItemNotFoundForUnit,
                Args(("ItemNo", normalisedItem))));
        }

        var code = request.UnitCode?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length == 0)
        {
            return Result<ItemUnit>.Failure(messages.Render(
                InventoryMessages.UnitCodeRequired,
                Args()));
        }

        // The company has to have agreed the word before an item can use it. Free text here is
        // how one shop ends up with CTN, CARTON and CASE all meaning the same thing.
        var known = await context.Set<UnitOfMeasure>()
            .AsNoTracking()
            .AnyAsync(u => u.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (!known)
        {
            return Result<ItemUnit>.Failure(messages.Render(
                InventoryMessages.UnitNotInCompanyList,
                Args(("UnitCode", code))));
        }

        var isBase = string.Equals(code, item.BaseUnitOfMeasure, StringComparison.OrdinalIgnoreCase);

        // One of the base unit is one of the base unit. Anything else says the item is counted in
        // something other than what it is counted in, and every stock figure it has is then wrong
        // by that factor.
        if (isBase && request.QuantityPerUnit != 1m)
        {
            return Result<ItemUnit>.Failure(messages.Render(
                InventoryMessages.BaseUnitFactorMustBeOne,
                Args(
                    ("ItemNo", item.No),
                    ("UnitCode", code),
                    ("QuantityPerUnit", request.QuantityPerUnit))));
        }

        if (request.QuantityPerUnit <= 0m)
        {
            return Result<ItemUnit>.Failure(messages.Render(
                InventoryMessages.UnitFactorNotUsable,
                Args(("ItemNo", item.No), ("UnitCode", code))));
        }

        var barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();

        if (barcode is not null)
        {
            var clash = await ClashAsync(item.Id, code, barcode, cancellationToken).ConfigureAwait(false);

            if (clash is not null)
            {
                return Result<ItemUnit>.Failure(clash);
            }
        }

        var existing = await context.Set<ItemUnit>()
            .FirstOrDefaultAsync(u => u.ItemId == item.Id && u.UnitCode == code, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new ItemUnit
            {
                TenantId = item.TenantId,
                CompanyId = item.CompanyId,
                ItemId = item.Id,
                UnitCode = code,
                QuantityPerUnit = request.QuantityPerUnit,
                Barcode = barcode,
                IsActive = request.IsActive,
            };

            context.Set<ItemUnit>().Add(existing);
        }
        else
        {
            existing.QuantityPerUnit = request.QuantityPerUnit;
            existing.Barcode = barcode;
            existing.IsActive = request.IsActive;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ItemUnit>.Success(existing);
    }

    /// <summary>
    /// Takes a unit off an item.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="unitCode">The unit to remove.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether it was removed, or why not.</returns>
    /// <remarks>
    /// Receipts and documents already posted keep the factor they were posted with, so removing a
    /// unit changes nothing that happened. It only stops the unit being chosen again.
    /// </remarks>
    public async Task<Result> RemoveItemUnitAsync(
        string itemNo,
        string unitCode,
        CancellationToken cancellationToken = default)
    {
        var normalisedItem = itemNo?.Trim().ToUpperInvariant() ?? string.Empty;
        var code = unitCode?.Trim().ToUpperInvariant() ?? string.Empty;

        var item = await context.Set<Item>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.No == normalisedItem, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Result.Failure(messages.Render(
                InventoryMessages.ItemNotFoundForUnit,
                Args(("ItemNo", normalisedItem))));
        }

        var existing = await context.Set<ItemUnit>()
            .FirstOrDefaultAsync(u => u.ItemId == item.Id && u.UnitCode == code, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return Result.Failure(messages.Render(
                InventoryMessages.UnitNotSetUpForItem,
                Args(
                    ("ItemNo", item.No),
                    ("UnitCode", code),
                    ("BaseUnit", item.BaseUnitOfMeasure))));
        }

        context.Set<ItemUnit>().Remove(existing);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Says who already has that barcode, when somebody does.
    /// </summary>
    /// <remarks>
    /// Both tables, because a barcode means one thing in a shop and the scanner does not know
    /// which table it came from. Two rows carrying the same barcode makes a scan return whichever
    /// the database reached first, which is not an error anybody sees until a stock count.
    /// </remarks>
    private async Task<AsapMessage?> ClashAsync(
        Guid itemId,
        string unitCode,
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
                Args(
                    ("Barcode", barcode),
                    ("ItemNo", onItem.No),
                    ("UnitCode", onItem.BaseUnitOfMeasure)));
        }

        var onUnit = await context.Set<ItemUnit>()
            .AsNoTracking()
            .Include(u => u.Item)
            .FirstOrDefaultAsync(
                u => u.Barcode == barcode && (u.ItemId != itemId || u.UnitCode != unitCode),
                cancellationToken)
            .ConfigureAwait(false);

        return onUnit is null
            ? null
            : messages.Render(
                InventoryMessages.BarcodeAlreadyInUse,
                Args(
                    ("Barcode", barcode),
                    ("ItemNo", onUnit.Item?.No ?? string.Empty),
                    ("UnitCode", onUnit.UnitCode)));
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
