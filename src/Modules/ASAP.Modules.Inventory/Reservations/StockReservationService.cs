using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Inventory.Reservations;

/// <summary>What is on the shelf and how much of it is still free.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="LocationCode">Where.</param>
/// <param name="VariantCode">Which variant, where the item has them.</param>
/// <param name="QuantityOnHand">What is physically there.</param>
/// <param name="QuantityReserved">How much of it is spoken for.</param>
/// <param name="QuantityAvailable">
/// What is left to promise. On hand less what is reserved, and never mind that the two together
/// are the only figures anybody can act on.
/// </param>
public readonly record struct StockAvailabilityView(
    string ItemNo,
    string LocationCode,
    string? VariantCode,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal QuantityAvailable);

/// <summary>
/// Holds stock for a document, so a promise made once is not made twice.
/// </summary>
/// <remarks>
/// <para>
/// Reserving posts nothing. It is a claim on stock rather than a movement of it, and everything
/// here follows from that: no ledger entry, no cost, no transaction number, nothing for a
/// settlement routine to come back to.
/// </para>
/// <para>
/// Reserving more than is free is refused rather than warned about. That is a deliberate
/// difference from selling into negative stock, and the reason is who is standing there. A sale
/// below zero is a real decision somebody makes with a customer in front of them and the goods
/// visible on the shelf; a reservation is planning, made at a desk, with no urgency and nobody
/// waiting. A promise made against stock that does not exist is not a promise, and there is
/// nothing to be gained by letting it be made quietly.
/// </para>
/// <para>
/// Shipping consumes the reservation the document made. Anything left over stays held, which is
/// how a part shipment keeps the rest of the order's goods -- and it is also how stock gets
/// stranded when an order is abandoned, so the outstanding list is worth reading.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="logger">Records what was held and released.</param>
public sealed class StockReservationService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenancy,
    ILogger<StockReservationService> logger)
{
    /// <summary>
    /// Holds stock for a document.
    /// </summary>
    /// <param name="itemNo">What to hold.</param>
    /// <param name="locationCode">Where it is being held.</param>
    /// <param name="quantity">How much. Always positive.</param>
    /// <param name="documentNo">What it is being held for.</param>
    /// <param name="documentLineNo">Which line of that document.</param>
    /// <param name="variantCode">Which variant, on an item that has them.</param>
    /// <param name="sourceCode">Which module the document belongs to.</param>
    /// <param name="note">Why, where it is worth saying.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The reservation, or every reason it was refused.</returns>
    public async Task<Result<StockReservation>> ReserveAsync(
        string itemNo,
        string locationCode,
        decimal quantity,
        string documentNo,
        int? documentLineNo = null,
        string? variantCode = null,
        string? sourceCode = null,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ItemNo"] = itemNo,
            ["Location"] = locationCode,
            ["DocumentNo"] = documentNo,
        };

        if (quantity <= 0m)
        {
            return Result<StockReservation>.Failure(
                messages.Render(InventoryMessages.ReservationQuantityZero, arguments));
        }

        if (string.IsNullOrWhiteSpace(documentNo))
        {
            return Result<StockReservation>.Failure(
                messages.Render(InventoryMessages.ReservationNeedsADocument, arguments));
        }

        var item = await context.Set<Item>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.No == itemNo, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Result<StockReservation>.Failure(
                messages.Render(InventoryMessages.ItemNotFound, arguments));
        }

        var location = await context.Set<Location>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Code == locationCode, cancellationToken)
            .ConfigureAwait(false);

        if (location is null)
        {
            return Result<StockReservation>.Failure(
                messages.Render(InventoryMessages.LocationNotFound, arguments));
        }

        var variant = await ResolveVariantAsync(item.Id, variantCode, cancellationToken)
            .ConfigureAwait(false);

        if (variant.Failed)
        {
            return Result<StockReservation>.FailureFrom(variant);
        }

        var variantId = variant.Value;

        // What is free, counting everything held for anybody else. Held for this same document is
        // not counted against it: adding to an order's own reservation must not be refused because
        // of what that order already holds.
        var onHand = await OnHandAsync(item.Id, variantId, location.Id, cancellationToken)
            .ConfigureAwait(false);

        var heldByOthers = await ReservedAsync(
                item.Id, variantId, location.Id, exceptDocumentNo: documentNo, cancellationToken)
            .ConfigureAwait(false);

        var held = await context.Set<StockReservation>()
            .FirstOrDefaultAsync(
                r => r.ItemId == item.Id
                    && r.VariantId == variantId
                    && r.LocationId == location.Id
                    && r.DocumentNo == documentNo
                    && r.DocumentLineNo == documentLineNo,
                cancellationToken)
            .ConfigureAwait(false);

        var alreadyHeldHere = held?.QuantityOutstanding ?? 0m;
        var free = onHand - heldByOthers - alreadyHeldHere;

        if (quantity > free)
        {
            arguments["Wanted"] = quantity;
            arguments["QuantityAvailable"] = free;
            arguments["QuantityOnHand"] = onHand;
            arguments["QuantityReserved"] = heldByOthers + alreadyHeldHere;

            return Result<StockReservation>.Failure(
                messages.Render(InventoryMessages.NotEnoughToReserve, arguments));
        }

        if (held is null)
        {
            held = new StockReservation
            {
                TenantId = tenancy.TenantId ?? Guid.Empty,
                CompanyId = tenancy.RequireCompanyId(),
                ItemId = item.Id,
                ItemNo = item.No,
                VariantId = variantId,
                VariantCode = variant.Value is null ? null : variantCode?.Trim().ToUpperInvariant(),
                LocationId = location.Id,
                LocationCode = location.Code,
                DocumentNo = documentNo,
                DocumentLineNo = documentLineNo,
                SourceCode = sourceCode,
                Quantity = quantity,
                QuantityOutstanding = quantity,
                Note = note,
            };

            context.Set<StockReservation>().Add(held);
        }
        else
        {
            held.Quantity += quantity;
            held.QuantityOutstanding += quantity;
            held.ReleaseReason = null;
            held.Note = note ?? held.Note;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Reserved {Quantity} of {ItemNo} at {Location} for {DocumentNo}.",
            quantity,
            item.No,
            location.Code,
            documentNo);

        return Result<StockReservation>.Success(held);
    }

    /// <summary>
    /// Lets stock go that was being held.
    /// </summary>
    /// <remarks>
    /// Releasing keeps the row. A reservation that vanished when it was released could not answer
    /// what was held, for how long, or who let it go -- which is exactly what somebody asks when
    /// an order could not be filled.
    /// </remarks>
    /// <param name="documentNo">The document to release.</param>
    /// <param name="documentLineNo">One line of it, or null for all of them.</param>
    /// <param name="reason">Why.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How much was let go.</returns>
    public async Task<decimal> ReleaseAsync(
        string documentNo,
        int? documentLineNo = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<StockReservation>()
            .Where(r => r.DocumentNo == documentNo && r.QuantityOutstanding > 0m);

        if (documentLineNo is { } lineNo)
        {
            query = query.Where(r => r.DocumentLineNo == lineNo);
        }

        var held = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        var released = 0m;

        foreach (var reservation in held)
        {
            released += reservation.QuantityOutstanding;
            reservation.QuantityOutstanding = 0m;
            reservation.ReleaseReason = reason;
        }

        if (held.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Released {Quantity} reserved against {DocumentNo}.",
                released,
                documentNo);
        }

        return released;
    }

    /// <summary>
    /// Takes goods off a document's own reservations as they ship.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the posting engine rather than by whoever ships, so it happens whichever door the
    /// goods leave by. A shipment that consumed nothing would leave the order holding stock it had
    /// already taken, and the same units would look reserved and gone at once.
    /// </para>
    /// <para>
    /// Consuming more than was reserved is not an error. An order may reserve five and ship ten,
    /// and the five it did not reserve were simply never held -- the reservation falls to nought
    /// and the extra comes off free stock like anything else.
    /// </para>
    /// </remarks>
    /// <param name="documentNo">The document the goods left on.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="variantId">The variant, where the item has them.</param>
    /// <param name="locationId">Where they left from.</param>
    /// <param name="quantity">How much went. Positive.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How much of it came off a reservation.</returns>
    public async Task<decimal> ConsumeAsync(
        string documentNo,
        Guid itemId,
        Guid? variantId,
        Guid locationId,
        decimal quantity,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m || string.IsNullOrWhiteSpace(documentNo))
        {
            return 0m;
        }

        var held = await context.Set<StockReservation>()
            .Where(r => r.DocumentNo == documentNo
                && r.ItemId == itemId
                && r.VariantId == variantId
                && r.LocationId == locationId
                && r.QuantityOutstanding > 0m)
            .OrderBy(r => r.DocumentLineNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var remaining = quantity;
        var consumed = 0m;

        foreach (var reservation in held)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var taken = Math.Min(remaining, reservation.QuantityOutstanding);

            reservation.QuantityOutstanding -= taken;
            remaining -= taken;
            consumed += taken;
        }

        return consumed;
    }

    /// <summary>
    /// What is on hand and how much of it is free.
    /// </summary>
    /// <param name="itemNo">One item, or null for all of them.</param>
    /// <param name="locationCode">One location, or null for all of them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per item, variant and location that has anything to say.</returns>
    public async Task<IReadOnlyList<StockAvailabilityView>> AvailabilityAsync(
        string? itemNo = null,
        string? locationCode = null,
        CancellationToken cancellationToken = default)
    {
        var entries = context.Set<ItemLedgerEntry>().AsNoTracking();
        var reservations = context.Set<StockReservation>().AsNoTracking()
            .Where(r => r.QuantityOutstanding > 0m);

        if (!string.IsNullOrWhiteSpace(itemNo))
        {
            entries = entries.Where(e => e.ItemNo == itemNo);
            reservations = reservations.Where(r => r.ItemNo == itemNo);
        }

        if (!string.IsNullOrWhiteSpace(locationCode))
        {
            entries = entries.Where(e => e.LocationCode == locationCode);
            reservations = reservations.Where(r => r.LocationCode == locationCode);
        }

        var onHand = await entries
            .GroupBy(static e => new { e.ItemNo, e.LocationCode, e.VariantCode })
            .Select(static g => new
            {
                g.Key.ItemNo,
                g.Key.LocationCode,
                g.Key.VariantCode,
                Quantity = g.Sum(static e => e.Quantity),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var held = await reservations
            .GroupBy(static r => new { r.ItemNo, r.LocationCode, r.VariantCode })
            .Select(static g => new
            {
                g.Key.ItemNo,
                g.Key.LocationCode,
                g.Key.VariantCode,
                Quantity = g.Sum(static r => r.QuantityOutstanding),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var reserved = held.ToDictionary(
            static h => (h.ItemNo, h.LocationCode, h.VariantCode),
            static h => h.Quantity);

        var rows = onHand
            .Select(o =>
            {
                var spoken = reserved.GetValueOrDefault((o.ItemNo, o.LocationCode, o.VariantCode));

                return new StockAvailabilityView(
                    o.ItemNo,
                    o.LocationCode,
                    o.VariantCode,
                    o.Quantity,
                    spoken,
                    o.Quantity - spoken);
            })
            .ToList();

        // Stock held where the ledger shows nothing left is worth showing rather than hiding: it
        // is a reservation against goods that have gone, and somebody has to know.
        foreach (var stranded in held.Where(h => !onHand.Exists(
            o => o.ItemNo == h.ItemNo && o.LocationCode == h.LocationCode && o.VariantCode == h.VariantCode)))
        {
            rows.Add(new StockAvailabilityView(
                stranded.ItemNo,
                stranded.LocationCode,
                stranded.VariantCode,
                0m,
                stranded.Quantity,
                -stranded.Quantity));
        }

        return
        [
            .. rows
                .Where(static r => r.QuantityOnHand != 0m || r.QuantityReserved != 0m)
                .OrderBy(static r => r.ItemNo, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static r => r.LocationCode, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>What is being held, newest first.</summary>
    /// <param name="documentNo">One document, or null for all of them.</param>
    /// <param name="itemNo">One item, or null for all of them.</param>
    /// <param name="outstandingOnly">Whether to leave out reservations that are spent.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The reservations.</returns>
    public async Task<IReadOnlyList<StockReservation>> ListAsync(
        string? documentNo = null,
        string? itemNo = null,
        bool outstandingOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<StockReservation>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(documentNo))
        {
            query = query.Where(r => r.DocumentNo == documentNo);
        }

        if (!string.IsNullOrWhiteSpace(itemNo))
        {
            query = query.Where(r => r.ItemNo == itemNo);
        }

        if (outstandingOnly)
        {
            query = query.Where(r => r.QuantityOutstanding > 0m);
        }

        return await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// How much of an item at a location is held for anybody other than one document.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="variantId">The variant, where the item has them.</param>
    /// <param name="locationId">Where.</param>
    /// <param name="exceptDocumentNo">The document to leave out, or null to count everything.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The quantity held.</returns>
    public async Task<decimal> ReservedAsync(
        Guid itemId,
        Guid? variantId,
        Guid locationId,
        string? exceptDocumentNo,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<StockReservation>()
            .AsNoTracking()
            .Where(r => r.ItemId == itemId
                && r.VariantId == variantId
                && r.LocationId == locationId
                && r.QuantityOutstanding > 0m);

        if (!string.IsNullOrWhiteSpace(exceptDocumentNo))
        {
            query = query.Where(r => r.DocumentNo != exceptDocumentNo);
        }

        return await query
            .SumAsync(static r => (decimal?)r.QuantityOutstanding, cancellationToken)
            .ConfigureAwait(false) ?? 0m;
    }

    private async Task<decimal> OnHandAsync(
        Guid itemId,
        Guid? variantId,
        Guid locationId,
        CancellationToken cancellationToken)
        => await context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.ItemId == itemId && e.VariantId == variantId && e.LocationId == locationId)
            .SumAsync(static e => (decimal?)e.Quantity, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

    private async Task<Result<Guid?>> ResolveVariantAsync(
        Guid itemId,
        string? variantCode,
        CancellationToken cancellationToken)
    {
        var code = variantCode?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(code))
        {
            return Result<Guid?>.Success(null);
        }

        var variant = await context.Set<ItemVariant>()
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.ItemId == itemId && v.Code == code, cancellationToken)
            .ConfigureAwait(false);

        return variant is null
            ? Result<Guid?>.Failure(messages.Render(
                InventoryMessages.VariantNotFound,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VariantCode"] = code,
                }))
            : Result<Guid?>.Success(variant.Id);
    }
}
