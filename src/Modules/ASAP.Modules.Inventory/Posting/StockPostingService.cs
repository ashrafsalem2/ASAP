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
/// <param name="SalesAmount">What the goods sold for, on an issue.</param>
public sealed record StockMovementRequest(
    string ItemNo,
    string LocationCode,
    decimal Quantity,
    decimal UnitCost = 0m,
    ItemLedgerEntryType EntryType = ItemLedgerEntryType.PositiveAdjustment,
    decimal SalesAmount = 0m);

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
/// <param name="availability">Decides whether stock may move.</param>
/// <param name="events">Gives extensions their say, and announces the result.</param>
/// <param name="messages">Renders messages.</param>
/// <param name="tenantContext">Supplies the company and branch.</param>
/// <param name="userContext">Names who is posting, so an override can be recorded against them.</param>
/// <param name="clock">Supplies the time.</param>
/// <param name="transactionNumbers">Issues the number that groups the entries.</param>
/// <param name="logger">Records postings.</param>
public sealed partial class StockPostingService(
    AsapDbContext context,
    StockAvailability availability,
    IEventPublisher events,
    IMessageCatalog messages,
    ITenantContext tenantContext,
    IUserContext userContext,
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
    public async Task<Result<StockPostingReceipt>> PostAsync(
        IReadOnlyList<StockMovementRequest> requests,
        DateOnly postingDate,
        string sourceCode,
        string? documentNo,
        bool companyAllowsNegative,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var resolved = await ResolveAsync(requests, cancellationToken).ConfigureAwait(false);

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
        RecordOverrides(checkResult, vetoed, documentNo, transactionNo, overrideReason);
        var written = new List<ItemLedgerEntry>();
        var costAmount = 0m;
        var estimatedAmount = 0m;

        // What each movement is worth is carried forward from here rather than read back from the
        // database afterwards. The value entries are still sitting in the change tracker at this
        // point, unsaved, so a query would find nothing and the ledger posting would silently be
        // for zero -- a posting that never happens and never complains.
        var settledByEntry = new List<(ItemLedgerEntry Entry, decimal SettledCost)>();

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
            settledByEntry.Add((outcome.Entry, outcome.CostAmount - outcome.EstimatedCostAmount));
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
            Quantity = request.Quantity,
            DocumentNo = documentNo,
            TransactionNo = transactionNo,
            SourceCode = sourceCode,
            BranchId = tenantContext.BranchId,
        };

        var result = request.Quantity > 0
            ? ReceiveStock(entry, item, request)
            : await IssueStockAsync(entry, item, location, request, cancellationToken).ConfigureAwait(false);

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
        StockMovementRequest request,
        CancellationToken cancellationToken)
    {
        var quantity = -request.Quantity;

        var openLayers = await context.Set<ItemLedgerEntry>()
            .Where(e => e.ItemId == item.Id
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
                Cost = g.Sum(static v => v.CostAmount),
                Quantity = g.Sum(static v => v.Quantity),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return costs.ToDictionary(
            static c => c.EntryId,
            static c => c.Quantity == 0
                ? 0m
                : Math.Round(c.Cost / c.Quantity, 5, MidpointRounding.AwayFromZero));
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

            var onHand = await OnHandAsync(item.Id, location.Id, cancellationToken).ConfigureAwait(false);

            movements.Add(new MovementView(
                index + 1,
                new ItemView(
                    item.No,
                    item.Description,
                    item.Kind,
                    item.CostingMethod,
                    item.IsBlocked,
                    item.AllowNegativeInventory,
                    item.UnitCost > 0 ? item.UnitCost : item.LastDirectCost,
                    item.ReorderPoint),
                new LocationView(location.Code, location.Name, location.IsBlocked, location.IsSellable),
                request.Quantity,
                onHand,
                request.EntryType));
        }

        return missing.Count > 0
            ? Result<List<MovementView>>.Failure(missing)
            : Result<List<MovementView>>.Success(movements);
    }

    /// <summary>
    /// What is on hand for one item at one location.
    /// </summary>
    /// <remarks>
    /// Summed from the ledger rather than read off the item, because the item carries a total
    /// across every location and the question here is about one shelf.
    /// </remarks>
    private async Task<decimal> OnHandAsync(Guid itemId, Guid locationId, CancellationToken cancellationToken)
        => await context.Set<ItemLedgerEntry>()
            .Where(e => e.ItemId == itemId && e.LocationId == locationId)
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
    /// <summary>
    /// Writes an audit row for every protection this posting pushed past.
    /// </summary>
    /// <remarks>
    /// Inventory kept none of these for a while, so a sale could go out below zero from a
    /// location that was not sellable and leave nothing behind saying who allowed it. An override
    /// nobody recorded is indistinguishable from a rule that was never there.
    /// </remarks>
    private void RecordOverrides(
        Result validation,
        Result vetoed,
        string? documentNo,
        long transactionNo,
        string? overrideReason)
    {
        foreach (var warning in validation.Messages.Concat(vetoed.Messages))
        {
            if (!warning.WasOverridden)
            {
                continue;
            }

            context.AuditLog.Add(new AuditLogEntry
            {
                TenantId = tenantContext.TenantId ?? Guid.Empty,
                CompanyId = tenantContext.CompanyId,
                BranchId = tenantContext.BranchId,
                UserId = userContext.UserId,
                UserName = userContext.UserName,
                OccurredAtUtc = clock.UtcNow,
                Action = AuditAction.Override,
                EntityType = "Inventory.ItemLedgerEntry",
                DisplayNo = documentNo ?? transactionNo.ToString(CultureInfo.InvariantCulture),
                OverriddenMessageCode = warning.Code.Value,
                OverrideReason = overrideReason,
                Changes = warning.Detail,
            });
        }
    }

    private Task<long> NextTransactionNoAsync(CancellationToken cancellationToken)
        => transactionNumbers.NextAsync(cancellationToken);
}
