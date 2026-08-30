using System.Globalization;
using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Events;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Kernel.Events;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Inventory.Posting;

/// <summary>One movement to post.</summary>
/// <param name="ItemNo">The item moving.</param>
/// <param name="LocationCode">Where it is moving.</param>
/// <param name="Quantity">Signed. Positive receives stock, negative issues it.</param>
/// <param name="UnitCost">
/// What it cost per unit. Read only on a receipt; an issue works its own cost out from what is on
/// hand, which is the entire point of the costing engine.
/// </param>
/// <param name="EntryType">What caused the movement.</param>
/// <param name="ContraAccountNo">
/// What the value posts against, when the caller knows better than the item category does.
/// The entry type says what kind of movement it is; the counterparty depends on the document
/// behind it, and only the module owning that document knows it. A purchase receipt posts
/// against goods-received-not-invoiced, which Inventory has no business knowing about.
/// </param>
/// <param name="SalesAmount">What the goods sold for, on an issue.</param>
/// <param name="BinCode">
/// Which bin inside the location, where the location tracks them. Null lets a receipt fall to the
/// location's receiving bin; an issue with no bin at a bin-tracked location is refused, because
/// guessing which shelf it came off would make the bins wrong from that moment on.
/// </param>
/// <param name="ReasonCode">
/// Why, on an adjustment. The reason carries the account the loss lands in, so the person at the
/// shelf says "breakage" without having to know which account that is.
/// </param>
/// <param name="Note">What the person adjusting wrote, where they wrote anything.</param>
/// <param name="VariantCode">
/// Which colour, size or flavour, on an item that has them. Required there and refused elsewhere:
/// a variant splits the stock and the cost layers, so a movement that did not say which one would
/// have to be guessed at, and a guess here costs a blue shirt against a red receipt.
/// </param>
public sealed record StockMovementRequest(
    string ItemNo,
    string LocationCode,
    decimal Quantity,
    decimal UnitCost = 0m,
    ItemLedgerEntryType EntryType = ItemLedgerEntryType.PositiveAdjustment,
    decimal SalesAmount = 0m,
    string? ContraAccountNo = null,
    string? BinCode = null,
    string? ReasonCode = null,
    string? Note = null,
    string? VariantCode = null);

/// <summary>What a stock posting produced.</summary>
/// <param name="TransactionNo">The number grouping every entry written.</param>
/// <param name="EntryCount">How many item ledger entries were written.</param>
/// <param name="CostAmount">The total change in the value of stock.</param>
/// <param name="EstimatedCostAmount">How much of that is an estimate awaiting settlement.</param>
public readonly record struct StockPostingReceipt(
    long TransactionNo,
    int EntryCount,
    decimal CostAmount,
    decimal EstimatedCostAmount);

/// <summary>
/// Moves stock and records what it was worth.
/// </summary>
/// <remarks>
/// The order of work mirrors the general ledger poster: check first, because a refusal should cost
/// nothing; give extensions their say on movements already known to be sound; then write, with
/// everything after that point inside one transaction.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="branches">Says which branch a location belongs to.</param>
/// <param name="availability">Decides whether stock may move.</param>
/// <param name="events">Gives extensions their say, and announces the result.</param>
/// <param name="messages">Renders messages.</param>
/// <param name="tenantContext">Supplies the company and branch.</param>
/// <param name="overrides">Records every protection this posting pushed past.</param>
/// <param name="clock">Supplies the time.</param>
/// <param name="transactionNumbers">Issues the number that groups the entries.</param>
/// <param name="logger">Records postings.</param>
public sealed partial class StockPostingService(
    AsapDbContext context,
    StockAvailability availability,
    Locations.LocationBranchLookup branches,
    IEventPublisher events,
    IMessageCatalog messages,
    ITenantContext tenantContext,
    OverrideAuditor overrides,
    IClock clock,
    ITransactionNumberAllocator transactionNumbers,
    ILogger<StockPostingService> logger)
{
    /// <summary>
    /// Posts a set of stock movements.
    /// </summary>
    /// <param name="requests">The movements.</param>
    /// <param name="postingDate">The date to report them in.</param>
    /// <param name="sourceCode">Where they came from, for example <c>POS</c>.</param>
    /// <param name="documentNo">The document behind them.</param>
    /// <param name="companyAllowsNegative">Whether the company permits stock below zero.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection was pushed past, recorded with the override.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="reasonRequired">
    /// Whether an adjustment has to say why. A company setting rather than a rule, because a
    /// corner shop writing off a broken bottle should not have to maintain a code list, and a
    /// chain that cannot say what its shrinkage was made of should.
    /// </param>
    public async Task<Result<StockPostingReceipt>> PostAsync(
        IReadOnlyList<StockMovementRequest> requests,
        DateOnly postingDate,
        string sourceCode,
        string? documentNo,
        bool companyAllowsNegative,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default,
        bool reasonRequired = false)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var resolved = await ResolveAsync(requests, reasonRequired, cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return Result<StockPostingReceipt>.FailureFrom(resolved);
        }

        var movements = resolved.Value;

        var checkResult = availability.Check(movements, companyAllowsNegative, heldOverridePermissions);

        if (checkResult.Failed)
        {
            return Result<StockPostingReceipt>.FailureFrom(checkResult);
        }

        var posting = new StockPosting
        {
            Movements = movements,
            DocumentNo = documentNo,
            PostingDate = postingDate,
        };

        var vetoed = await events.PublishVetoableAsync(posting, cancellationToken).ConfigureAwait(false);

        if (vetoed.Failed)
        {
            return Result<StockPostingReceipt>.FailureFrom(vetoed);
        }

        var transactionNo = await NextTransactionNoAsync(cancellationToken).ConfigureAwait(false);

        // Anything that reached here carrying an overridden block was a refusal a moment ago.
        // Recorded against the transaction number so the trail and the entries point at each
        // other; both live or die with the same transaction.
        overrides.Record(
            [.. checkResult.Messages, .. vetoed.Messages],
            "Inventory.ItemLedgerEntry",
            documentNo ?? transactionNo.ToString(CultureInfo.InvariantCulture),
            overrideReason);
        var written = new List<ItemLedgerEntry>();
        var costAmount = 0m;
        var estimatedAmount = 0m;

        // What each movement is worth is carried forward from here rather than read back from the
        // database afterwards. The value entries are still sitting in the change tracker at this
        // point, unsaved, so a query would find nothing and the ledger posting would silently be
        // for zero -- a posting that never happens and never complains.
        var settledByEntry = new List<(ItemLedgerEntry Entry, decimal SettledCost, string? ContraAccountNo)>();

        foreach (var (request, movement) in requests.Zip(movements))
        {
            var outcome = await WriteMovementAsync(
                    request,
                    movement,
                    postingDate,
                    sourceCode,
                    documentNo,
                    transactionNo,
                    cancellationToken)
                .ConfigureAwait(false);

            written.Add(outcome.Entry);
            costAmount += outcome.CostAmount;
            estimatedAmount += outcome.EstimatedCostAmount;

            // Estimated cost is excluded: a figure nobody has confirmed must not reach the
            // inventory account, or the ledger drifts from the valuation by the amount in doubt.
            settledByEntry.Add((
                outcome.Entry,
                outcome.CostAmount - outcome.EstimatedCostAmount,
                request.ContraAccountNo ?? movement.ContraAccountNo));
        }

        await RequestLedgerPostingAsync(
                settledByEntry,
                postingDate,
                sourceCode,
                documentNo,
                transactionNo,
                cancellationToken)
            .ConfigureAwait(false);

        events.Enqueue(new StockPosted
        {
            OccurredAtUtc = clock.UtcNow,
            TransactionNo = transactionNo,
            DocumentNo = documentNo,
            PostingDate = postingDate,
            EntryCount = written.Count,
            CostAmount = costAmount,
            EstimatedCostAmount = estimatedAmount,
            SourceCode = sourceCode,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Posted {Count} stock movement(s) as transaction {TransactionNo}, cost {Cost}, of which "
            + "{Estimated} is estimated.",
            written.Count,
            transactionNo,
            costAmount,
            estimatedAmount);

        return Result<StockPostingReceipt>.Success(
            new StockPostingReceipt(transactionNo, written.Count, costAmount, estimatedAmount),
            checkResult.Messages);
    }

    private async Task<(ItemLedgerEntry Entry, decimal CostAmount, decimal EstimatedCostAmount)>
        WriteMovementAsync(
            StockMovementRequest request,
            MovementView movement,
            DateOnly postingDate,
            string sourceCode,
            string? documentNo,
            long transactionNo,
            CancellationToken cancellationToken)
    {
        var item = await context.Set<Item>()
            .FirstAsync(i => i.No == request.ItemNo, cancellationToken)
            .ConfigureAwait(false);

        var location = await context.Set<Location>()
            .FirstAsync(l => l.Code == request.LocationCode, cancellationToken)
            .ConfigureAwait(false);

        var entry = new ItemLedgerEntry
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.RequireCompanyId(),
            ItemId = item.Id,
            ItemNo = item.No,
            EntryType = request.EntryType,
            PostingDate = postingDate,
            LocationId = location.Id,
            LocationCode = location.Code,

            // Copied off the resolved movement rather than the request, because a receipt with no
            // bin named lands in the receiving bay and the entry has to say so.
            BinId = movement.Bin?.Id,
            BinCode = movement.Bin?.Code,
            VariantId = movement.VariantId,
            VariantCode = movement.VariantCode,
            ReasonCode = request.ReasonCode?.Trim().ToUpperInvariant(),
            Note = request.Note?.Trim(),
            Quantity = request.Quantity,
            DocumentNo = documentNo,
            TransactionNo = transactionNo,
            SourceCode = sourceCode,
            BranchId = tenantContext.BranchId,
        };

        var result = request.Quantity > 0
            ? ReceiveStock(entry, item, request)
            : await IssueStockAsync(entry, item, location, movement, request, cancellationToken).ConfigureAwait(false);

        // What this variant last cost, kept beside the item's own figure. The item's becomes
        // whichever variant arrived most recently once variants are in play, which makes it a poor
        // thing to estimate an unreceived colour at.
        if (request.Quantity > 0m && movement.VariantId is { } receivedVariantId)
        {
            var variant = await context.Set<ItemVariant>()
                .FirstOrDefaultAsync(v => v.Id == receivedVariantId, cancellationToken)
                .ConfigureAwait(false);

            if (variant is not null)
            {
                variant.LastDirectCost = request.UnitCost > 0m ? request.UnitCost : item.UnitCost;
            }
        }

        context.Set<ItemLedgerEntry>().Add(entry);

        item.QuantityOnHand += request.Quantity;
        item.HasLedgerEntries = true;

        return (entry, result.CostAmount, result.EstimatedCostAmount);
    }

    /// <summary>
    /// Records a receipt: its whole quantity is available to be drawn from.
    /// </summary>
    private (decimal CostAmount, decimal EstimatedCostAmount) ReceiveStock(
        ItemLedgerEntry entry,
        Item item,
        StockMovementRequest request)
    {
        entry.RemainingQuantity = request.Quantity;

        var unitCost = request.UnitCost > 0 ? request.UnitCost : item.UnitCost;
        var costAmount = Math.Round(request.Quantity * unitCost, 2, MidpointRounding.AwayFromZero);

        context.Set<ValueEntry>().Add(NewValueEntry(
            entry,
            item,
            ValueEntryType.DirectCost,
            request.Quantity,
            unitCost,
            costAmount,
            isExpected: false));

        // A receipt is what the item last actually cost, and on an average-costed item it moves
        // the running unit cost. Both are read later to value an issue that has nothing to draw on.
        item.LastDirectCost = unitCost;
        item.UnitCost = unitCost;

        return (costAmount, 0m);
    }

    /// <summary>
    /// Records an issue, working its cost out from what is on hand.
    /// </summary>
    /// <remarks>
    /// The applications written here are the record of which receipt covered which issue. An issue
    /// that ran ahead of its receipt gets an application with no inbound entry, marked outstanding,
    /// and that row is the work list the settlement routine comes back to.
    /// </remarks>
    private async Task<(decimal CostAmount, decimal EstimatedCostAmount)> IssueStockAsync(
        ItemLedgerEntry entry,
        Item item,
        Location location,
        MovementView movement,
        StockMovementRequest request,
        CancellationToken cancellationToken)
    {
        var quantity = -request.Quantity;

        // Item, variant and location together. Dropping the variant here would cost a blue shirt
        // out of a red receipt without failing, and the only symptom would be a margin quietly
        // wrong on both.
        var openLayers = await context.Set<ItemLedgerEntry>()
            .Where(e => e.ItemId == item.Id
                        && e.VariantId == movement.VariantId
                        && e.LocationId == location.Id
                        && e.RemainingQuantity > 0)
            .OrderBy(e => e.PostingDate)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var layerCosts = await LayerUnitCostsAsync(openLayers, cancellationToken).ConfigureAwait(false);

        var layers = openLayers
            .Select(l => new InboundLayer(l.Id, l.PostingDate, l.RemainingQuantity, layerCosts[l.Id]))
            .ToList();

        var outcome = CostingEngine.ApplyOutbound(
            quantity,
            layers,
            item.CostingMethod,
            item.UnitCost > 0 ? item.UnitCost : item.LastDirectCost,
            item.StandardCost);

        entry.RemainingQuantity = 0;
        entry.IsApplied = !outcome.WentNegative;
        entry.WentNegative = outcome.WentNegative;

        foreach (var application in outcome.Applications)
        {
            if (application.InboundEntryId is { } inboundId)
            {
                openLayers.First(l => l.Id == inboundId).RemainingQuantity -= application.Quantity;
            }

            context.Set<ItemApplicationEntry>().Add(new ItemApplicationEntry
            {
                TenantId = entry.TenantId,
                CompanyId = entry.CompanyId,
                ItemId = item.Id,

                // Carried onto the application so the settlement routine can find a receipt of
                // the same variant without a join it might one day drop.
                VariantId = movement.VariantId,
                OutboundEntryId = entry.Id,
                InboundEntryId = application.InboundEntryId,
                Quantity = application.Quantity,
                PostingDate = entry.PostingDate,
                IsOutstanding = application.IsEstimate,
            });

            context.Set<ValueEntry>().Add(NewValueEntry(
                entry,
                item,
                ValueEntryType.DirectCost,
                -application.Quantity,
                application.UnitCost,
                -application.CostAmount,
                isExpected: application.IsEstimate,
                salesAmount: request.SalesAmount));
        }

        return (outcome.TotalCost, -outcome.EstimatedCost);
    }

    /// <summary>
    /// Reads what each open receipt cost per unit.
    /// </summary>
    /// <remarks>
    /// Taken from the value entries rather than from the item, because the item carries one current
    /// cost while the layers each carry the cost they were received at -- which is the whole reason
    /// FIFO produces a different answer from average.
    /// </remarks>
    /// <summary>
    /// What each open receipt cost per unit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two kinds of value entry, added together. The ordinary ones carry both a cost and the
    /// quantity it covers, and their unit cost is the one divided by the other -- what the goods
    /// were bought for, freight included.
    /// </para>
    /// <para>
    /// A revaluation carries a cost and no quantity, because nothing moved. Its own
    /// <see cref="ValueEntry.UnitCost"/> is the amount each remaining unit was written up or down
    /// by, and it is added rather than averaged in. That distinction is the whole reason a
    /// revaluation survives: averaging it over the receipt's original quantity would spread a
    /// write-down across units that were sold months ago, and the layer would drift back towards
    /// its old cost as it drained -- a revaluation that quietly undoes itself as the stock sells.
    /// </para>
    /// </remarks>
    private async Task<Dictionary<Guid, decimal>> LayerUnitCostsAsync(
        List<ItemLedgerEntry> layers,
        CancellationToken cancellationToken)
    {
        if (layers.Count == 0)
        {
            return [];
        }

        var ids = layers.Select(static l => l.Id).ToList();

        var costs = await context.Set<ValueEntry>()
            .Where(v => ids.Contains(v.ItemLedgerEntryId))
            .GroupBy(static v => v.ItemLedgerEntryId)
            .Select(static g => new
            {
                EntryId = g.Key,
                Cost = g.Where(static v => v.Quantity != 0m).Sum(static v => v.CostAmount),
                Quantity = g.Sum(static v => v.Quantity),
                Revalued = g.Where(static v => v.Quantity == 0m).Sum(static v => v.UnitCost),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return costs.ToDictionary(
            static c => c.EntryId,
            static c => c.Quantity == 0
                ? Math.Round(c.Revalued, 5, MidpointRounding.AwayFromZero)
                : Math.Round((c.Cost / c.Quantity) + c.Revalued, 5, MidpointRounding.AwayFromZero));
    }

    private ValueEntry NewValueEntry(
        ItemLedgerEntry entry,
        Item item,
        ValueEntryType type,
        decimal quantity,
        decimal unitCost,
        decimal costAmount,
        bool isExpected,
        decimal salesAmount = 0m)
        => new()
        {
            TenantId = entry.TenantId,
            CompanyId = entry.CompanyId,
            ItemLedgerEntryId = entry.Id,
            ItemId = item.Id,
            ItemNo = item.No,
            EntryType = type,
            ItemLedgerEntryType = entry.EntryType,
            PostingDate = entry.PostingDate,
            Quantity = quantity,
            CostAmount = costAmount,
            UnitCost = unitCost,
            SalesAmount = salesAmount,
            IsExpected = isExpected,
            DocumentNo = entry.DocumentNo,
            TransactionNo = entry.TransactionNo,
            SourceCode = entry.SourceCode,
            BranchId = entry.BranchId,
        };

    /// <summary>
    /// Turns item and location codes into the views the rules work over.
    /// </summary>
    /// <remarks>
    /// A code that matches nothing produces a failure naming it rather than an exception, so a
    /// batch with one bad line still reports everything else wrong with it.
    /// </remarks>
    private async Task<Result<List<MovementView>>> ResolveAsync(
        IReadOnlyList<StockMovementRequest> requests,
        bool reasonRequired,
        CancellationToken cancellationToken)
    {
        var itemNos = requests.Select(static r => r.ItemNo).Distinct().ToList();
        var locationCodes = requests.Select(static r => r.LocationCode).Distinct().ToList();

        var items = await context.Set<Item>()
            .Where(i => itemNos.Contains(i.No))
            .ToDictionaryAsync(static i => i.No, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var locations = await context.Set<Location>()
            .Where(l => locationCodes.Contains(l.Code))
            .ToDictionaryAsync(static l => l.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        // Every bin at every location on this posting, loaded once. A warehouse posting is the
        // one place a query per line is felt, and a put-away sheet is a hundred lines.
        var locationIds = locations.Values.Select(static l => l.Id).ToList();

        var bins = await context.Set<Locations.Bin>()
            .AsNoTracking()
            .Where(b => locationIds.Contains(b.LocationId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Every variant of every item on this posting, loaded once. A goods receipt for a
        // clothing line is fifty lines of the same item in different sizes.
        var itemIds = items.Values.Select(static i => i.Id).ToList();

        var variants = await context.Set<ItemVariant>()
            .AsNoTracking()
            .Where(v => itemIds.Contains(v.ItemId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var reasonCodes = requests
            .Select(static r => r.ReasonCode)
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var reasons = reasonCodes.Count == 0
            ? []
            : await context.Set<Adjustments.AdjustmentReason>()
                .AsNoTracking()
                .Where(r => reasonCodes.Contains(r.Code))
                .ToDictionaryAsync(static r => r.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);

        var missing = new List<AsapMessage>();
        var movements = new List<MovementView>();

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];

            // Not found and blocked are different problems with different answers. Reporting a
            // typo as "withdrawn from use" sends the user to an administrator to unblock
            // something that was never there.
            if (!items.TryGetValue(request.ItemNo, out var item))
            {
                missing.Add(messages.Render(
                    InventoryMessages.ItemNotFound,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ItemNo"] = request.ItemNo,
                    },
                    MessageTarget.OnField($"Lines[{index + 1}]")));

                continue;
            }

            if (!locations.TryGetValue(request.LocationCode, out var location))
            {
                missing.Add(messages.Render(
                    InventoryMessages.LocationNotFound,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Location"] = request.LocationCode,
                    },
                    MessageTarget.OnField($"Lines[{index + 1}]")));

                continue;
            }

            var binResult = ResolveBin(request, location, bins, index + 1);

            if (binResult.Refusal is { } binRefusal)
            {
                missing.Add(binRefusal);
                continue;
            }

            var variantResult = ResolveVariant(request, item, variants, index + 1);

            if (variantResult.Refusal is { } variantRefusal)
            {
                missing.Add(variantRefusal);
                continue;
            }

            var reasonRefusal = CheckReason(request, reasons, reasonRequired, index + 1);

            if (reasonRefusal is not null)
            {
                missing.Add(reasonRefusal);
                continue;
            }

            var onHand = await OnHandAsync(item.Id, variantResult.VariantId, location.Id, cancellationToken)
                .ConfigureAwait(false);

            var inBin = binResult.Bin is null
                ? 0m
                : await BinOnHandAsync(item.Id, variantResult.VariantId, binResult.Bin.Id, cancellationToken)
                    .ConfigureAwait(false);

            // Only asked when the shelf is actually short. "Where is it then" is a query, and
            // running it on every line of a put-away sheet to answer a question nobody asked
            // would make the ordinary case pay for the exception.
            var elsewhere = binResult.Bin is not null && request.Quantity < 0m
                && inBin + request.Quantity < 0m
                ? await BinsHoldingAsync(
                        item.Id, variantResult.VariantId, location.Id, binResult.Bin.Id, cancellationToken)
                    .ConfigureAwait(false)
                : [];

            movements.Add(new MovementView(
                index + 1,
                new ItemView(
                    item.No,
                    item.Description,
                    item.Kind,
                    item.CostingMethod,
                    item.IsBlocked,
                    item.AllowNegativeInventory,
                    variantResult.LastDirectCost > 0m
                        ? variantResult.LastDirectCost
                        : item.UnitCost > 0 ? item.UnitCost : item.LastDirectCost,
                    item.ReorderPoint),
                new LocationView(location.Code, location.Name, location.IsBlocked, location.IsSellable),
                request.Quantity,
                onHand,
                request.EntryType)
            {
                Bin = binResult.Bin,
                BinQuantityOnHand = inBin,
                BinsHoldingIt = elsewhere,
                ContraAccountNo = ContraFor(request, reasons),
                VariantId = variantResult.VariantId,
                VariantCode = variantResult.VariantCode,
                VariantUnitCost = variantResult.LastDirectCost,
            });
        }

        return missing.Count > 0
            ? Result<List<MovementView>>.Failure(missing)
            : Result<List<MovementView>>.Success(movements);
    }

    /// <summary>What a line's variant came to, or why the line cannot stand.</summary>
    private readonly record struct ResolvedVariant(
        Guid? VariantId,
        string? VariantCode,
        decimal LastDirectCost,
        AsapMessage? Refusal);

    /// <summary>
    /// Works out which variant a line moves, and whether it may.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both directions refused, like bins: an item with variants takes nothing else, and one
    /// without takes no variant at all. Unlike bins there is no softening and no default, because
    /// a variant is not a refinement of the stock but a partition of it. Falling back to "no
    /// variant" on an item that has them would open a phantom stock line that no shelf
    /// corresponds to, and the first sale out of it would cost against nothing.
    /// </para>
    /// <para>
    /// Blocked stops goods arriving, not leaving, for the same reason a blocked bin does: what is
    /// already in stock under a withdrawn variant is still on the shelf and still has to be
    /// sellable.
    /// </para>
    /// </remarks>
    private ResolvedVariant ResolveVariant(
        StockMovementRequest request,
        Item item,
        List<ItemVariant> variants,
        int lineNo)
    {
        var target = MessageTarget.OnField($"Lines[{lineNo}]");
        var wanted = request.VariantCode?.Trim();

        if (!item.HasVariants)
        {
            return string.IsNullOrEmpty(wanted)
                ? new ResolvedVariant(null, null, 0m, null)
                : new ResolvedVariant(null, null, 0m, messages.Render(
                    InventoryMessages.VariantNotUsedHere,
                    Args(("LineNo", lineNo), ("VariantCode", wanted), ("ItemNo", item.No)),
                    target));
        }

        if (string.IsNullOrEmpty(wanted))
        {
            return new ResolvedVariant(null, null, 0m, messages.Render(
                InventoryMessages.VariantRequired,
                Args(("LineNo", lineNo), ("ItemNo", item.No)),
                target));
        }

        var variant = variants.Find(v =>
            v.ItemId == item.Id && string.Equals(v.Code, wanted, StringComparison.OrdinalIgnoreCase));

        if (variant is null)
        {
            return new ResolvedVariant(null, null, 0m, messages.Render(
                InventoryMessages.VariantNotFound,
                Args(("LineNo", lineNo), ("VariantCode", wanted), ("ItemNo", item.No)),
                target));
        }

        return variant.IsBlocked && request.Quantity > 0m
            ? new ResolvedVariant(null, null, 0m, messages.Render(
                InventoryMessages.VariantBlocked,
                Args(("LineNo", lineNo), ("VariantCode", variant.Code), ("ItemNo", item.No)),
                target))
            : new ResolvedVariant(variant.Id, variant.Code, variant.LastDirectCost, null);
    }

    /// <summary>Where an adjustment's value posts against, when its reason names an account.</summary>
    private static string? ContraFor(
        StockMovementRequest request,
        IReadOnlyDictionary<string, Adjustments.AdjustmentReason> reasons)
    {
        var code = request.ReasonCode?.Trim();

        return code is { Length: > 0 } named
            && reasons.TryGetValue(named, out var reason)
            && reason.ContraAccountNo is { Length: > 0 } account
                ? account
                : null;
    }

    /// <summary>
    /// Says why an adjustment's reason will not do, when it will not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only adjustments are asked. A sale, a purchase and a transfer already say why they happened
    /// -- the document behind them is the reason -- and demanding a code as well would be asking
    /// the same question twice.
    /// </para>
    /// <para>
    /// The direction check is the one that earns its place. Breakage cannot increase stock and
    /// goods found cannot decrease it, and a reason used the wrong way round produces an entry
    /// that looks perfectly valid in every report that reads it.
    /// </para>
    /// </remarks>
    private AsapMessage? CheckReason(
        StockMovementRequest request,
        IReadOnlyDictionary<string, Adjustments.AdjustmentReason> reasons,
        bool reasonRequired,
        int lineNo)
    {
        var isAdjustment = request.EntryType
            is ItemLedgerEntryType.PositiveAdjustment or ItemLedgerEntryType.NegativeAdjustment;

        if (!isAdjustment)
        {
            return null;
        }

        var target = MessageTarget.OnField($"Lines[{lineNo}]");
        var wanted = request.ReasonCode?.Trim();

        if (string.IsNullOrEmpty(wanted))
        {
            return reasonRequired
                ? messages.Render(
                    InventoryMessages.ReasonRequired,
                    Args(("LineNo", lineNo), ("ItemNo", request.ItemNo), ("Quantity", request.Quantity)),
                    target)
                : null;
        }

        if (!reasons.TryGetValue(wanted, out var reason))
        {
            return messages.Render(
                InventoryMessages.ReasonNotFound,
                Args(("LineNo", lineNo), ("ReasonCode", wanted)),
                target);
        }

        if (!reason.IsActive)
        {
            return messages.Render(
                InventoryMessages.ReasonNotInUse,
                Args(("LineNo", lineNo), ("ReasonCode", reason.Code)),
                target);
        }

        if (!reason.Permits(request.Quantity))
        {
            return messages.Render(
                InventoryMessages.ReasonWrongDirection,
                Args(
                    ("LineNo", lineNo),
                    ("ReasonCode", reason.Code),
                    ("ReasonName", reason.Name),
                    ("Quantity", Math.Abs(request.Quantity)),
                    ("Direction", reason.Direction is Adjustments.AdjustmentDirection.IncreaseOnly ? "up" : "down"),
                    ("Actual", request.Quantity > 0m ? "up" : "down")),
                target);
        }

        return reason.RequiresNote && string.IsNullOrWhiteSpace(request.Note)
            ? messages.Render(
                InventoryMessages.ReasonNeedsANote,
                Args(("LineNo", lineNo), ("ReasonCode", reason.Code), ("ReasonName", reason.Name)),
                target)
            : null;
    }

    /// <summary>What a line's bin came to, or why the line cannot stand.</summary>
    private readonly record struct ResolvedBin(Locations.Bin? Bin, AsapMessage? Refusal);

    /// <summary>
    /// Works out which bin a line moves at, and whether it may.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A location that does not track bins takes no bin at all, and one that does takes nothing
    /// else. Both directions are refused rather than shrugged at: a bin recorded where nothing
    /// reads it looks like tracking that never happened, and a movement with no bin at a tracked
    /// location leaves the shelves holding a picture that is wrong from that line onwards.
    /// </para>
    /// <para>
    /// The one softening is the receiving bin, and only for goods coming in. Something arriving
    /// with nowhere named has physically arrived somewhere, and the receiving bay is that
    /// somewhere. Goods going out get no such default: guessing which shelf they came off is how
    /// a bin ends up holding stock nobody can find.
    /// </para>
    /// </remarks>
    private ResolvedBin ResolveBin(
        StockMovementRequest request,
        Location location,
        List<Locations.Bin> bins,
        int lineNo)
    {
        var target = MessageTarget.OnField($"Lines[{lineNo}]");
        var wanted = request.BinCode?.Trim();

        if (!location.UsesBins)
        {
            return string.IsNullOrEmpty(wanted)
                ? new ResolvedBin(null, null)
                : new ResolvedBin(null, messages.Render(
                    InventoryMessages.BinNotUsedHere,
                    Args(("LineNo", lineNo), ("BinCode", wanted), ("Location", location.Code)),
                    target));
        }

        if (string.IsNullOrEmpty(wanted))
        {
            var receiving = bins.Find(b =>
                b.LocationId == location.Id && b.IsReceiving && !b.IsBlocked);

            // Only for goods coming in. What goes out came off a particular shelf, and the system
            // does not know which one.
            if (request.Quantity > 0m && receiving is not null)
            {
                return new ResolvedBin(receiving, null);
            }

            return new ResolvedBin(null, messages.Render(
                InventoryMessages.BinRequired,
                Args(("LineNo", lineNo), ("Location", location.Code)),
                target));
        }

        var bin = bins.Find(b =>
            b.LocationId == location.Id
            && string.Equals(b.Code, wanted, StringComparison.OrdinalIgnoreCase));

        if (bin is null)
        {
            return new ResolvedBin(null, messages.Render(
                InventoryMessages.BinNotFound,
                Args(("BinCode", wanted), ("Location", location.Code)),
                target));
        }

        // Blocked only stops goods arriving. What is already in a blocked bin is still physically
        // there, and refusing to take it out would strand it until somebody unblocked a shelf
        // that is out of use precisely because nothing should be added to it.
        return bin.IsBlocked && request.Quantity > 0m
            ? new ResolvedBin(null, messages.Render(
                InventoryMessages.BinBlocked,
                Args(("BinCode", bin.Code), ("Location", location.Code)),
                target))
            : new ResolvedBin(bin, null);
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

    /// <summary>
    /// Which other bins at this location are holding the item, and how many.
    /// </summary>
    /// <remarks>
    /// In pick order, because the answer is going to send somebody walking and the shortest walk
    /// is a fact about the floor plan rather than about how the shelves are named.
    /// </remarks>
    private async Task<IReadOnlyList<string>> BinsHoldingAsync(
        Guid itemId,
        Guid? variantId,
        Guid locationId,
        Guid exceptBinId,
        CancellationToken cancellationToken)
    {
        var held = await context.Set<ItemLedgerEntry>()
            .Where(e => e.ItemId == itemId && e.VariantId == variantId
                && e.LocationId == locationId && e.BinId != null
                && e.BinId != exceptBinId)
            .GroupBy(static e => e.BinId)
            .Select(static g => new { BinId = g.Key, Quantity = g.Sum(static e => e.Quantity) })
            .Where(static g => g.Quantity > 0m)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (held.Count == 0)
        {
            return [];
        }

        var ids = held.Select(static h => h.BinId!.Value).ToList();

        var order = await context.Set<Locations.Bin>()
            .AsNoTracking()
            .Where(b => ids.Contains(b.Id))
            .Select(static b => new { b.Id, b.Code, b.PickOrder })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. held
                .Join(order, static h => h.BinId!.Value, static o => o.Id, static (h, o) => new
                {
                    o.Code,
                    o.PickOrder,
                    h.Quantity,
                })
                .OrderBy(static x => x.PickOrder)
                .ThenBy(static x => x.Code, StringComparer.OrdinalIgnoreCase)
                .Select(static x => $"{x.Code} ({x.Quantity:0.#####})"),
        ];
    }

    /// <summary>
    /// What one bin holds of one item.
    /// </summary>
    /// <remarks>
    /// Summed from the same ledger the location total comes from, so the bins add up to the
    /// location by construction rather than by a reconciliation somebody has to run.
    /// </remarks>
    private async Task<decimal> BinOnHandAsync(
        Guid itemId,
        Guid? variantId,
        Guid binId,
        CancellationToken cancellationToken)
        => await context.Set<ItemLedgerEntry>()
            .Where(e => e.ItemId == itemId && e.VariantId == variantId && e.BinId == binId)
            .SumAsync(static e => (decimal?)e.Quantity, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

    /// <summary>
    /// What is on hand for one item at one location.
    /// </summary>
    /// <remarks>
    /// Summed from the ledger rather than read off the item, because the item carries a total
    /// across every location and the question here is about one shelf.
    /// </remarks>
    private async Task<decimal> OnHandAsync(
        Guid itemId,
        Guid? variantId,
        Guid locationId,
        CancellationToken cancellationToken)
        => await context.Set<ItemLedgerEntry>()
            .Where(e => e.ItemId == itemId && e.VariantId == variantId && e.LocationId == locationId)
            .SumAsync(static e => (decimal?)e.Quantity, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

    /// <summary>
    /// Takes the next transaction number from the platform allocator.
    /// </summary>
    /// <remarks>
    /// Inventory shares the company counter with Finance so that a stock movement and the ledger
    /// entries it causes belong to one transaction. The counter lives in the platform rather than
    /// in Finance, because reaching into another module table would mean Inventory could not run
    /// without it.
    /// </remarks>
    private Task<long> NextTransactionNoAsync(CancellationToken cancellationToken)
        => transactionNumbers.NextAsync(cancellationToken);
}
