using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Inventory.Counting;

/// <summary>
/// Physical stock counts: what is on the shelves, against what the system believed.
/// </summary>
/// <remarks>
/// <para>
/// Everything else in this module records what somebody said happened. A count is the only thing
/// that goes and looks. That makes it the only check on all the rest, and the only place a
/// shortfall the system never saw — theft, breakage, a delivery signed for and never put away —
/// becomes a number anybody can act on.
/// </para>
/// <para>
/// The sheet freezes what the system said at the moment it was made. Comparing against a live
/// figure would turn every sale rung up while somebody walks the aisles into a discrepancy nobody
/// can explain, and a count nobody trusts is worse than no count at all.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="stock">Posts the adjustments the differences come to.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="numbers">Issues count numbers.</param>
/// <param name="setup">Supplies the number series and whether negative stock is allowed.</param>
/// <param name="userContext">Records who posted it.</param>
/// <param name="clock">Supplies the time.</param>
/// <param name="logger">Records counts posted.</param>
public sealed class StockCountService(
    AsapDbContext context,
    StockPostingService stock,
    IMessageCatalog messages,
    INumberSeriesService numbers,
    ISetupService setup,
    IUserContext userContext,
    IClock clock,
    ILogger<StockCountService> logger)
{
    /// <summary>Lists counts, most recent first.</summary>
    /// <param name="locationCode">One location, or null for all.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The counts, with their lines.</returns>
    public Task<List<StockCount>> ListAsync(
        string? locationCode = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<StockCount> query = context.Set<StockCount>().AsNoTracking().Include(c => c.Lines);

        if (!string.IsNullOrWhiteSpace(locationCode))
        {
            query = query.Where(c => c.LocationCode == locationCode);
        }

        return query
            .OrderByDescending(static c => c.CountDate)
            .ThenByDescending(static c => c.No)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reads one count and its sheet.</summary>
    /// <param name="countNo">Its number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The count, or null when nothing is numbered that.</returns>
    public Task<StockCount?> LoadAsync(string countNo, CancellationToken cancellationToken = default)
        => context.Set<StockCount>()
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.No == countNo, cancellationToken);

    /// <summary>
    /// Starts a count, and makes the sheet.
    /// </summary>
    /// <remarks>
    /// Every item the location has ever held goes on the sheet, including the ones the system
    /// says are at nothing. A sheet that listed only what the system expects to find cannot
    /// discover the box of stock nobody ever booked in, which is one of the two things a count
    /// exists to find.
    /// </remarks>
    /// <param name="locationCode">The location to count.</param>
    /// <param name="countDate">The day to report it on, or null for today.</param>
    /// <param name="description">What the count is for.</param>
    /// <param name="itemNos">Specific items, or null for everything the location has held.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The count with its sheet, or why it could not be started.</returns>
    public async Task<Result<StockCount>> StartAsync(
        string locationCode,
        DateOnly? countDate = null,
        string? description = null,
        IReadOnlyList<string>? itemNos = null,
        CancellationToken cancellationToken = default)
    {
        var location = await context.Set<Location>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Code == locationCode, cancellationToken)
            .ConfigureAwait(false);

        if (location is null)
        {
            return Result<StockCount>.Failure(messages.Render(
                InventoryMessages.LocationNotFound,
                Args(("LocationCode", locationCode))));
        }

        var open = await context.Set<StockCount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.LocationCode == locationCode && c.Status == StockCountStatus.Open,
                cancellationToken)
            .ConfigureAwait(false);

        if (open is not null)
        {
            return Result<StockCount>.Failure(messages.Render(
                InventoryMessages.CountAlreadyOpen,
                Args(("CountNo", open.No), ("LocationCode", locationCode))));
        }

        var series = await setup
            .GetAsync<string>($"{InventoryModule.Id}.Count.NumberSeries", cancellationToken)
            .ConfigureAwait(false) ?? "COUNT";

        var numbered = await numbers.NextAsync(series, clock.Today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<StockCount>.FailureFrom(numbered);
        }

        var count = new StockCount
        {
            No = numbered.Value,
            LocationCode = locationCode,
            CountDate = countDate ?? clock.Today,
            Description = description,
            SheetTakenAtUtc = clock.UtcNow,
        };

        context.Set<StockCount>().Add(count);

        foreach (var line in await SheetAsync(locationCode, itemNos, cancellationToken).ConfigureAwait(false))
        {
            line.StockCountId = count.Id;
            context.Set<StockCountLine>().Add(line);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Count {CountNo} started at {LocationCode} with {Lines} lines.",
            count.No,
            locationCode,
            count.Lines.Count);

        return Result<StockCount>.Success(count);
    }

    /// <summary>
    /// Records what was found.
    /// </summary>
    /// <param name="countNo">The count.</param>
    /// <param name="itemNo">The item.</param>
    /// <param name="countedQuantity">What was on the shelf, or null to un-count it.</param>
    /// <param name="note">Why, where somebody wants to say.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The count, or why it could not be recorded.</returns>
    public async Task<Result<StockCount>> RecordAsync(
        string countNo,
        string itemNo,
        decimal? countedQuantity,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var count = await LoadAsync(countNo, cancellationToken).ConfigureAwait(false);

        if (count is null)
        {
            return Result<StockCount>.Failure(messages.Render(
                InventoryMessages.CountNotFound,
                Args(("CountNo", countNo))));
        }

        if (!count.IsEditable)
        {
            return Result<StockCount>.Failure(messages.Render(
                InventoryMessages.CountAlreadyPosted,
                Args(("CountNo", countNo), ("TransactionNo", count.TransactionNo))));
        }

        var line = count.Lines.FirstOrDefault(l => l.ItemNo == itemNo);

        if (line is null)
        {
            // An item nobody expected at this location is exactly what a count is for. It goes on
            // the sheet with a system quantity of nothing, so the whole of it reads as a gain.
            var item = await context.Set<Item>()
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.No == itemNo, cancellationToken)
                .ConfigureAwait(false);

            if (item is null)
            {
                return Result<StockCount>.Failure(messages.Render(
                    InventoryMessages.ItemNotFound,
                    Args(("ItemNo", itemNo))));
            }

            line = new StockCountLine
            {
                TenantId = count.TenantId,
                CompanyId = count.CompanyId,
                StockCountId = count.Id,
                ItemNo = item.No,
                Description = item.Description,
                SystemQuantity = 0m,
            };

            context.Set<StockCountLine>().Add(line);
        }

        line.CountedQuantity = countedQuantity;
        line.Note = note ?? line.Note;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<StockCount>.Success(count);
    }

    /// <summary>
    /// Posts the differences.
    /// </summary>
    /// <remarks>
    /// Only the differences. A line that matched needs no movement, and posting one would put a
    /// row on every item's history saying that nothing happened to it.
    /// </remarks>
    /// <param name="countNo">The count to post.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The posted count, or every reason it could not be.</returns>
    public async Task<Result<StockCount>> PostAsync(
        string countNo,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        var count = await LoadAsync(countNo, cancellationToken).ConfigureAwait(false);

        if (count is null)
        {
            return Result<StockCount>.Failure(messages.Render(
                InventoryMessages.CountNotFound,
                Args(("CountNo", countNo))));
        }

        if (!count.IsEditable)
        {
            return Result<StockCount>.Failure(messages.Render(
                InventoryMessages.CountAlreadyPosted,
                Args(("CountNo", countNo), ("TransactionNo", count.TransactionNo))));
        }

        var found = new List<AsapMessage>();

        if (count.NotCounted > 0)
        {
            var refusal = Raise(
                InventoryMessages.CountIncomplete,
                Args(("CountNo", count.No), ("NotCountedQuantity", count.NotCounted)),
                heldOverridePermissions);

            if (refusal.Severity is MessageSeverity.Blocked)
            {
                return Result<StockCount>.Failure(refusal);
            }

            found.Add(refusal);
        }

        var differences = count.Differences.ToList();

        if (differences.Count == 0)
        {
            found.Add(messages.Render(
                InventoryMessages.CountNoDifferences,
                Args(("CountNo", count.No))));

            Close(count, transactionNo: null);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result<StockCount>.Success(count, found);
        }

        var allowsNegative = await setup
            .GetAsync<bool>($"{InventoryModule.Id}.Costing.AllowNegativeInventory", cancellationToken)
            .ConfigureAwait(false);

        var movements = differences
            .Select(line => new StockMovementRequest(
                line.ItemNo,
                count.LocationCode,
                line.Difference,
                EntryType: line.Difference > 0m
                    ? ItemLedgerEntryType.PositiveAdjustment
                    : ItemLedgerEntryType.NegativeAdjustment))
            .ToList();

        var posted = await stock
            .PostAsync(
                movements,
                count.CountDate,
                sourceCode: "COUNT",
                documentNo: count.No,
                companyAllowsNegative: allowsNegative,
                heldOverridePermissions,
                overrideReason,
                cancellationToken)
            .ConfigureAwait(false);

        if (posted.Failed)
        {
            return Result<StockCount>.FailureFrom(posted);
        }

        Close(count, posted.Value.TransactionNo);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Count {CountNo} posted as transaction {TransactionNo}: {Differences} differences of "
            + "{Lines} lines at {LocationCode}.",
            count.No,
            count.TransactionNo,
            differences.Count,
            count.Lines.Count,
            count.LocationCode);

        return Result<StockCount>.Success(
            count,
            [.. found, .. posted.Messages.Where(static m => m.Severity is not MessageSeverity.Success)]);
    }

    /// <summary>
    /// Abandons a count.
    /// </summary>
    /// <remarks>
    /// Kept rather than deleted. A count somebody started and gave up on is a fact about the
    /// shop, and the next person to wonder why the shelves were never counted deserves to find
    /// the answer rather than a silence.
    /// </remarks>
    /// <param name="countNo">The count to abandon.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The count, or why it could not be abandoned.</returns>
    public async Task<Result<StockCount>> CancelAsync(
        string countNo,
        CancellationToken cancellationToken = default)
    {
        var count = await LoadAsync(countNo, cancellationToken).ConfigureAwait(false);

        if (count is null)
        {
            return Result<StockCount>.Failure(messages.Render(
                InventoryMessages.CountNotFound,
                Args(("CountNo", countNo))));
        }

        if (!count.IsEditable)
        {
            return Result<StockCount>.Failure(messages.Render(
                InventoryMessages.CountAlreadyPosted,
                Args(("CountNo", countNo), ("TransactionNo", count.TransactionNo))));
        }

        count.Status = StockCountStatus.Cancelled;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<StockCount>.Success(count);
    }

    /// <summary>
    /// Builds the sheet: every item the location has ever held, at what the ledger says now.
    /// </summary>
    private async Task<List<StockCountLine>> SheetAsync(
        string locationCode,
        IReadOnlyList<string>? itemNos,
        CancellationToken cancellationToken)
    {
        var query = context.Set<ItemLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.LocationCode == locationCode);

        if (itemNos is { Count: > 0 })
        {
            query = query.Where(e => itemNos.Contains(e.ItemNo));
        }

        var balances = await query
            .GroupBy(static e => e.ItemNo)
            .Select(static g => new { ItemNo = g.Key, Quantity = g.Sum(static e => e.Quantity) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var descriptions = await context.Set<Item>()
            .AsNoTracking()
            .ToDictionaryAsync(static i => i.No, static i => i.Description, cancellationToken)
            .ConfigureAwait(false);

        // Items asked for by name that the location has never held still go on the sheet. Being
        // told to count something is a reason to have a line for it, whatever the ledger says.
        var known = balances.Select(static b => b.ItemNo).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var itemNo in itemNos ?? [])
        {
            if (known.Add(itemNo))
            {
                balances.Add(new { ItemNo = itemNo, Quantity = 0m });
            }
        }

        return [.. balances
            .Where(b => descriptions.ContainsKey(b.ItemNo))
            .OrderBy(static b => b.ItemNo, StringComparer.OrdinalIgnoreCase)
            .Select(b => new StockCountLine
            {
                TenantId = Guid.Empty,
                CompanyId = Guid.Empty,
                ItemNo = b.ItemNo,
                Description = descriptions[b.ItemNo],
                SystemQuantity = b.Quantity,
            })];
    }

    private void Close(StockCount count, long? transactionNo)
    {
        count.Status = StockCountStatus.Posted;
        count.TransactionNo = transactionNo;
        count.PostedAtUtc = clock.UtcNow;
        count.PostedBy = userContext.UserId;
    }

    /// <summary>Renders a refusal, downgraded to a warning where the caller may push past it.</summary>
    private AsapMessage Raise(
        MessageCode code,
        Dictionary<string, object?> arguments,
        IReadOnlySet<string>? held)
    {
        var rendered = messages.Render(code, arguments);

        return rendered.OverridePermission is { } permission && held?.Contains(permission) == true
            ? messages.AsOverridden(rendered)
            : rendered;
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
