using ASAP.Api.Infrastructure;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>An item as the client sees it.</summary>
/// <param name="No">The item number.</param>
/// <param name="Description">What it is.</param>
/// <param name="DescriptionArabic">What it is, in Arabic.</param>
/// <param name="CostingMethod">How its cost is worked out.</param>
/// <param name="UnitCost">Current cost per unit.</param>
/// <param name="UnitPrice">List price before discount.</param>
/// <param name="QuantityOnHand">Total across every location.</param>
/// <param name="ReorderPoint">Level at which it should be reordered.</param>
/// <param name="AllowNegativeInventory">Whether it may go below zero, or null to follow the company.</param>
public sealed record ItemSummary(
    string No,
    string Description,
    string? DescriptionArabic,
    string CostingMethod,
    decimal UnitCost,
    decimal UnitPrice,
    decimal QuantityOnHand,
    decimal ReorderPoint,
    bool? AllowNegativeInventory);

/// <summary>What is on hand for one item at one location.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is.</param>
/// <param name="DescriptionArabic">What it is, in Arabic. Sent alongside rather than instead of,
/// so switching language redraws the table without going back to the server.</param>
/// <param name="LocationCode">Where.</param>
/// <param name="Quantity">How much.</param>
/// <param name="IsNegative">Whether the balance has gone below zero.</param>
public sealed record StockOnHandRow(
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    string LocationCode,
    decimal Quantity,
    bool IsNegative);

/// <summary>What a client sends to move stock.</summary>
/// <param name="Movements">The movements.</param>
/// <param name="PostingDate">The date to report them in. Defaults to today.</param>
/// <param name="DocumentNo">The document behind them.</param>
/// <param name="SourceCode">Where they came from.</param>
/// <param name="OverrideReason">
/// Why a protection is being pushed past, recorded with the override. Only read when the caller
/// actually overrides something; it neither grants the right nor is required to hold it.
/// </param>
public sealed record PostStockRequest(
    IReadOnlyList<StockMovementRequest> Movements,
    DateOnly? PostingDate = null,
    string? DocumentNo = null,
    string SourceCode = "INVJNL",
    string? OverrideReason = null);

/// <summary>Items, locations, stock levels and movements.</summary>
public static class InventoryEndpoints
{
    /// <summary>Maps the Inventory endpoints.</summary>
    /// <param name="app">The route builder.</param>
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/inventory").RequireAuthorization().WithTags("Inventory");

        group.MapGet("/items", ItemsAsync)
             .WithName("Items")
             .WithSummary("Lists the items in the active company.");

        group.MapGet("/locations", LocationsAsync)
             .WithName("Locations")
             .WithSummary("Lists the locations stock can be held at.");

        group.MapGet("/stock/on-hand", OnHandAsync)
             .WithName("StockOnHand")
             .WithSummary("Reports what is on hand, by item and location.");

        group.MapGet("/stock/movements", MovementsAsync)
             .WithName("StockMovements")
             .WithSummary("Lists item ledger entries, most recent first.");

        group.MapPost("/stock/post", PostStockAsync)
             .WithName("PostStock")
             .WithSummary("Receives, issues or adjusts stock, valuing the movement as it goes.");

        group.MapPost("/stock/settle", SettleAsync)
             .WithName("SettleCosts")
             .WithSummary("Settles estimated costs against what the goods actually cost.");

        return app;
    }

    private static async Task<IResult> ItemsAsync(AsapDbContext context, CancellationToken cancellationToken)
        => Results.Ok(await context.Set<Item>()
            .AsNoTracking()
            .OrderBy(i => i.No)
            .Select(i => new ItemSummary(
                i.No,
                i.Description,
                i.DescriptionArabic,
                i.CostingMethod.ToString(),
                i.UnitCost,
                i.UnitPrice,
                i.QuantityOnHand,
                i.ReorderPoint,
                i.AllowNegativeInventory))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));

    private static async Task<IResult> LocationsAsync(AsapDbContext context, CancellationToken cancellationToken)
        => Results.Ok(await context.Set<Location>()
            .AsNoTracking()
            .OrderBy(l => l.Code)
            .Select(l => new
            {
                l.Code,
                l.Name,
                l.NameArabic,
                l.IsSellable,
                l.IsInTransit,
                l.IsBlocked,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Reports stock on hand, summed from the ledger.
    /// </summary>
    /// <remarks>
    /// Summed rather than read off the item, because the item carries one total across every
    /// location and the question people actually ask is about one shelf. Balances that have gone
    /// below zero are flagged rather than hidden: they are the ones waiting for goods to arrive.
    /// </remarks>
    private static async Task<IResult> OnHandAsync(
        AsapDbContext context,
        [FromQuery] string? itemNo,
        CancellationToken cancellationToken)
    {
        var query = context.Set<ItemLedgerEntry>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(itemNo))
        {
            query = query.Where(e => e.ItemNo == itemNo);
        }

        var rows = await query
            .GroupBy(static e => new { e.ItemNo, e.LocationCode })
            .Select(static g => new
            {
                g.Key.ItemNo,
                g.Key.LocationCode,
                Quantity = g.Sum(static e => e.Quantity),
            })
            .Where(static g => g.Quantity != 0)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var descriptions = await context.Set<Item>()
            .AsNoTracking()
            .ToDictionaryAsync(
                static i => i.No,
                static i => new { i.Description, i.DescriptionArabic },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(rows
            .OrderBy(static r => r.ItemNo)
            .ThenBy(static r => r.LocationCode)
            .Select(r => new StockOnHandRow(
                r.ItemNo,
                descriptions.GetValueOrDefault(r.ItemNo)?.Description ?? string.Empty,
                descriptions.GetValueOrDefault(r.ItemNo)?.DescriptionArabic,
                r.LocationCode,
                r.Quantity,
                r.Quantity < 0)));
    }

    private static async Task<IResult> MovementsAsync(
        AsapDbContext context,
        [FromQuery] string? itemNo,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var query = context.Set<ItemLedgerEntry>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(itemNo))
        {
            query = query.Where(e => e.ItemNo == itemNo);
        }

        var entries = await query
            .OrderByDescending(e => e.TransactionNo)
            .Take(Math.Clamp(take ?? 100, 1, 500))
            .Select(e => new
            {
                e.PostingDate,
                e.TransactionNo,
                e.ItemNo,
                e.LocationCode,
                EntryType = e.EntryType.ToString(),
                e.Quantity,
                e.RemainingQuantity,
                e.DocumentNo,
                e.SourceCode,
                e.WentNegative,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(entries);
    }

    private static async Task<IResult> PostStockAsync(
        PostStockRequest request,
        StockPostingService posting,
        ISetupService setup,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!user.Has("Inventory.Stock.Post") && !user.IsSuperUser)
        {
            return Results.Json(
                new ProblemDetails
                {
                    Type = AsapProblem.TypeUri,
                    Title = "You do not have permission to post stock movements",
                    Detail = "Inventory.Stock.Post is required.",
                    Status = StatusCodes.Status403Forbidden,
                    Instance = http.Request.Path,
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var allowsNegative = await setup
            .GetAsync<bool>($"{InventoryModule.Id}.Costing.AllowNegativeInventory", cancellationToken)
            .ConfigureAwait(false);

        // Only the overrides this caller actually holds. The availability rules downgrade a block
        // to a warning when the permission is present, exactly as the ledger poster does.
        var overrides = new[] { "Inventory.Stock.Override" }
            .Where(permission => user.IsSuperUser || user.Has(permission))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = await posting
            .PostAsync(
                request.Movements,
                request.PostingDate ?? clock.Today,
                request.SourceCode,
                request.DocumentNo,
                allowsNegative,
                overrides,
                request.OverrideReason,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return Results.Json(
                AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
                statusCode: AsapProblem.StatusFor(result.Messages));
        }

        return Results.Ok(new
        {
            transactionNo = result.Value.TransactionNo,
            entryCount = result.Value.EntryCount,
            costAmount = result.Value.CostAmount,
            estimatedCostAmount = result.Value.EstimatedCostAmount,

            // Warnings travel back with the success. A sale that took stock below zero should say
            // so on the screen, not only in the log.
            messages = MessagePayload.FromAll(result.Messages),
        });
    }

    private static async Task<IResult> SettleAsync(
        CostSettlementService settlement,
        [FromQuery] string? itemNo,
        CancellationToken cancellationToken)
    {
        var result = await settlement.SettleAsync(itemNo, cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            itemsExamined = result.Value.ItemsExamined,
            applicationsSettled = result.Value.ApplicationsSettled,
            totalCorrection = result.Value.TotalCorrection,
            messages = MessagePayload.FromAll(result.Messages),
        });
    }
}
