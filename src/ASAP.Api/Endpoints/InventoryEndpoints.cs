using ASAP.Api.Infrastructure;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Counting;
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
/// <param name="IsBlocked">Whether it has been withdrawn from use.</param>
public sealed record ItemSummary(
    string No,
    string Description,
    string? DescriptionArabic,
    string CostingMethod,
    decimal UnitCost,
    decimal UnitPrice,
    decimal QuantityOnHand,
    decimal ReorderPoint,
    bool? AllowNegativeInventory,
    bool IsBlocked);

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

/// <summary>What a client sends to start a stock count.</summary>
/// <param name="LocationCode">The location to count.</param>
/// <param name="CountDate">The day to report it on. Defaults to today.</param>
/// <param name="Description">What the count is for.</param>
/// <param name="ItemNos">
/// Specific items, or null for everything the location has ever held. A partial count is the
/// ordinary case: nobody counts a supermarket in one evening.
/// </param>
public sealed record StartCountRequest(
    string LocationCode,
    DateOnly? CountDate = null,
    string? Description = null,
    IReadOnlyList<string>? ItemNos = null);

/// <summary>What a client sends when something has been counted.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="CountedQuantity">
/// What was on the shelf, or null to mark it uncounted again. Nought is a shelf somebody looked
/// at and found empty; null is a shelf nobody reached, and the two must not be confused.
/// </param>
/// <param name="Note">Why, where somebody wants to say.</param>
public sealed record RecordCountRequest(
    string ItemNo,
    decimal? CountedQuantity,
    string? Note = null);

/// <summary>What a client sends to post a count.</summary>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record PostCountRequest(string? OverrideReason = null);

/// <summary>A unit as a client sends it.</summary>
/// <param name="Code">What it is called on a document.</param>
/// <param name="Name">Its name in English.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="DecimalPlaces">How many decimal places a quantity in it may carry.</param>
/// <param name="IsActive">Whether it may still be chosen.</param>
public sealed record SaveUnitRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    int DecimalPlaces = 0,
    bool IsActive = true);

/// <summary>What one of a unit holds for one item, as a client sends it.</summary>
/// <param name="UnitCode">The unit.</param>
/// <param name="QuantityPerUnit">How many base units are in one of it.</param>
/// <param name="Barcode">Its own barcode, when it has one.</param>
/// <param name="IsActive">Whether it may still be chosen.</param>
public sealed record SaveItemUnitRequest(
    string UnitCode,
    decimal QuantityPerUnit,
    string? Barcode = null,
    bool IsActive = true);

/// <summary>What stock is worth right now, as the client sees it.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is called.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="LocationCode">Where.</param>
/// <param name="Quantity">How much is on hand.</param>
/// <param name="UnitCost">What one costs now.</param>
/// <param name="Value">What the lot is worth now.</param>
public sealed record StockValuationView(
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    string LocationCode,
    decimal Quantity,
    decimal UnitCost,
    decimal Value);

/// <summary>What a client sends to write stock up or down.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="LocationCode">The location.</param>
/// <param name="NewUnitCost">What one should cost from now on.</param>
/// <param name="PostingDate">The date to report it in. Defaults to today.</param>
/// <param name="Reason">Why, which goes on the entries and the ledger description.</param>
/// <param name="ContraAccountNo">Where the loss or gain lands, or null for the category's own.</param>
public sealed record RevalueStockRequest(
    string ItemNo,
    string LocationCode,
    decimal NewUnitCost,
    DateOnly? PostingDate = null,
    string? Reason = null,
    string? ContraAccountNo = null);

/// <summary>Whether a location tracks stock down to the bin.</summary>
/// <param name="UsesBins">On, every movement here has to say which bin.</param>
public sealed record SetBinTrackingRequest(bool UsesBins);

/// <summary>One bin at a location.</summary>
/// <param name="Code">Its code, unique inside its location.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="IsReceiving">Whether arrivals land here when nobody says where.</param>
/// <param name="PickOrder">The order a picker walks the bins in.</param>
/// <param name="IsBlocked">Whether it is withdrawn from use.</param>
public sealed record BinView(
    string Code,
    string? Name,
    string? NameArabic,
    bool IsReceiving,
    int PickOrder,
    bool IsBlocked);

/// <summary>What is standing on one shelf.</summary>
/// <param name="BinCode">The bin.</param>
/// <param name="BinName">What it is called.</param>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What the item is called.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="Quantity">How much of it is there.</param>
public sealed record BinContentView(
    string BinCode,
    string? BinName,
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    decimal Quantity);

/// <summary>A bin as a client sends it.</summary>
/// <param name="Code">Its code, unique inside its location.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="IsReceiving">Whether arrivals land here when nobody says where.</param>
/// <param name="PickOrder">The order a picker walks the bins in.</param>
/// <param name="IsBlocked">Whether it is withdrawn from use.</param>
public sealed record SaveBinRequest(
    string Code,
    string? Name = null,
    string? NameArabic = null,
    bool IsReceiving = false,
    int PickOrder = 0,
    bool IsBlocked = false);

/// <summary>One unit an item may be handled in.</summary>
/// <param name="UnitCode">The unit.</param>
/// <param name="QuantityPerUnit">How many base units one of these is.</param>
/// <param name="Barcode">Its own barcode, when it has one.</param>
/// <param name="IsBase">Whether this is the unit stock is stored in.</param>
public sealed record ItemUnitView(
    string UnitCode,
    decimal QuantityPerUnit,
    string? Barcode,
    bool IsBase);

/// <summary>What a scan or a keyed quantity came to.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is called.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="UnitCode">The unit scanned or named.</param>
/// <param name="Quantity">How many of that unit.</param>
/// <param name="BaseQuantity">The same amount in the unit stock is stored in.</param>
/// <param name="BaseUnitCode">What that unit is.</param>
public sealed record ResolvedQuantityView(
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    string UnitCode,
    decimal Quantity,
    decimal BaseQuantity,
    string BaseUnitCode);

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

        group.MapGet("/counts", CountsAsync)
             .WithName("StockCounts")
             .WithSummary("Lists stock counts, most recent first.");

        group.MapGet("/counts/{countNo}", CountAsync)
             .WithName("StockCount")
             .WithSummary("Reads one count and its sheet.");

        group.MapPost("/counts", StartCountAsync)
             .WithName("StartStockCount")
             .WithSummary("Starts a count and makes the sheet from what the system says now.");

        group.MapPost("/counts/{countNo}/lines", RecordCountAsync)
             .WithName("RecordStockCount")
             .WithSummary("Records what was found on a shelf.");

        group.MapPost("/counts/{countNo}/post", PostCountAsync)
             .WithName("PostStockCount")
             .WithSummary("Posts the differences as adjustments, and closes the count.");

        group.MapPost("/counts/{countNo}/cancel", CancelCountAsync)
             .WithName("CancelStockCount")
             .WithSummary("Abandons a count. It stays on the record.");

        group.MapPost("/stock/settle", SettleAsync)
             .WithName("SettleCosts")
             .WithSummary("Settles estimated costs against what the goods actually cost.");

        group.MapGet("/units", UnitsAsync)
             .WithName("UnitsOfMeasure")
             .WithSummary("Lists the units this company counts, weighs and measures in.");

        group.MapGet("/items/{itemNo}/units", ItemUnitsAsync)
             .WithName("ItemUnits")
             .WithSummary("Lists the units one item may be handled in, base unit first.");

        group.MapGet("/scan/{barcode}", ScanAsync)
             .WithName("ScanBarcode")
             .WithSummary("Says what a barcode is and how many it stands for.");

        group.MapPost("/units", SaveUnitAsync)
             .WithName("SaveUnitOfMeasure")
             .WithSummary("Adds a unit to the company's list, or changes one on it.");

        group.MapPost("/items/{itemNo}/units", SaveItemUnitAsync)
             .WithName("SaveItemUnit")
             .WithSummary("Says what one of a unit holds, for one item.");

        group.MapDelete("/items/{itemNo}/units/{unitCode}", RemoveItemUnitAsync)
             .WithName("RemoveItemUnit")
             .WithSummary("Takes a unit off an item. What already posted keeps its factor.");

        group.MapGet("/locations/{locationCode}/bins", BinsAsync)
             .WithName("Bins")
             .WithSummary("Lists the bins at a location, in the order a picker walks them.");

        group.MapGet("/locations/{locationCode}/bin-contents", BinContentsAsync)
             .WithName("BinContents")
             .WithSummary("Says what is standing on each shelf at a location.");

        group.MapPost("/locations/{locationCode}/bins", SaveBinAsync)
             .WithName("SaveBin")
             .WithSummary("Adds a bin to a location, or changes one already there.");

        group.MapDelete("/locations/{locationCode}/bins/{binCode}", RemoveBinAsync)
             .WithName("RemoveBin")
             .WithSummary("Takes an empty bin off a location.");

        group.MapPost("/locations/{locationCode}/bin-tracking", SetBinTrackingAsync)
             .WithName("SetBinTracking")
             .WithSummary("Turns bin tracking on or off at a location.");

        group.MapGet("/stock/valuation", ValuationAsync)
             .WithName("StockValuation")
             .WithSummary("What one item is worth at one location right now.");

        group.MapPost("/stock/revalue", RevalueAsync)
             .WithName("RevalueStock")
             .WithSummary("Writes stock up or down without changing how much there is.");

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
                i.AllowNegativeInventory,
                i.IsBlocked))
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
                l.UsesBins,
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

    private static async Task<IResult> CountsAsync(
        StockCountService counts,
        IUserContext user,
        HttpContext http,
        [FromQuery] string? locationCode,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Count.Read"))
        {
            return Forbidden("Inventory.Count.Read", "see stock counts", http);
        }

        var found = await counts.ListAsync(locationCode, cancellationToken).ConfigureAwait(false);

        return Results.Ok(found.Select(Summarise));
    }

    private static async Task<IResult> CountAsync(
        string countNo,
        StockCountService counts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Count.Read"))
        {
            return Forbidden("Inventory.Count.Read", "see stock counts", http);
        }

        var count = await counts.LoadAsync(countNo, cancellationToken).ConfigureAwait(false);

        return count is null ? Results.NotFound() : Results.Ok(View(count));
    }

    private static async Task<IResult> StartCountAsync(
        StartCountRequest request,
        StockCountService counts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Count.Create"))
        {
            return Forbidden("Inventory.Count.Create", "start a stock count", http);
        }

        var result = await counts
            .StartAsync(
                request.LocationCode,
                request.CountDate,
                request.Description,
                request.ItemNos,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(result.Value));
    }

    private static async Task<IResult> RecordCountAsync(
        string countNo,
        RecordCountRequest request,
        StockCountService counts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Count.Create"))
        {
            return Forbidden("Inventory.Count.Create", "record a count", http);
        }

        var result = await counts
            .RecordAsync(countNo, request.ItemNo, request.CountedQuantity, request.Note, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(result.Value));
    }

    private static async Task<IResult> PostCountAsync(
        string countNo,
        PostCountRequest? request,
        StockCountService counts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Count.Post"))
        {
            return Forbidden("Inventory.Count.Post", "post what a count found", http);
        }

        var overrides = new[] { "Inventory.Stock.Override" }
            .Where(permission => Can(user, permission))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = await counts
            .PostAsync(countNo, overrides, request?.OverrideReason, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                count = View(result.Value),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> CancelCountAsync(
        string countNo,
        StockCountService counts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Count.Create"))
        {
            return Forbidden("Inventory.Count.Create", "abandon a stock count", http);
        }

        var result = await counts.CancelAsync(countNo, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(result.Value));
    }

    private static object Summarise(StockCount count)
        => new
        {
            no = count.No,
            locationCode = count.LocationCode,
            countDate = count.CountDate,
            status = count.Status.ToString(),
            description = count.Description,
            lines = count.Lines.Count,
            notCounted = count.NotCounted,
            differences = count.Differences.Count(),
            transactionNo = count.TransactionNo,
        };

    private static object View(StockCount count)
        => new
        {
            no = count.No,
            locationCode = count.LocationCode,
            countDate = count.CountDate,
            status = count.Status.ToString(),
            description = count.Description,

            // What the system quantities are as at. A reader comparing a line against the shelf
            // today needs to know how old the figure they are arguing with is.
            sheetTakenAtUtc = count.SheetTakenAtUtc,
            notCounted = count.NotCounted,
            transactionNo = count.TransactionNo,
            lines = count.Lines
                .OrderBy(static l => l.ItemNo, StringComparer.OrdinalIgnoreCase)
                .Select(static l => new
                {
                    itemNo = l.ItemNo,
                    description = l.Description,
                    systemQuantity = l.SystemQuantity,
                    countedQuantity = l.CountedQuantity,
                    difference = l.Difference,
                    note = l.Note,
                }),
        };

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

    private static async Task<IResult> UnitsAsync(
        AsapDbContext context,
        CancellationToken cancellationToken,
        bool includeInactive = false)
    {
        // A setup screen has to see what somebody switched off, or the only way to switch it
        // back on is to guess that it is there. A till asks without the flag and sees only what
        // it may sell in.
        var units = await context.Set<UnitOfMeasure>()
            .AsNoTracking()
            .Where(u => includeInactive || u.IsActive)
            .OrderBy(u => u.Code)
            .Select(u => new
            {
                code = u.Code,
                name = u.Name,
                nameArabic = u.NameArabic,
                decimalPlaces = u.DecimalPlaces,
                isActive = u.IsActive,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(units);
    }

    private static async Task<IResult> ItemUnitsAsync(
        string itemNo,
        UnitConversionService conversion,
        CancellationToken cancellationToken)
    {
        var units = await conversion.UnitsAsync(itemNo, cancellationToken).ConfigureAwait(false);

        return units.Count == 0
            ? Results.NotFound()
            : Results.Ok(units.Select(static u => new ItemUnitView(
                u.UnitCode, u.QuantityPerUnit, u.Barcode, u.IsBase)));
    }

    private static async Task<IResult> ScanAsync(
        string barcode,
        UnitConversionService conversion,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await conversion.ScanAsync(barcode, cancellationToken).ConfigureAwait(false);

        return result.Failed
            ? Results.Json(
                AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
                statusCode: AsapProblem.StatusFor(result.Messages))
            : Results.Ok(new ResolvedQuantityView(
                result.Value.ItemNo,
                result.Value.Description,
                result.Value.DescriptionArabic,
                result.Value.UnitCode,
                result.Value.Quantity,
                result.Value.BaseQuantity,
                result.Value.BaseUnitCode));
    }
    private static async Task<IResult> SaveUnitAsync(
        SaveUnitRequest request,
        UnitSetupService setup,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Unit.Update"))
        {
            return Forbidden("Inventory.Unit.Update", "maintain units of measure", http);
        }

        var result = await setup
            .SaveUnitAsync(
                new UnitRequest(
                    request.Code,
                    request.Name,
                    request.NameArabic,
                    request.DecimalPlaces,
                    request.IsActive),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                code = result.Value.Code,
                name = result.Value.Name,
                nameArabic = result.Value.NameArabic,
                decimalPlaces = result.Value.DecimalPlaces,
                isActive = result.Value.IsActive,
            });
    }

    private static async Task<IResult> SaveItemUnitAsync(
        string itemNo,
        SaveItemUnitRequest request,
        UnitSetupService setup,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Unit.Update"))
        {
            return Forbidden("Inventory.Unit.Update", "maintain units of measure", http);
        }

        var result = await setup
            .SaveItemUnitAsync(
                itemNo,
                new ItemUnitRequest(
                    request.UnitCode,
                    request.QuantityPerUnit,
                    request.Barcode,
                    request.IsActive),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                unitCode = result.Value.UnitCode,
                quantityPerUnit = result.Value.QuantityPerUnit,
                barcode = result.Value.Barcode,
                isActive = result.Value.IsActive,
            });
    }

    private static async Task<IResult> RemoveItemUnitAsync(
        string itemNo,
        string unitCode,
        UnitSetupService setup,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Unit.Update"))
        {
            return Forbidden("Inventory.Unit.Update", "maintain units of measure", http);
        }

        var result = await setup
            .RemoveItemUnitAsync(itemNo, unitCode, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.NoContent();
    }

    private static async Task<IResult> BinsAsync(
        string locationCode,
        BinSetupService bins,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Bin.Read"))
        {
            return Forbidden("Inventory.Bin.Read", "view bins", http);
        }

        var rows = await bins.BinsAsync(locationCode, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static b => new BinView(
            b.Code,
            b.Name,
            b.NameArabic,
            b.IsReceiving,
            b.PickOrder,
            b.IsBlocked)));
    }

    private static async Task<IResult> BinContentsAsync(
        string locationCode,
        BinSetupService bins,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        string? itemNo = null)
    {
        if (!Can(user, "Inventory.Bin.Read"))
        {
            return Forbidden("Inventory.Bin.Read", "view what is on the shelves", http);
        }

        var rows = await bins.ContentsAsync(locationCode, itemNo, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static r => new BinContentView(
            r.BinCode,
            r.BinName,
            r.ItemNo,
            r.Description,
            r.DescriptionArabic,
            r.Quantity)));
    }

    private static async Task<IResult> SaveBinAsync(
        string locationCode,
        SaveBinRequest request,
        BinSetupService bins,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Bin.Update"))
        {
            return Forbidden("Inventory.Bin.Update", "maintain bins", http);
        }

        var result = await bins
            .SaveAsync(
                locationCode,
                new BinRequest(
                    request.Code,
                    request.Name,
                    request.NameArabic,
                    request.IsReceiving,
                    request.PickOrder,
                    request.IsBlocked),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new BinView(
                result.Value.Code,
                result.Value.Name,
                result.Value.NameArabic,
                result.Value.IsReceiving,
                result.Value.PickOrder,
                result.Value.IsBlocked));
    }

    private static async Task<IResult> RemoveBinAsync(
        string locationCode,
        string binCode,
        BinSetupService bins,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Bin.Update"))
        {
            return Forbidden("Inventory.Bin.Update", "maintain bins", http);
        }

        var result = await bins.RemoveAsync(locationCode, binCode, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.NoContent();
    }

    private static async Task<IResult> SetBinTrackingAsync(
        string locationCode,
        SetBinTrackingRequest request,
        BinSetupService bins,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Bin.Update"))
        {
            return Forbidden("Inventory.Bin.Update", "turn bin tracking on or off", http);
        }

        var result = await bins
            .SetUsesBinsAsync(locationCode, request.UsesBins, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new { code = result.Value.Code, usesBins = result.Value.UsesBins });
    }

    private static async Task<IResult> ValuationAsync(
        RevaluationService revaluation,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        string itemNo,
        string locationCode)
    {
        if (!Can(user, "Inventory.Stock.Read"))
        {
            return Forbidden("Inventory.Stock.Read", "read a stock valuation", http);
        }

        var result = await revaluation
            .ValuationAsync(itemNo, locationCode, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new StockValuationView(
                result.Value.ItemNo,
                result.Value.Description,
                result.Value.DescriptionArabic,
                result.Value.LocationCode,
                result.Value.Quantity,
                result.Value.UnitCost,
                result.Value.Value));
    }

    private static async Task<IResult> RevalueAsync(
        RevalueStockRequest request,
        RevaluationService revaluation,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Revaluation.Post"))
        {
            return Forbidden("Inventory.Revaluation.Post", "write stock up or down", http);
        }

        var result = await revaluation
            .RevalueAsync(
                request.ItemNo,
                request.LocationCode,
                request.NewUnitCost,
                request.PostingDate ?? clock.Today,
                request.Reason,
                request.ContraAccountNo,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                transactionNo = result.Value.TransactionNo,
                quantity = result.Value.Quantity,
                oldUnitCost = result.Value.OldUnitCost,
                newUnitCost = result.Value.NewUnitCost,
                valueChange = result.Value.ValueChange,
                layerCount = result.Value.LayerCount,
                messages = result.Messages.Select(static m => new
                {
                    code = m.Code.Value,
                    severity = m.Severity.ToString(),
                    title = m.Title,
                    detail = m.Detail,
                    resolution = m.Resolution,
                }),
            });
    }

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult Refused(ASAP.Platform.Kernel.Results.Result result, HttpContext http)
        => Results.Json(
            AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
            statusCode: AsapProblem.StatusFor(result.Messages));
}
