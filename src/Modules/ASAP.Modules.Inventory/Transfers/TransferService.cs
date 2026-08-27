using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Inventory.Transfers;

/// <summary>What a shipment or receipt produced.</summary>
/// <param name="TransferNo">The transfer.</param>
/// <param name="TransactionNo">The transaction the movements were posted under.</param>
/// <param name="LineCount">How many lines moved.</param>
/// <param name="Status">Where the transfer stands now.</param>
public readonly record struct TransferReceipt(
    string TransferNo,
    long TransactionNo,
    int LineCount,
    TransferStatus Status);

/// <summary>
/// Ships and receives transfers.
/// </summary>
/// <remarks>
/// <para>
/// Each half is an ordinary pair of stock movements, which is deliberate. Shipping issues from the
/// source and receives into the in-transit location; receiving issues from in transit and receives
/// into the destination. Nothing about transfers needs its own costing, its own ledger rules or
/// its own idea of what stock is -- it is the existing posting engine used twice.
/// </para>
/// <para>
/// The value never leaves inventory, so neither half posts anything to the general ledger. What
/// moves is where the goods are, not what the company owns.
/// </para>
/// </remarks>
/// <summary>One line asked for on a new transfer.</summary>
/// <param name="ItemNo">The item to move.</param>
/// <param name="Quantity">How much to move. Always positive; the direction is the transfer's.</param>
public readonly record struct TransferLineRequest(string ItemNo, decimal Quantity);

/// <param name="context">The unit of work.</param>
/// <param name="posting">Moves the stock.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="numbers">Issues the transfer number.</param>
/// <param name="tenantContext">Supplies the company the transfer belongs to.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records shipments and receipts.</param>
public sealed class TransferService(
    AsapDbContext context,
    StockPostingService posting,
    IMessageCatalog messages,
    INumberSeriesService numbers,
    ITenantContext tenantContext,
    IClock clock,
    ILogger<TransferService> logger)
{
    /// <summary>The series transfer numbers come from.</summary>
    private const string NumberSeriesCode = "TRANSFER";

    /// <summary>
    /// Creates a transfer, ready to be shipped.
    /// </summary>
    /// <param name="fromLocationCode">Where the goods leave.</param>
    /// <param name="toLocationCode">Where they are going.</param>
    /// <param name="lines">What is moving.</param>
    /// <param name="description">A note for whoever handles it.</param>
    /// <param name="expectedReceiptDate">When it should arrive.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The created transfer, or every reason it was refused.</returns>
    /// <remarks>
    /// Nothing moves here. Creating a transfer records an intention; the stock stays exactly where
    /// it is until somebody ships it. That separation is what lets a branch raise a request its
    /// warehouse fulfils later, and what makes the paperwork survive goods that never leave.
    /// </remarks>
    public async Task<Result<TransferOrder>> CreateAsync(
        string fromLocationCode,
        string toLocationCode,
        IReadOnlyList<TransferLineRequest> lines,
        string? description = null,
        DateOnly? expectedReceiptDate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["From"] = fromLocationCode,
            ["To"] = toLocationCode,
        };

        var refusals = new List<AsapMessage>();

        if (string.Equals(fromLocationCode, toLocationCode, StringComparison.OrdinalIgnoreCase))
        {
            refusals.Add(messages.Render(InventoryMessages.TransferToSameLocation, arguments));
        }

        var wanted = lines.Where(static l => l.Quantity > 0).ToList();

        if (wanted.Count == 0)
        {
            refusals.Add(messages.Render(InventoryMessages.TransferNoLines, arguments));
        }

        var locations = await context.Set<Location>()
            .Where(l => l.Code == fromLocationCode || l.Code == toLocationCode)
            .ToDictionaryAsync(static l => l.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        // Every reason at once. Sending them back one at a time turns a single correction into
        // four round trips.
        CheckLocation(fromLocationCode, locations, refusals);
        CheckLocation(toLocationCode, locations, refusals);

        var items = await ResolveLinesAsync(wanted, refusals, cancellationToken).ConfigureAwait(false);

        if (refusals.Count > 0)
        {
            return Result<TransferOrder>.Failure(refusals);
        }

        var today = clock.Today;
        var numbered = await numbers.NextAsync(NumberSeriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<TransferOrder>.FailureFrom(numbered);
        }

        var from = locations[fromLocationCode];
        var to = locations[toLocationCode];

        var transfer = new TransferOrder
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.RequireCompanyId(),
            No = numbered.Value,
            FromLocationId = from.Id,
            FromLocationCode = from.Code,
            ToLocationId = to.Id,
            ToLocationCode = to.Code,
            Status = TransferStatus.Open,
            ShipmentDate = today,
            ExpectedReceiptDate = expectedReceiptDate,
            Description = description,
        };

        var lineNo = 0;

        foreach (var line in wanted)
        {
            var item = items[line.ItemNo];

            transfer.Lines.Add(new TransferOrderLine
            {
                TenantId = transfer.TenantId,
                CompanyId = transfer.CompanyId,
                LineNo = ++lineNo * 10,
                ItemId = item.Id,
                ItemNo = item.No,
                Description = item.Description,
                Quantity = line.Quantity,
            });
        }

        context.Set<TransferOrder>().Add(transfer);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Created transfer {TransferNo} from {From} to {To} with {LineCount} line(s).",
            transfer.No,
            transfer.FromLocationCode,
            transfer.ToLocationCode,
            transfer.Lines.Count);

        return Result<TransferOrder>.Success(transfer);
    }

    private void CheckLocation(
        string code,
        IReadOnlyDictionary<string, Location> locations,
        List<AsapMessage> refusals)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Location"] = code,
        };

        if (!locations.TryGetValue(code, out var location))
        {
            refusals.Add(messages.Render(InventoryMessages.LocationNotFound, arguments));
            return;
        }

        if (location.IsBlocked)
        {
            arguments["Location"] = location.Name;
            refusals.Add(messages.Render(InventoryMessages.LocationBlocked, arguments));
        }
    }

    private async Task<Dictionary<string, Item>> ResolveLinesAsync(
        IReadOnlyList<TransferLineRequest> lines,
        List<AsapMessage> refusals,
        CancellationToken cancellationToken)
    {
        var itemNos = lines.Select(static l => l.ItemNo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var items = await context.Set<Item>()
            .Where(i => itemNos.Contains(i.No))
            .ToDictionaryAsync(static i => i.No, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var target = MessageTarget.OnField($"Lines[{index + 1}]");

            if (!items.TryGetValue(line.ItemNo, out var item))
            {
                refusals.Add(messages.Render(
                    InventoryMessages.ItemNotFound,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ItemNo"] = line.ItemNo,
                    },
                    target));

                continue;
            }

            if (item.IsBlocked)
            {
                refusals.Add(messages.Render(
                    InventoryMessages.ItemBlocked,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ItemNo"] = item.No,
                        ["ItemName"] = item.Description,
                    },
                    target));
            }
        }

        return items;
    }

    /// <summary>
    /// Ships a transfer: goods leave the source and go into transit.
    /// </summary>
    /// <param name="transferNo">The transfer to ship.</param>
    /// <param name="companyAllowsNegative">Whether the company permits stock below zero.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<Result<TransferReceipt>> ShipAsync(
        string transferNo,
        bool companyAllowsNegative,
        IReadOnlySet<string>? heldOverridePermissions = null,
        CancellationToken cancellationToken = default)
    {
        var transfer = await LoadAsync(transferNo, cancellationToken).ConfigureAwait(false);

        if (transfer is null)
        {
            return NotFound(transferNo);
        }

        if (transfer.HasShipped)
        {
            return Result<TransferReceipt>.Failure(messages.Render(
                InventoryMessages.TransferAlreadyShipped,
                Arguments(transfer)));
        }

        var inTransit = await InTransitLocationAsync(transfer, cancellationToken).ConfigureAwait(false);

        if (inTransit is null)
        {
            return Result<TransferReceipt>.Failure(messages.Render(
                InventoryMessages.NoInTransitLocation,
                Arguments(transfer)));
        }

        var lines = transfer.Lines.Where(static l => l.OutstandingToShip > 0).ToList();

        if (lines.Count == 0)
        {
            return Result<TransferReceipt>.Failure(messages.Render(
                InventoryMessages.TransferNothingToMove,
                Arguments(transfer)));
        }

        // Out of the source and into transit, as one posting, so the goods are never in neither
        // place nor both.
        var movements = lines
            .SelectMany(line => new[]
            {
                new StockMovementRequest(
                    line.ItemNo,
                    transfer.FromLocationCode,
                    -line.OutstandingToShip,
                    EntryType: ItemLedgerEntryType.TransferOut),
                new StockMovementRequest(
                    line.ItemNo,
                    inTransit.Code,
                    line.OutstandingToShip,
                    EntryType: ItemLedgerEntryType.TransferIn),
            })
            .ToList();

        var result = await posting
            .PostAsync(
                movements,
                clock.Today,
                "TRANSFER",
                transfer.No,
                companyAllowsNegative,
                heldOverridePermissions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return Result<TransferReceipt>.FailureFrom(result);
        }

        foreach (var line in lines)
        {
            line.QuantityShipped += line.OutstandingToShip;
        }

        transfer.Status = TransferStatus.Shipped;
        transfer.ShippedOn = clock.Today;
        transfer.ShipmentTransactionNo = result.Value.TransactionNo;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Shipped transfer {TransferNo}: {LineCount} line(s) from {From} into transit.",
            transfer.No,
            lines.Count,
            transfer.FromLocationCode);

        return Result<TransferReceipt>.Success(
            new TransferReceipt(transfer.No, result.Value.TransactionNo, lines.Count, transfer.Status),
            result.Messages);
    }

    /// <summary>
    /// Receives a transfer: goods leave transit and arrive at the destination.
    /// </summary>
    /// <param name="transferNo">The transfer to receive.</param>
    /// <param name="shortages">
    /// Quantities actually received, by item, where they differ from what was shipped. Anything
    /// not named is taken as arriving in full.
    /// </param>
    /// <param name="companyAllowsNegative">Whether the company permits stock below zero.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <remarks>
    /// A shortage leaves the difference sitting in the in-transit location rather than writing it
    /// off silently. That is the honest position: the goods left, they did not arrive, and until
    /// somebody investigates nobody knows whether they are lost, stolen or on the next lorry.
    /// Writing them off here would decide that question by default.
    /// </remarks>
    public async Task<Result<TransferReceipt>> ReceiveAsync(
        string transferNo,
        IReadOnlyDictionary<string, decimal>? shortages = null,
        bool companyAllowsNegative = false,
        IReadOnlySet<string>? heldOverridePermissions = null,
        CancellationToken cancellationToken = default)
    {
        var transfer = await LoadAsync(transferNo, cancellationToken).ConfigureAwait(false);

        if (transfer is null)
        {
            return NotFound(transferNo);
        }

        if (!transfer.HasShipped)
        {
            return Result<TransferReceipt>.Failure(messages.Render(
                InventoryMessages.TransferNotShipped,
                Arguments(transfer)));
        }

        var inTransit = await InTransitLocationAsync(transfer, cancellationToken).ConfigureAwait(false);

        if (inTransit is null)
        {
            return Result<TransferReceipt>.Failure(messages.Render(
                InventoryMessages.NoInTransitLocation,
                Arguments(transfer)));
        }

        var arriving = transfer.Lines
            .Where(static l => l.InTransit > 0)
            .Select(line => (Line: line, Quantity: QuantityArriving(line, shortages)))
            .Where(static x => x.Quantity > 0)
            .ToList();

        if (arriving.Count == 0)
        {
            return Result<TransferReceipt>.Failure(messages.Render(
                InventoryMessages.TransferNothingToMove,
                Arguments(transfer)));
        }

        var movements = arriving
            .SelectMany(x => new[]
            {
                new StockMovementRequest(
                    x.Line.ItemNo,
                    inTransit.Code,
                    -x.Quantity,
                    EntryType: ItemLedgerEntryType.TransferOut),
                new StockMovementRequest(
                    x.Line.ItemNo,
                    transfer.ToLocationCode,
                    x.Quantity,
                    EntryType: ItemLedgerEntryType.TransferIn),
            })
            .ToList();

        var result = await posting
            .PostAsync(
                movements,
                clock.Today,
                "TRANSFER",
                transfer.No,
                companyAllowsNegative,
                heldOverridePermissions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return Result<TransferReceipt>.FailureFrom(result);
        }

        foreach (var (line, quantity) in arriving)
        {
            line.QuantityReceived += quantity;
        }

        // Still in transit means the transfer is not finished, whether because a line was short or
        // because only part of the load has arrived.
        transfer.Status = transfer.Lines.Any(static l => l.InTransit > 0)
            ? TransferStatus.PartiallyReceived
            : TransferStatus.Received;

        transfer.ReceivedOn = clock.Today;
        transfer.ReceiptTransactionNo = result.Value.TransactionNo;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var stillInTransit = transfer.Lines.Sum(static l => l.InTransit);

        logger.LogInformation(
            "Received transfer {TransferNo} at {To}: {LineCount} line(s), {InTransit} still in transit.",
            transfer.No,
            transfer.ToLocationCode,
            arriving.Count,
            stillInTransit);

        var reported = result.Messages.ToList();

        if (stillInTransit > 0)
        {
            reported.Add(messages.Render(
                InventoryMessages.TransferShortReceipt,
                new Dictionary<string, object?>(Arguments(transfer), StringComparer.OrdinalIgnoreCase)
                {
                    ["ShortfallQuantity"] = stillInTransit,
                    ["Location"] = inTransit.Name,
                }));
        }

        return Result<TransferReceipt>.Success(
            new TransferReceipt(transfer.No, result.Value.TransactionNo, arriving.Count, transfer.Status),
            reported);
    }

    private static decimal QuantityArriving(
        TransferOrderLine line,
        IReadOnlyDictionary<string, decimal>? shortages)
    {
        if (shortages is null || !shortages.TryGetValue(line.ItemNo, out var received))
        {
            return line.InTransit;
        }

        // Never more than left, whatever the receiving branch keys. More arriving than was sent is
        // not a transfer, it is a stock count difference, and belongs on an adjustment.
        return Math.Clamp(received, 0m, line.InTransit);
    }

    private Task<TransferOrder?> LoadAsync(string transferNo, CancellationToken cancellationToken)
        => context.Set<TransferOrder>()
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.No == transferNo, cancellationToken);

    /// <summary>
    /// Finds where goods travel through: the transfer's own in-transit location, or the company's.
    /// </summary>
    private async Task<Location?> InTransitLocationAsync(
        TransferOrder transfer,
        CancellationToken cancellationToken)
        => transfer.InTransitLocationId is { } id
            ? await context.Set<Location>()
                .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                .ConfigureAwait(false)
            : await context.Set<Location>()
                .Where(l => l.IsInTransit && !l.IsBlocked)
                .OrderBy(l => l.Code)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

    private static Dictionary<string, object?> Arguments(TransferOrder transfer)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["TransferNo"] = transfer.No,
            ["From"] = transfer.FromLocationCode,
            ["To"] = transfer.ToLocationCode,
            ["Status"] = transfer.Status.ToString(),
        };

    private Result<TransferReceipt> NotFound(string transferNo)
        => Result<TransferReceipt>.Failure(messages.Render(
            InventoryMessages.TransferNotFound,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["TransferNo"] = transferNo,
            }));
}
