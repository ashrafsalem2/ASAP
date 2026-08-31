using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Locations;

/// <summary>One thing to move from one shelf to another.</summary>
/// <param name="ItemNo">What to move.</param>
/// <param name="FromBinCode">The shelf it comes off.</param>
/// <param name="ToBinCode">The shelf it goes onto.</param>
/// <param name="Quantity">How much.</param>
/// <param name="VariantCode">Which variant, on an item that has them.</param>
public readonly record struct BinMovementLineRequest(
    string ItemNo,
    string FromBinCode,
    string ToBinCode,
    decimal Quantity,
    string? VariantCode = null);

/// <summary>
/// Moves goods between shelves inside one place.
/// </summary>
/// <remarks>
/// <para>
/// The entries written here carry no value and take part in no costing. That is the whole point:
/// a box moved from one shelf to the next is worth exactly what it was worth before, and putting
/// the move through the ordinary costing would consume a cost layer on the way out and create a
/// new one on the way in — fragmenting the layers and moving the valuation for a change that
/// moved nothing but a box.
/// </para>
/// <para>
/// Bin contents are the sum of the quantities on the entries, so a matched pair of minus and plus
/// says what happened and nets to nothing at the location. On-hand is untouched because the pair
/// sums to zero; valuation is untouched because it is driven from the value entries, and there
/// are none.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="numbers">Issues the movement number.</param>
/// <param name="setup">Supplies the number series.</param>
/// <param name="transactionNumbers">Issues the number that groups the entries.</param>
/// <param name="branches">Says which branch a location belongs to.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="user">Says who recorded it.</param>
/// <param name="clock">Says what today is.</param>
public sealed class BinMovementService(
    AsapDbContext context,
    IMessageCatalog messages,
    INumberSeriesService numbers,
    ISetupService setup,
    ITransactionNumberAllocator transactionNumbers,
    LocationBranchLookup branches,
    ITenantContext tenancy,
    IUserContext user,
    IClock clock)
{
    /// <summary>The movements, most recent first.</summary>
    /// <param name="locationCode">One location, or null for all of them.</param>
    /// <param name="take">How many.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The movements and their lines.</returns>
    public async Task<IReadOnlyList<BinMovement>> ListAsync(
        string? locationCode = null,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<BinMovement>()
            .AsNoTracking()
            .Include(m => m.Lines);

        var filtered = string.IsNullOrWhiteSpace(locationCode)
            ? query
            : query.Where(m => m.LocationCode == locationCode.Trim().ToUpperInvariant());

        return await filtered
            .OrderByDescending(m => m.MovementDate)
            .ThenByDescending(m => m.No)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>One movement and what is on it.</summary>
    /// <param name="no">The movement number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The movement, or null.</returns>
    public Task<BinMovement?> LoadAsync(string no, CancellationToken cancellationToken = default)
        => context.Set<BinMovement>()
            .AsNoTracking()
            .Include(m => m.Lines)
            .FirstOrDefaultAsync(m => m.No == no, cancellationToken);

    /// <summary>
    /// Records a sheet of moves and puts them through, all or none.
    /// </summary>
    /// <param name="locationCode">Where it happened.</param>
    /// <param name="lines">What moved.</param>
    /// <param name="movementDate">When, or null for today.</param>
    /// <param name="note">Why, where anybody said.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The movement, or every reason it was refused.</returns>
    public async Task<Result<BinMovement>> PostAsync(
        string locationCode,
        IReadOnlyList<BinMovementLineRequest> lines,
        DateOnly? movementDate = null,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var code = locationCode?.Trim().ToUpperInvariant() ?? string.Empty;

        var location = await context.Set<Location>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (location is null)
        {
            return Result<BinMovement>.Failure(
                messages.Render(InventoryMessages.LocationNotFound, Args(("LocationCode", code))));
        }

        if (!location.UsesBins)
        {
            return Result<BinMovement>.Failure(messages.Render(
                InventoryMessages.BinMovementWithoutBins,
                Args(("LocationCode", code), ("LocationName", location.Name))));
        }

        if (lines.Count == 0)
        {
            return Result<BinMovement>.Failure(
                messages.Render(InventoryMessages.BinMovementHasNoLines, Args(("LocationCode", code))));
        }

        var found = new List<AsapMessage>();

        var bins = (await context.Set<Bin>()
                .AsNoTracking()
                .Where(b => b.LocationId == location.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(static b => b.Code, StringComparer.OrdinalIgnoreCase);

        var itemNos = lines.Select(static l => l.ItemNo.Trim().ToUpperInvariant()).Distinct().ToList();

        var items = (await context.Set<Item>()
                .AsNoTracking()
                .Where(i => itemNos.Contains(i.No))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(static i => i.No, StringComparer.OrdinalIgnoreCase);

        // What each bin holds now. Read once for the whole sheet, and reduced as the sheet is
        // walked, so eleven lines all taking from one shelf are refused on the twelfth rather
        // than each passing a check against the same untouched figure.
        var held = await HeldAsync(location.Id, itemNos, cancellationToken).ConfigureAwait(false);

        var checkedLines = new List<(BinMovementLineRequest Request, Bin From, Bin To, Item Item)>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var itemNo = line.ItemNo?.Trim().ToUpperInvariant() ?? string.Empty;
            var fromCode = line.FromBinCode?.Trim().ToUpperInvariant() ?? string.Empty;
            var toCode = line.ToBinCode?.Trim().ToUpperInvariant() ?? string.Empty;
            var variant = line.VariantCode?.Trim().ToUpperInvariant();

            var target = MessageTarget.OnField($"Lines[{index + 1}]");

            var arguments = Args(
                ("LineNo", index + 1),
                ("ItemNo", itemNo),
                ("LocationCode", code),

                // The bin messages name the place as {Location}, not {LocationCode}. Passing only
                // the one leaves the other rendered literally in front of somebody.
                ("Location", location.Name),
                ("FromBinCode", fromCode),
                ("ToBinCode", toCode),
                ("Quantity", line.Quantity));

            if (!items.TryGetValue(itemNo, out var item))
            {
                found.Add(messages.Render(InventoryMessages.ItemNotFound, arguments, target));
                continue;
            }

            if (line.Quantity <= 0m)
            {
                found.Add(messages.Render(InventoryMessages.BinMovementQuantityZero, arguments, target));
                continue;
            }

            if (!bins.TryGetValue(fromCode, out var from))
            {
                arguments["BinCode"] = fromCode;
                found.Add(messages.Render(InventoryMessages.BinNotFound, arguments, target));
                continue;
            }

            if (!bins.TryGetValue(toCode, out var to))
            {
                arguments["BinCode"] = toCode;
                found.Add(messages.Render(InventoryMessages.BinNotFound, arguments, target));
                continue;
            }

            if (from.Id == to.Id)
            {
                found.Add(messages.Render(InventoryMessages.BinMovementToItself, arguments, target));
                continue;
            }

            var key = (from.Id, itemNo, variant);
            var available = held.GetValueOrDefault(key);

            if (available < line.Quantity)
            {
                arguments["AvailableQuantity"] = available;
                arguments["BinCode"] = fromCode;

                found.Add(messages.Render(InventoryMessages.NotEnoughInBin, arguments, target));
                continue;
            }

            held[key] = available - line.Quantity;

            var destination = (to.Id, itemNo, variant);
            held[destination] = held.GetValueOrDefault(destination) + line.Quantity;

            checkedLines.Add((line with { ItemNo = itemNo, VariantCode = variant }, from, to, item));
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<BinMovement>.Failure(found);
        }

        var seriesCode = await setup
            .GetAsync<string>($"{InventoryModule.Id}.BinMovement.NumberSeries", cancellationToken)
            .ConfigureAwait(false) ?? "BINMOVE";

        var on = movementDate ?? clock.Today;

        var numbered = await numbers.NextAsync(seriesCode, on, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<BinMovement>.FailureFrom(numbered);
        }

        var transactionNo = await transactionNumbers.NextAsync(cancellationToken).ConfigureAwait(false);
        var branchId = await branches.BranchOfAsync(location.Code, cancellationToken).ConfigureAwait(false);

        var movement = new BinMovement
        {
            TenantId = tenancy.RequireTenantId(),
            CompanyId = tenancy.RequireCompanyId(),
            No = numbered.Value,
            LocationId = location.Id,
            LocationCode = location.Code,
            MovementDate = on,
            Status = BinMovementStatus.Posted,
            Note = note?.Trim(),
            RecordedByUserName = user.DisplayName ?? user.UserName,
            TransactionNo = transactionNo,
        };

        context.Set<BinMovement>().Add(movement);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var lineNo = 0;

        foreach (var (request, from, to, item) in checkedLines)
        {
            lineNo += 10;

            context.Set<BinMovementLine>().Add(new BinMovementLine
            {
                TenantId = movement.TenantId,
                CompanyId = movement.CompanyId,
                BinMovementId = movement.Id,
                LineNo = lineNo,
                ItemNo = request.ItemNo,
                VariantCode = request.VariantCode,
                FromBinCode = from.Code,
                ToBinCode = to.Code,
                Quantity = request.Quantity,
            });

            // A matched pair, both with nothing remaining. Remaining quantity is what makes an
            // entry a cost layer available to be consumed, and neither of these is one: no value
            // was created and none was consumed.
            context.Set<ItemLedgerEntry>().AddRange(
                Entry(movement, item, request, from, -request.Quantity, branchId, transactionNo),
                Entry(movement, item, request, to, request.Quantity, branchId, transactionNo));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var saved = await context.Set<BinMovement>()
            .AsNoTracking()
            .Include(m => m.Lines)
            .FirstAsync(m => m.Id == movement.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result<BinMovement>.Success(saved, found);
    }

    private static ItemLedgerEntry Entry(
        BinMovement movement,
        Item item,
        BinMovementLineRequest request,
        Bin bin,
        decimal quantity,
        Guid? branchId,
        long transactionNo)
        => new()
        {
            TenantId = movement.TenantId,
            CompanyId = movement.CompanyId,
            ItemId = item.Id,
            ItemNo = item.No,
            PostingDate = movement.MovementDate,
            LocationId = movement.LocationId,
            LocationCode = movement.LocationCode,
            VariantCode = request.VariantCode,
            BinId = bin.Id,
            BinCode = bin.Code,
            Quantity = quantity,
            RemainingQuantity = 0m,
            EntryType = quantity < 0m
                ? ItemLedgerEntryType.BinMovementOut
                : ItemLedgerEntryType.BinMovementIn,
            DocumentNo = movement.No,
            BranchId = branchId,
            SourceCode = "BINMOVE",
            TransactionNo = transactionNo,
        };

    /// <summary>What each bin holds of each item, from the entries.</summary>
    private async Task<Dictionary<(Guid BinId, string ItemNo, string? VariantCode), decimal>> HeldAsync(
        Guid locationId,
        IReadOnlyList<string> itemNos,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.LocationId == locationId && e.BinId != null && itemNos.Contains(e.ItemNo))
            .GroupBy(static e => new { e.BinId, e.ItemNo, e.VariantCode })
            .Select(static g => new
            {
                g.Key.BinId,
                g.Key.ItemNo,
                g.Key.VariantCode,
                Quantity = g.Sum(static e => e.Quantity),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            static r => (r.BinId!.Value, r.ItemNo, r.VariantCode),
            static r => r.Quantity);
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
