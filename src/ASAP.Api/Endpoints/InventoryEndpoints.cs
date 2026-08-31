using ASAP.Api.Infrastructure;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Adjustments;
using ASAP.Modules.Inventory.Costing;
using ASAP.Modules.Inventory.Counting;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Modules.Inventory.Posting;
using ASAP.Modules.Inventory.Reporting;
using ASAP.Modules.Inventory.Reservations;
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

/// <summary>What a client sends to hold stock for a document.</summary>
/// <param name="ItemNo">What to hold.</param>
/// <param name="LocationCode">Where it is being held.</param>
/// <param name="Quantity">How much. Always positive.</param>
/// <param name="DocumentNo">What it is being held for.</param>
/// <param name="DocumentLineNo">Which line of that document.</param>
/// <param name="VariantCode">Which variant, on an item that has them.</param>
/// <param name="SourceCode">Which module the document belongs to.</param>
/// <param name="Note">Why, where it is worth saying.</param>
public sealed record ReserveStockRequest(
    string ItemNo,
    string LocationCode,
    decimal Quantity,
    string DocumentNo,
    int? DocumentLineNo = null,
    string? VariantCode = null,
    string? SourceCode = null,
    string? Note = null);

/// <summary>What a client sends to let held stock go.</summary>
/// <param name="Reason">Why.</param>
public sealed record ReleaseStockRequest(string? Reason = null);

/// <summary>One reservation as it is reported back.</summary>
/// <param name="ItemNo">The item held.</param>
/// <param name="VariantCode">Which variant, where the item has them.</param>
/// <param name="LocationCode">Where.</param>
/// <param name="DocumentNo">What it is held for.</param>
/// <param name="DocumentLineNo">Which line of that document.</param>
/// <param name="SourceCode">Which module the document belongs to.</param>
/// <param name="Quantity">How much was held to begin with.</param>
/// <param name="QuantityOutstanding">How much is still held.</param>
/// <param name="QuantityFulfilled">How much has gone against it.</param>
/// <param name="ReleaseReason">Why it was let go, where somebody said.</param>
/// <param name="Note">The note on it.</param>
public sealed record StockReservationRow(
    string ItemNo,
    string? VariantCode,
    string LocationCode,
    string DocumentNo,
    int? DocumentLineNo,
    string? SourceCode,
    decimal Quantity,
    decimal QuantityOutstanding,
    decimal QuantityFulfilled,
    string? ReleaseReason,
    string? Note);

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

/// <summary>One version of an item that is stocked separately.</summary>
/// <param name="Code">Its code, unique within its item.</param>
/// <param name="Description">What this version is called.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="Barcode">Its own barcode.</param>
/// <param name="SortOrder">Where it sits in a list.</param>
/// <param name="IsBlocked">Whether it is withdrawn from use.</param>
public sealed record ItemVariantView(
    string Code,
    string Description,
    string? DescriptionArabic,
    string? Barcode,
    int SortOrder,
    bool IsBlocked);

/// <summary>A variant as a client sends it.</summary>
/// <param name="Code">Its code, unique within its item.</param>
/// <param name="Description">What this version is called.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="Barcode">Its own barcode.</param>
/// <param name="SortOrder">Where it sits in a list.</param>
/// <param name="IsBlocked">Whether it is withdrawn from use.</param>
public sealed record SaveItemVariantRequest(
    string Code,
    string Description,
    string? DescriptionArabic = null,
    string? Barcode = null,
    int SortOrder = 0,
    bool IsBlocked = false);

/// <summary>Whether an item is stocked as separate versions.</summary>
/// <param name="HasVariants">On, every movement has to say which variant.</param>
public sealed record SetHasVariantsRequest(bool HasVariants);

/// <summary>What one variant is holding at one location.</summary>
/// <param name="VariantCode">The variant.</param>
/// <param name="Description">What it is called.</param>
/// <param name="DescriptionArabic">The same in Arabic.</param>
/// <param name="LocationCode">Where.</param>
/// <param name="Quantity">How much of it is there.</param>
public sealed record VariantStockView(
    string VariantCode,
    string Description,
    string? DescriptionArabic,
    string LocationCode,
    decimal Quantity);

/// <summary>One category items are grouped under.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="ParentCode">The category it sits under.</param>
/// <param name="InventoryAccountNo">Where the value of its stock is held.</param>
/// <param name="CostOfGoodsSoldAccountNo">Where the cost of what it sells is charged.</param>
/// <param name="SalesAccountNo">Where revenue from it is credited.</param>
/// <param name="VarianceAccountNo">Where an adjustment or a settled estimate lands.</param>
/// <param name="ItemCount">How many items sit under it.</param>
public sealed record ItemCategoryView(
    string Code,
    string Name,
    string? NameArabic,
    string? ParentCode,
    string? InventoryAccountNo,
    string? CostOfGoodsSoldAccountNo,
    string? SalesAccountNo,
    string? VarianceAccountNo,
    int ItemCount);

/// <summary>A category as a client sends it.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="ParentCode">The category it sits under.</param>
/// <param name="InventoryAccountNo">Where the value of its stock is held.</param>
/// <param name="CostOfGoodsSoldAccountNo">Where the cost of what it sells is charged.</param>
/// <param name="SalesAccountNo">Where revenue from it is credited.</param>
/// <param name="VarianceAccountNo">Where an adjustment or a settled estimate lands.</param>
public sealed record SaveItemCategoryRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    string? ParentCode = null,
    string? InventoryAccountNo = null,
    string? CostOfGoodsSoldAccountNo = null,
    string? SalesAccountNo = null,
    string? VarianceAccountNo = null);

/// <summary>Which category an item belongs to.</summary>
/// <param name="CategoryCode">The category, or null to take it out of any.</param>
public sealed record SetItemCategoryRequest(string? CategoryCode);

/// <summary>What a category is not posting, and what that has cost.</summary>
/// <param name="Code">The category, or empty for items in none.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="ItemCount">How many items sit under it.</param>
/// <param name="MissingAccounts">Which of its accounts have not been set.</param>
/// <param name="UnpostedValue">The value of movements that could not reach the ledger.</param>
/// <param name="UnpostedEntryCount">How many movements that was.</param>
public sealed record CategoryPostingGapView(
    string Code,
    string Name,
    string? NameArabic,
    int ItemCount,
    IReadOnlyList<string> MissingAccounts,
    decimal UnpostedValue,
    int UnpostedEntryCount);

/// <summary>One reason stock may be adjusted for.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="ContraAccountNo">Where the value lands, or null for the category's variance account.</param>
/// <param name="Direction">Which way it may move stock.</param>
/// <param name="RequiresNote">Whether it needs something written against it.</param>
/// <param name="IsActive">Whether it may still be chosen.</param>
public sealed record AdjustmentReasonView(
    string Code,
    string Name,
    string? NameArabic,
    string? ContraAccountNo,
    string Direction,
    bool RequiresNote,
    bool IsActive);

/// <summary>A reason as a client sends it.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="ContraAccountNo">Where the value lands, or null for the category's variance account.</param>
/// <param name="Direction">Which way it may move stock.</param>
/// <param name="RequiresNote">Whether it needs something written against it.</param>
/// <param name="IsActive">Whether it may still be chosen.</param>
public sealed record SaveAdjustmentReasonRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    string? ContraAccountNo = null,
    AdjustmentDirection Direction = AdjustmentDirection.Either,
    bool RequiresNote = false,
    bool IsActive = true);

/// <summary>What was adjusted under one reason.</summary>
/// <param name="ReasonCode">The reason, or empty where none was given.</param>
/// <param name="ReasonName">What it is called.</param>
/// <param name="ReasonNameArabic">The same in Arabic.</param>
/// <param name="EntryCount">How many adjustments carried it.</param>
/// <param name="Quantity">The net quantity moved under it.</param>
/// <param name="CostAmount">What that was worth.</param>
public sealed record ShrinkageView(
    string ReasonCode,
    string ReasonName,
    string? ReasonNameArabic,
    int EntryCount,
    decimal Quantity,
    decimal CostAmount);

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
/// <param name="VariantCode">Which variant, on an item that has them.</param>
public sealed record RevalueStockRequest(
    string ItemNo,
    string LocationCode,
    decimal NewUnitCost,
    DateOnly? PostingDate = null,
    string? Reason = null,
    string? ContraAccountNo = null,
    string? VariantCode = null);

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
/// <param name="VariantCode">Which variant was scanned, on an item that has them.</param>
/// <param name="VariantDescription">What that variant is called.</param>
public sealed record ResolvedQuantityView(
    string ItemNo,
    string Description,
    string? DescriptionArabic,
    string UnitCode,
    decimal Quantity,
    decimal BaseQuantity,
    string BaseUnitCode,
    string? VariantCode = null,
    string? VariantDescription = null);

/// <summary>One thing moved from one shelf to another.</summary>
/// <param name="LineNo">Position on the sheet.</param>
/// <param name="ItemNo">What moved.</param>
/// <param name="ItemName">What it is called.</param>
/// <param name="VariantCode">Which variant, on an item that has them.</param>
/// <param name="FromBinCode">The shelf it came off.</param>
/// <param name="ToBinCode">The shelf it went onto.</param>
/// <param name="Quantity">How much moved.</param>
public sealed record BinMovementLineView(
    int LineNo,
    string ItemNo,
    string? ItemName,
    string? VariantCode,
    string FromBinCode,
    string ToBinCode,
    decimal Quantity);

/// <summary>Goods moved between shelves inside one place.</summary>
/// <param name="No">The movement number.</param>
/// <param name="LocationCode">Where it happened.</param>
/// <param name="MovementDate">When.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Note">Why, where anybody said.</param>
/// <param name="RecordedByUserName">Who recorded it.</param>
/// <param name="TransactionNo">The transaction the entries posted under.</param>
/// <param name="Lines">What moved.</param>
public sealed record BinMovementView(
    string No,
    string LocationCode,
    DateOnly MovementDate,
    BinMovementStatus Status,
    string? Note,
    string? RecordedByUserName,
    long? TransactionNo,
    IReadOnlyList<BinMovementLineView> Lines);

/// <summary>What a client sends to move goods between shelves.</summary>
/// <param name="LocationCode">Where it happened.</param>
/// <param name="Lines">What moved.</param>
/// <param name="MovementDate">When, or null for today.</param>
/// <param name="Note">Why, where anybody said.</param>
public sealed record PostBinMovementRequest(
    string LocationCode,
    IReadOnlyList<BinMovementLineRequest> Lines,
    DateOnly? MovementDate = null,
    string? Note = null);

/// <summary>When to reorder an item at one place, and how much.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="ItemName">What it is called.</param>
/// <param name="LocationCode">Where it is stocked.</param>
/// <param name="VariantCode">Which variant, where the policy names one.</param>
/// <param name="Kind">Whether the quantity is fixed or measured against a maximum.</param>
/// <param name="ReorderPoint">The level at or below which it should be reordered.</param>
/// <param name="ReorderQuantity">How much to order, on a fixed-quantity policy.</param>
/// <param name="MaximumInventory">The level to order back up to.</param>
/// <param name="MinimumOrderQuantity">The least the vendor will ship.</param>
/// <param name="OrderMultiple">The pack it is sold in.</param>
/// <param name="LeadTimeDays">Days between ordering and arrival.</param>
/// <param name="VendorNo">A vendor it is normally bought from.</param>
/// <param name="IsActive">Whether the worksheet still looks at it.</param>
public sealed record ReorderPolicyView(
    string ItemNo,
    string ItemName,
    string LocationCode,
    string? VariantCode,
    ReorderKind Kind,
    decimal ReorderPoint,
    decimal ReorderQuantity,
    decimal MaximumInventory,
    decimal MinimumOrderQuantity,
    decimal OrderMultiple,
    int LeadTimeDays,
    string? VendorNo,
    bool IsActive);

/// <summary>Items, locations, stock levels and movements.</summary>
public static class InventoryEndpoints
{
    /// <summary>Maps the Inventory endpoints.</summary>
    /// <param name="app">The route builder.</param>
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/inventory").RequireAuthorization().WithTags("Inventory");

        group.MapGet("/bin-movements", BinMovementsAsync)
             .WithName("BinMovements")
             .WithSummary("Goods moved between shelves, most recent first.");

        group.MapPost("/bin-movements", PostBinMovementAsync)
             .WithName("PostBinMovement")
             .WithSummary("Moves goods between shelves inside one place, all lines or none.");

        group.MapGet("/reorder-policies", ReorderPoliciesAsync)
             .WithName("ReorderPolicies")
             .WithSummary("When each place reorders each item, and how much.");

        group.MapPut("/reorder-policies/{itemNo}/{locationCode}", SaveReorderPolicyAsync)
             .WithName("SaveReorderPolicy")
             .WithSummary("Writes a reorder policy for one item at one place.");

        group.MapDelete("/reorder-policies/{itemNo}/{locationCode}", RemoveReorderPolicyAsync)
             .WithName("RemoveReorderPolicy")
             .WithSummary("Leaves a place with no rule for that item.");

        group.MapGet("/items", ItemsAsync)
             .WithName("Items")
             .WithSummary("Lists the items in the active company.");

        group.MapGet("/locations", LocationsAsync)
             .WithName("Locations")
             .WithSummary("Lists the locations stock can be held at.");

        group.MapGet("/reports/valuation", ValuationReportAsync)
             .WithName("InventoryValuationReport")
             .WithSummary("What the stock was worth on a day, built from the same rows as the account.");

        group.MapGet("/reports/ageing", AgeingReportAsync)
             .WithName("InventoryAgeingReport")
             .WithSummary("How long the stock on hand has been sitting, in bands.");

        group.MapGet("/reports/velocity", VelocityReportAsync)
             .WithName("InventoryVelocityReport")
             .WithSummary("How fast each item moves, slowest first.");

        group.MapGet("/stock/available", AvailableAsync)
             .WithName("StockAvailable")
             .WithSummary("What is on hand, what is promised, and what is left to promise.");

        group.MapGet("/reservations", ReservationsAsync)
             .WithName("StockReservations")
             .WithSummary("What stock is being held, and for what.");

        group.MapPost("/reservations", ReserveAsync)
             .WithName("ReserveStock")
             .WithSummary("Holds stock for a document. Moves nothing.");

        group.MapPost("/reservations/{documentNo}/release", ReleaseReservationAsync)
             .WithName("ReleaseStock")
             .WithSummary("Lets held stock go, and keeps the record of what was held.");

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

        group.MapGet("/adjustment-reasons", ReasonsAsync)
             .WithName("AdjustmentReasons")
             .WithSummary("The reasons this company adjusts stock for.");

        group.MapPost("/adjustment-reasons", SaveReasonAsync)
             .WithName("SaveAdjustmentReason")
             .WithSummary("Adds an adjustment reason, or changes one already there.");

        group.MapGet("/reports/shrinkage", ShrinkageAsync)
             .WithName("ShrinkageReport")
             .WithSummary("What was adjusted under each reason, and what it was worth.");

        group.MapGet("/categories", CategoriesAsync)
             .WithName("ItemCategories")
             .WithSummary("The categories items are grouped under, and the accounts each posts to.");

        group.MapPost("/categories", SaveCategoryAsync)
             .WithName("SaveItemCategory")
             .WithSummary("Adds an item category, or changes one already there.");

        group.MapPost("/items/{itemNo}/category", SetItemCategoryAsync)
             .WithName("SetItemCategory")
             .WithSummary("Moves an item into a category. What already posted keeps its accounts.");

        group.MapGet("/reports/posting-gaps", PostingGapsAsync)
             .WithName("CategoryPostingGaps")
             .WithSummary("Which categories are not reaching the ledger, and what that has cost.");

        group.MapGet("/items/{itemNo}/variants", VariantsAsync)
             .WithName("ItemVariants")
             .WithSummary("The colours, sizes and flavours an item is stocked as.");

        group.MapPost("/items/{itemNo}/variants", SaveVariantAsync)
             .WithName("SaveItemVariant")
             .WithSummary("Adds a variant to an item, or changes one already there.");

        group.MapPost("/items/{itemNo}/has-variants", SetHasVariantsAsync)
             .WithName("SetItemHasVariants")
             .WithSummary("Turns variants on or off for an item.");

        group.MapGet("/items/{itemNo}/variant-stock", VariantStockAsync)
             .WithName("VariantStock")
             .WithSummary("What each variant is holding, by location.");

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
    private static async Task<IResult> ValuationReportAsync(
        InventoryReportService reports,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? asOf = null,
        [FromQuery] string? itemNo = null,
        [FromQuery] string? locationCode = null)
    {
        if (!Can(user, "Inventory.Report.Read"))
        {
            return Forbidden("Inventory.Report.Read", "read a stock valuation", http);
        }

        return Results.Ok(await reports
            .ValuationAsync(asOf ?? clock.Today, itemNo, locationCode, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> AgeingReportAsync(
        InventoryReportService reports,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? asOf = null,
        [FromQuery] string? itemNo = null,
        [FromQuery] string? locationCode = null)
    {
        if (!Can(user, "Inventory.Report.Read"))
        {
            return Forbidden("Inventory.Report.Read", "read stock ageing", http);
        }

        return Results.Ok(await reports
            .AgeingAsync(asOf ?? clock.Today, itemNo, locationCode, null, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> VelocityReportAsync(
        InventoryReportService reports,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        if (!Can(user, "Inventory.Report.Read"))
        {
            return Forbidden("Inventory.Report.Read", "read stock velocity", http);
        }

        var last = to ?? clock.Today;

        return Results.Ok(await reports
            .VelocityAsync(from ?? last.AddMonths(-3), last, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> AvailableAsync(
        StockReservationService reservations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] string? itemNo = null,
        [FromQuery] string? locationCode = null)
    {
        if (!user.IsSuperUser && !user.Has("Inventory.Reservation.Read"))
        {
            return Results.Json(
                AsapProblem.Forbidden("Inventory.Reservation.Read", "view what stock is free", http.Request.Path),
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await reservations
            .AvailabilityAsync(itemNo, locationCode, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> ReservationsAsync(
        StockReservationService reservations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] string? documentNo = null,
        [FromQuery] string? itemNo = null,
        [FromQuery] bool outstandingOnly = true)
    {
        if (!user.IsSuperUser && !user.Has("Inventory.Reservation.Read"))
        {
            return Results.Json(
                AsapProblem.Forbidden("Inventory.Reservation.Read", "view reservations", http.Request.Path),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var held = await reservations
            .ListAsync(documentNo, itemNo, outstandingOnly, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(held.Select(static r => new StockReservationRow(
            r.ItemNo,
            r.VariantCode,
            r.LocationCode,
            r.DocumentNo,
            r.DocumentLineNo,
            r.SourceCode,
            r.Quantity,
            r.QuantityOutstanding,
            r.QuantityFulfilled,
            r.ReleaseReason,
            r.Note)));
    }

    private static async Task<IResult> ReserveAsync(
        ReserveStockRequest request,
        StockReservationService reservations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!user.IsSuperUser && !user.Has("Inventory.Reservation.Update"))
        {
            return Results.Json(
                AsapProblem.Forbidden("Inventory.Reservation.Update", "hold stock", http.Request.Path),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await reservations
            .ReserveAsync(
                request.ItemNo,
                request.LocationCode,
                request.Quantity,
                request.DocumentNo,
                request.DocumentLineNo,
                request.VariantCode,
                request.SourceCode,
                request.Note,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Results.Json(
                AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
                statusCode: AsapProblem.StatusFor(result.Messages))
            : Results.Ok(new StockReservationRow(
                result.Value.ItemNo,
                result.Value.VariantCode,
                result.Value.LocationCode,
                result.Value.DocumentNo,
                result.Value.DocumentLineNo,
                result.Value.SourceCode,
                result.Value.Quantity,
                result.Value.QuantityOutstanding,
                result.Value.QuantityFulfilled,
                result.Value.ReleaseReason,
                result.Value.Note));
    }

    private static async Task<IResult> ReleaseReservationAsync(
        string documentNo,
        ReleaseStockRequest request,
        StockReservationService reservations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] int? lineNo = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!user.IsSuperUser && !user.Has("Inventory.Reservation.Update"))
        {
            return Results.Json(
                AsapProblem.Forbidden("Inventory.Reservation.Update", "release stock", http.Request.Path),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var released = await reservations
            .ReleaseAsync(documentNo, lineNo, request.Reason, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { released });
    }

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

        var reasonRequired = await setup
            .GetAsync<bool>($"{InventoryModule.Id}.Adjustment.ReasonRequired", cancellationToken)
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
                cancellationToken,
                reasonRequired)
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
                result.Value.BaseUnitCode,
                result.Value.VariantCode,
                result.Value.VariantDescription));
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
        string locationCode,
        string? variantCode = null)
    {
        if (!Can(user, "Inventory.Stock.Read"))
        {
            return Forbidden("Inventory.Stock.Read", "read a stock valuation", http);
        }

        var result = await revaluation
            .ValuationAsync(itemNo, locationCode, variantCode, cancellationToken)
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
                request.VariantCode,
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

    private static async Task<IResult> ReasonsAsync(
        AdjustmentReasonService reasons,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        bool includeWithdrawn = false)
    {
        if (!Can(user, "Inventory.AdjustmentReason.Read"))
        {
            return Forbidden("Inventory.AdjustmentReason.Read", "view adjustment reasons", http);
        }

        var rows = await reasons.ReasonsAsync(includeWithdrawn, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static r => new AdjustmentReasonView(
            r.Code,
            r.Name,
            r.NameArabic,
            r.ContraAccountNo,
            r.Direction.ToString(),
            r.RequiresNote,
            r.IsActive)));
    }

    private static async Task<IResult> SaveReasonAsync(
        SaveAdjustmentReasonRequest request,
        AdjustmentReasonService reasons,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.AdjustmentReason.Update"))
        {
            return Forbidden("Inventory.AdjustmentReason.Update", "maintain adjustment reasons", http);
        }

        var result = await reasons
            .SaveAsync(
                new AdjustmentReasonRequest(
                    request.Code,
                    request.Name,
                    request.NameArabic,
                    request.ContraAccountNo,
                    request.Direction,
                    request.RequiresNote,
                    request.IsActive),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new AdjustmentReasonView(
                result.Value.Code,
                result.Value.Name,
                result.Value.NameArabic,
                result.Value.ContraAccountNo,
                result.Value.Direction.ToString(),
                result.Value.RequiresNote,
                result.Value.IsActive));
    }

    private static async Task<IResult> ShrinkageAsync(
        AdjustmentReasonService reasons,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        string? locationCode = null)
    {
        if (!Can(user, "Inventory.Stock.Read"))
        {
            return Forbidden("Inventory.Stock.Read", "read the shrinkage report", http);
        }

        var last = to ?? clock.Today;
        var first = from ?? last.AddMonths(-1);

        var rows = await reasons
            .ShrinkageAsync(first, last, locationCode, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(rows.Select(static r => new ShrinkageView(
            r.ReasonCode,
            r.ReasonName,
            r.ReasonNameArabic,
            r.EntryCount,
            r.Quantity,
            r.CostAmount)));
    }

    private static async Task<IResult> CategoriesAsync(
        ItemCategoryService categories,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Category.Read"))
        {
            return Forbidden("Inventory.Category.Read", "view item categories", http);
        }

        var rows = await categories.CategoriesAsync(cancellationToken).ConfigureAwait(false);

        // The parent is shown by its code rather than its key, because a code is the only part of
        // a category a person ever types or reads.
        var byId = rows.ToDictionary(static c => c.Id, static c => c.Code);

        var counts = await context.Set<Item>()
            .AsNoTracking()
            .Where(static i => i.CategoryId != null)
            .GroupBy(static i => i.CategoryId)
            .Select(static g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(static g => g.CategoryId!.Value, static g => g.Count, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(rows.Select(c => new ItemCategoryView(
            c.Code,
            c.Name,
            c.NameArabic,
            c.ParentId is { } parent ? byId.GetValueOrDefault(parent) : null,
            c.InventoryAccountNo,
            c.CostOfGoodsSoldAccountNo,
            c.SalesAccountNo,
            c.VarianceAccountNo,
            counts.GetValueOrDefault(c.Id))));
    }

    private static async Task<IResult> SaveCategoryAsync(
        SaveItemCategoryRequest request,
        ItemCategoryService categories,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Category.Update"))
        {
            return Forbidden("Inventory.Category.Update", "maintain item categories", http);
        }

        var result = await categories
            .SaveAsync(
                new ItemCategoryRequest(
                    request.Code,
                    request.Name,
                    request.NameArabic,
                    request.ParentCode,
                    request.InventoryAccountNo,
                    request.CostOfGoodsSoldAccountNo,
                    request.SalesAccountNo,
                    request.VarianceAccountNo),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new ItemCategoryView(
                result.Value.Code,
                result.Value.Name,
                result.Value.NameArabic,
                request.ParentCode,
                result.Value.InventoryAccountNo,
                result.Value.CostOfGoodsSoldAccountNo,
                result.Value.SalesAccountNo,
                result.Value.VarianceAccountNo,
                0));
    }

    private static async Task<IResult> SetItemCategoryAsync(
        string itemNo,
        SetItemCategoryRequest request,
        ItemCategoryService categories,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Category.Update"))
        {
            return Forbidden("Inventory.Category.Update", "move an item between categories", http);
        }

        var result = await categories
            .SetCategoryAsync(itemNo, request.CategoryCode, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new { itemNo = result.Value.No, categoryCode = request.CategoryCode });
    }

    private static async Task<IResult> PostingGapsAsync(
        ItemCategoryService categories,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Category.Read"))
        {
            return Forbidden("Inventory.Category.Read", "see which categories are not posting", http);
        }

        var rows = await categories.GapsAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static g => new CategoryPostingGapView(
            g.Code,
            g.Name,
            g.NameArabic,
            g.ItemCount,
            g.MissingAccounts,
            g.UnpostedValue,
            g.UnpostedEntryCount)));
    }

    private static async Task<IResult> VariantsAsync(
        string itemNo,
        ItemVariantService variants,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Variant.Read"))
        {
            return Forbidden("Inventory.Variant.Read", "view item variants", http);
        }

        var rows = await variants.VariantsAsync(itemNo, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static v => new ItemVariantView(
            v.Code,
            v.Description,
            v.DescriptionArabic,
            v.Barcode,
            v.SortOrder,
            v.IsBlocked)));
    }

    private static async Task<IResult> SaveVariantAsync(
        string itemNo,
        SaveItemVariantRequest request,
        ItemVariantService variants,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Variant.Update"))
        {
            return Forbidden("Inventory.Variant.Update", "maintain item variants", http);
        }

        var result = await variants
            .SaveAsync(
                itemNo,
                new ItemVariantRequest(
                    request.Code,
                    request.Description,
                    request.DescriptionArabic,
                    request.Barcode,
                    request.SortOrder,
                    request.IsBlocked),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new ItemVariantView(
                result.Value.Code,
                result.Value.Description,
                result.Value.DescriptionArabic,
                result.Value.Barcode,
                result.Value.SortOrder,
                result.Value.IsBlocked));
    }

    private static async Task<IResult> SetHasVariantsAsync(
        string itemNo,
        SetHasVariantsRequest request,
        ItemVariantService variants,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Variant.Update"))
        {
            return Forbidden("Inventory.Variant.Update", "turn variants on or off", http);
        }

        var result = await variants
            .SetHasVariantsAsync(itemNo, request.HasVariants, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new { itemNo = result.Value.No, hasVariants = result.Value.HasVariants });
    }

    private static async Task<IResult> VariantStockAsync(
        string itemNo,
        ItemVariantService variants,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Stock.Read"))
        {
            return Forbidden("Inventory.Stock.Read", "read stock by variant", http);
        }

        var rows = await variants.StockAsync(itemNo, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static r => new VariantStockView(
            r.VariantCode,
            r.Description,
            r.DescriptionArabic,
            r.LocationCode,
            r.Quantity)));
    }

    private static async Task<IResult> BinMovementsAsync(
        BinMovementService movements,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] string? locationCode = null,
        [FromQuery] int take = 50)
    {
        if (!Can(user, "Inventory.Item.Read"))
        {
            return Forbidden("Inventory.Item.Read", "view bin movements", http);
        }

        var rows = await movements.ListAsync(locationCode, take, cancellationToken).ConfigureAwait(false);

        return Results.Ok(await ViewsOfAsync(rows, context, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> PostBinMovementAsync(
        PostBinMovementRequest request,
        BinMovementService movements,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Item.Update"))
        {
            return Forbidden("Inventory.Item.Update", "move goods between shelves", http);
        }

        var result = await movements
            .PostAsync(request.LocationCode, request.Lines, request.MovementDate, request.Note, cancellationToken)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return Refused(result, http);
        }

        var views = await ViewsOfAsync([result.Value], context, cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            movement = views[0],
            messages = MessagePayload.FromAll(result.Messages),
        });
    }

    /// <summary>Adds the item names, so a sheet reads without a lookup per line.</summary>
    private static async Task<IReadOnlyList<BinMovementView>> ViewsOfAsync(
        IReadOnlyList<BinMovement> movements,
        AsapDbContext context,
        CancellationToken cancellationToken)
    {
        var itemNos = movements
            .SelectMany(static m => m.Lines)
            .Select(static l => l.ItemNo)
            .Distinct()
            .ToList();

        var names = await context.Set<Item>()
            .AsNoTracking()
            .Where(i => itemNos.Contains(i.No))
            .ToDictionaryAsync(static i => i.No, static i => i.Description, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. movements.Select(m => new BinMovementView(
                m.No,
                m.LocationCode,
                m.MovementDate,
                m.Status,
                m.Note,
                m.RecordedByUserName,
                m.TransactionNo,
                [
                    .. m.Lines
                        .OrderBy(static l => l.LineNo)
                        .Select(l => new BinMovementLineView(
                            l.LineNo,
                            l.ItemNo,
                            names.GetValueOrDefault(l.ItemNo),
                            l.VariantCode,
                            l.FromBinCode,
                            l.ToBinCode,
                            l.Quantity)),
                ])),
        ];
    }

    private static async Task<IResult> ReorderPoliciesAsync(
        ReorderPolicyService policies,
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] string? locationCode = null,
        [FromQuery] bool activeOnly = false)
    {
        if (!Can(user, "Inventory.Item.Read"))
        {
            return Forbidden("Inventory.Item.Read", "view reorder policies", http);
        }

        var rows = await policies.ListAsync(locationCode, activeOnly, cancellationToken)
            .ConfigureAwait(false);

        var itemNos = rows.Select(static r => r.ItemNo).Distinct().ToList();

        var names = await context.Set<Item>()
            .AsNoTracking()
            .Where(i => itemNos.Contains(i.No))
            .ToDictionaryAsync(static i => i.No, static i => i.Description, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(rows.Select(r => ViewOf(r, names.GetValueOrDefault(r.ItemNo))));
    }

    private static async Task<IResult> SaveReorderPolicyAsync(
        string itemNo,
        string locationCode,
        ReorderPolicyRequest request,
        ReorderPolicyService policies,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Inventory.Item.Update"))
        {
            return Forbidden("Inventory.Item.Update", "maintain reorder policies", http);
        }

        var result = await policies
            .SaveAsync(
                request with { ItemNo = itemNo, LocationCode = locationCode },
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(ViewOf(result.Value, null));
    }

    private static async Task<IResult> RemoveReorderPolicyAsync(
        string itemNo,
        string locationCode,
        ReorderPolicyService policies,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Inventory.Item.Update"))
        {
            return Forbidden("Inventory.Item.Update", "maintain reorder policies", http);
        }

        var result = await policies.RemoveAsync(itemNo, locationCode, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.NoContent();
    }

    private static ReorderPolicyView ViewOf(ReorderPolicy policy, string? itemName)
        => new(
            policy.ItemNo,
            itemName ?? policy.ItemNo,
            policy.LocationCode,
            policy.VariantCode,
            policy.Kind,
            policy.ReorderPoint,
            policy.ReorderQuantity,
            policy.MaximumInventory,
            policy.MinimumOrderQuantity,
            policy.OrderMultiple,
            policy.LeadTimeDays,
            policy.VendorNo,
            policy.IsActive);

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
