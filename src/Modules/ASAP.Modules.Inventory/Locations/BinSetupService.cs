using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Locations;

/// <summary>A bin as somebody sets it up.</summary>
/// <param name="Code">Its code, unique inside its location.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="IsReceiving">Whether arrivals land here when nobody says where.</param>
/// <param name="PickOrder">The order a picker walks the bins in.</param>
/// <param name="IsBlocked">Whether it is withdrawn from use.</param>
public readonly record struct BinRequest(
    string Code,
    string? Name = null,
    string? NameArabic = null,
    bool IsReceiving = false,
    int PickOrder = 0,
    bool IsBlocked = false);

/// <summary>What one bin is holding.</summary>
/// <param name="BinCode">The bin.</param>
/// <param name="BinName">What it is called.</param>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What the item is called.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="Quantity">How much of it is there.</param>
public readonly record struct BinContentRow(
    string BinCode,
    string? BinName,
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    decimal Quantity);

/// <summary>
/// Sets up the shelves inside a location, and says what is on them.
/// </summary>
/// <remarks>
/// Bins are a refinement of a location, never a substitute for one. Nothing here touches a cost
/// or a valuation, and that is the point: turning bins on at a location changes where the system
/// says goods are, not how much there is or what it is worth.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
public sealed class BinSetupService(AsapDbContext context, IMessageCatalog messages)
{
    /// <summary>
    /// The bins at a location, in the order a picker walks them.
    /// </summary>
    /// <param name="locationCode">The location.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Its bins, or an empty list when there is no such location.</returns>
    public async Task<IReadOnlyList<Bin>> BinsAsync(
        string locationCode,
        CancellationToken cancellationToken = default)
    {
        var location = await FindLocationAsync(locationCode, cancellationToken).ConfigureAwait(false);

        if (location is null)
        {
            return [];
        }

        return await context.Set<Bin>()
            .AsNoTracking()
            .Where(b => b.LocationId == location.Id)
            .OrderBy(b => b.PickOrder)
            .ThenBy(b => b.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Turns bin tracking on or off at a location.
    /// </summary>
    /// <param name="locationCode">The location.</param>
    /// <param name="usesBins">Whether goods here are tracked down to a bin.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The location as saved, or why it was refused.</returns>
    /// <remarks>
    /// <para>
    /// Safe in both directions, and that is the payoff for keeping bins out of costing. Turning it
    /// on cannot change a valuation, because no quantity or cost ever depended on a bin. Stock
    /// already here simply has no shelf yet, and the first pick says so in a message that explains
    /// itself rather than failing.
    /// </para>
    /// <para>
    /// Turning it off leaves the bin codes on the entries that already carry them. Nothing reads
    /// them while it is off, and they are still true: those goods really were put on those
    /// shelves. Erasing them would destroy a record to tidy up a flag.
    /// </para>
    /// </remarks>
    public async Task<Result<Location>> SetUsesBinsAsync(
        string locationCode,
        bool usesBins,
        CancellationToken cancellationToken = default)
    {
        var location = await FindLocationAsync(locationCode, cancellationToken).ConfigureAwait(false);

        if (location is null)
        {
            return Result<Location>.Failure(messages.Render(
                InventoryMessages.LocationNotFound,
                Args(("Location", locationCode))));
        }

        location.UsesBins = usesBins;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<Location>.Success(location);
    }

    /// <summary>
    /// Adds a bin to a location, or changes one already there.
    /// </summary>
    /// <param name="locationCode">The location.</param>
    /// <param name="request">The bin.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The bin as saved, or why it was refused.</returns>
    public async Task<Result<Bin>> SaveAsync(
        string locationCode,
        BinRequest request,
        CancellationToken cancellationToken = default)
    {
        var location = await FindLocationAsync(locationCode, cancellationToken).ConfigureAwait(false);

        if (location is null)
        {
            return Result<Bin>.Failure(messages.Render(
                InventoryMessages.LocationNotFound,
                Args(("Location", locationCode))));
        }

        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length == 0)
        {
            return Result<Bin>.Failure(messages.Render(InventoryMessages.BinCodeRequired, Args()));
        }

        var existing = await context.Set<Bin>()
            .FirstOrDefaultAsync(b => b.LocationId == location.Id && b.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (request.IsReceiving)
        {
            // At most one, because "where do things go when nobody says" has to have one answer.
            // Two would make it depend on which row the database reached first, which is the sort
            // of thing that works until the day it does not.
            var other = await context.Set<Bin>()
                .FirstOrDefaultAsync(
                    b => b.LocationId == location.Id && b.IsReceiving && b.Code != code,
                    cancellationToken)
                .ConfigureAwait(false);

            if (other is not null)
            {
                other.IsReceiving = false;
            }
        }

        if (existing is null)
        {
            existing = new Bin
            {
                TenantId = location.TenantId,
                CompanyId = location.CompanyId,
                LocationId = location.Id,
                Code = code,
                Name = request.Name?.Trim(),
                NameArabic = request.NameArabic?.Trim(),
                IsReceiving = request.IsReceiving,
                PickOrder = request.PickOrder,
                IsBlocked = request.IsBlocked,
            };

            context.Set<Bin>().Add(existing);
        }
        else
        {
            existing.Name = request.Name?.Trim();
            existing.NameArabic = request.NameArabic?.Trim();
            existing.IsReceiving = request.IsReceiving;
            existing.PickOrder = request.PickOrder;
            existing.IsBlocked = request.IsBlocked;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<Bin>.Success(existing);
    }

    /// <summary>
    /// Takes a bin off a location.
    /// </summary>
    /// <param name="locationCode">The location.</param>
    /// <param name="binCode">The bin.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether it went, or why not.</returns>
    /// <remarks>
    /// Refused while anything is standing in it. A bin removed with stock in it does not lose the
    /// stock -- the location total is unchanged, because bins never held the quantity in the first
    /// place -- but it does lose the only record of where those goods are, which is the one thing
    /// a bin was for.
    /// </remarks>
    public async Task<Result> RemoveAsync(
        string locationCode,
        string binCode,
        CancellationToken cancellationToken = default)
    {
        var location = await FindLocationAsync(locationCode, cancellationToken).ConfigureAwait(false);

        if (location is null)
        {
            return Result.Failure(messages.Render(
                InventoryMessages.LocationNotFound,
                Args(("Location", locationCode))));
        }

        var code = binCode?.Trim().ToUpperInvariant() ?? string.Empty;

        var bin = await context.Set<Bin>()
            .FirstOrDefaultAsync(b => b.LocationId == location.Id && b.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (bin is null)
        {
            return Result.Failure(messages.Render(
                InventoryMessages.BinNotFound,
                Args(("BinCode", code), ("Location", location.Code))));
        }

        var standing = await context.Set<ItemLedgerEntry>()
            .Where(e => e.BinId == bin.Id)
            .GroupBy(static e => e.ItemNo)
            .Select(static g => new { ItemNo = g.Key, Quantity = g.Sum(static e => e.Quantity) })
            .Where(static g => g.Quantity != 0m)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (standing.Count > 0)
        {
            return Result.Failure(messages.Render(
                InventoryMessages.BinNotEmpty,
                Args(
                    ("BinCode", bin.Code),
                    ("Location", location.Code),
                    ("ItemCount", standing.Count),
                    ("Items", string.Join(", ", standing.Take(5).Select(s => $"{s.ItemNo} ({s.Quantity:0.#####})"))))));
        }

        context.Set<Bin>().Remove(bin);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// What is standing on each shelf at a location.
    /// </summary>
    /// <param name="locationCode">The location.</param>
    /// <param name="itemNo">One item, or null for all of them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The contents, in pick order.</returns>
    /// <remarks>
    /// Summed from the same ledger the location total comes from, so the bins add up to the
    /// location by construction. Nothing reconciles the two because there is nothing to
    /// reconcile: they are the same entries grouped differently.
    /// </remarks>
    public async Task<IReadOnlyList<BinContentRow>> ContentsAsync(
        string locationCode,
        string? itemNo = null,
        CancellationToken cancellationToken = default)
    {
        var location = await FindLocationAsync(locationCode, cancellationToken).ConfigureAwait(false);

        if (location is null)
        {
            return [];
        }

        var wanted = itemNo?.Trim().ToUpperInvariant();

        var totals = await context.Set<ItemLedgerEntry>()
            .Where(e => e.LocationId == location.Id && e.BinId != null
                && (wanted == null || e.ItemNo == wanted))
            .GroupBy(static e => new { e.BinId, e.ItemNo })
            .Select(static g => new
            {
                g.Key.BinId,
                g.Key.ItemNo,
                Quantity = g.Sum(static e => e.Quantity),
            })
            .Where(static g => g.Quantity != 0m)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (totals.Count == 0)
        {
            return [];
        }

        var binIds = totals.Select(static t => t.BinId!.Value).Distinct().ToList();
        var itemNos = totals.Select(static t => t.ItemNo).Distinct().ToList();

        var bins = await context.Set<Bin>()
            .AsNoTracking()
            .Where(b => binIds.Contains(b.Id))
            .ToDictionaryAsync(static b => b.Id, cancellationToken)
            .ConfigureAwait(false);

        var items = await context.Set<Items.Item>()
            .AsNoTracking()
            .Where(i => itemNos.Contains(i.No))
            .ToDictionaryAsync(static i => i.No, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. totals
                .Where(t => bins.ContainsKey(t.BinId!.Value))
                .Select(t =>
                {
                    var bin = bins[t.BinId!.Value];
                    var item = items.GetValueOrDefault(t.ItemNo);

                    return (bin.PickOrder, Row: new BinContentRow(
                        bin.Code,
                        bin.Name,
                        t.ItemNo,
                        item?.Description ?? t.ItemNo,
                        item?.DescriptionArabic,
                        t.Quantity));
                })
                .OrderBy(static x => x.PickOrder)
                .ThenBy(static x => x.Row.BinCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static x => x.Row.ItemNo, StringComparer.OrdinalIgnoreCase)
                .Select(static x => x.Row),
        ];
    }

    private Task<Location?> FindLocationAsync(string locationCode, CancellationToken cancellationToken)
    {
        var code = locationCode?.Trim().ToUpperInvariant() ?? string.Empty;

        return context.Set<Location>().FirstOrDefaultAsync(l => l.Code == code, cancellationToken);
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
