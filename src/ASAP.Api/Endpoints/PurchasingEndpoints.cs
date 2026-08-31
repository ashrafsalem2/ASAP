using ASAP.Api.Infrastructure;
using ASAP.Modules.Purchasing;
using ASAP.Modules.Purchasing.Approvals;
using ASAP.Modules.Purchasing.Costing;
using ASAP.Modules.Purchasing.Orders;
using ASAP.Modules.Purchasing.Quotations;
using ASAP.Modules.Purchasing.Requisitions;
using ASAP.Modules.Purchasing.Reporting;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>A charge to spread across the goods an order brought in.</summary>
/// <param name="Amount">The charge, from the carrier's invoice.</param>
/// <param name="Basis">Whether to spread it by value or by quantity.</param>
/// <param name="ContraAccountNo">What it posts against -- the accrual the carrier is paid from.</param>
/// <param name="PostingDate">The date to report it in. Defaults to today.</param>
/// <param name="Description">What it was for.</param>
public sealed record LandedCostRequest(
    decimal Amount,
    LandedCostBasis Basis = LandedCostBasis.ByValue,
    string ContraAccountNo = "",
    DateOnly? PostingDate = null,
    string? Description = null);

/// <summary>Why an order is being turned down.</summary>
/// <param name="Reason">What was wrong, which the buyer will read.</param>
public sealed record RejectOrderRequest(string? Reason);

/// <summary>How much one person may sign a purchase order for.</summary>
/// <param name="UserId">The person.</param>
/// <param name="UserName">Their user name.</param>
/// <param name="DisplayName">What they are called.</param>
/// <param name="MaximumAmount">The most they may approve, on one order.</param>
/// <param name="IsActive">Whether the limit is still in force.</param>
public sealed record ApprovalLimitView(
    Guid UserId,
    string UserName,
    string? DisplayName,
    decimal MaximumAmount,
    bool IsActive);

/// <summary>An approval limit as a client sends it.</summary>
/// <param name="UserId">The person.</param>
/// <param name="UserName">Their user name.</param>
/// <param name="DisplayName">What they are called.</param>
/// <param name="MaximumAmount">The most they may approve, on one order.</param>
/// <param name="IsActive">Whether the limit is still in force.</param>
public sealed record SetApprovalLimitRequest(
    Guid UserId,
    string UserName,
    string? DisplayName = null,
    decimal MaximumAmount = 0m,
    bool IsActive = true);

/// <summary>One line on a new purchase order, as a client sends it.</summary>
/// <param name="Type">Whether it buys stock or a cost.</param>
/// <param name="No">The item number, or the account number on a cost line.</param>
/// <param name="Quantity">How much to order.</param>
/// <param name="DirectUnitCost">The agreed price per unit, before tax.</param>
/// <param name="Description">What it is.</param>
/// <param name="TaxCode">The tax the vendor will charge.</param>
/// <param name="LocationCode">Where this line's goods go, when it differs from the order.</param>
/// <param name="VariantCode">Which variant of the item, where the item has them.</param>
public sealed record PurchaseLinePayload(
    PurchaseLineType Type,
    string No,
    decimal Quantity,
    decimal DirectUnitCost,
    string? Description = null,
    string? TaxCode = null,
    string? LocationCode = null,
    string? VariantCode = null);

/// <summary>What a client sends to ask several vendors what something would cost.</summary>
/// <param name="Lines">What to ask about.</param>
/// <param name="LocationCode">Where the goods are wanted.</param>
/// <param name="RespondByDate">When answers are wanted by.</param>
/// <param name="NeededByDate">When the goods are wanted.</param>
/// <param name="Description">What it is for.</param>
/// <param name="RequisitionNo">The requisition it arose from.</param>
public sealed record CreateQuotationRequest(
    IReadOnlyList<QuotationLineRequest> Lines,
    string? LocationCode = null,
    DateOnly? RespondByDate = null,
    DateOnly? NeededByDate = null,
    string? Description = null,
    string? RequisitionNo = null);

/// <summary>What a client sends to add vendors to a request.</summary>
/// <param name="VendorNos">Who to ask.</param>
public sealed record InviteVendorsRequest(IReadOnlyList<string> VendorNos);

/// <summary>What a client sends to record one vendor's answer.</summary>
/// <param name="VendorNo">Who answered.</param>
/// <param name="Lines">What they said about each line.</param>
public sealed record RecordQuoteRequest(
    string VendorNo,
    IReadOnlyList<QuotationResponseLine> Lines);

/// <summary>What a client sends to record that a vendor is not quoting.</summary>
/// <param name="VendorNo">Who said no.</param>
/// <param name="Reason">Why.</param>
public sealed record DeclineToQuoteRequest(string VendorNo, string? Reason = null);

/// <summary>What a client sends to decide which vendor wins which line.</summary>
/// <param name="Awards">Who wins each line, and why where a reason is needed.</param>
public sealed record AwardQuotationRequest(IReadOnlyList<QuotationAward> Awards);

/// <summary>What a client sends to turn one vendor's awards into an order.</summary>
/// <param name="VendorNo">Whose awarded lines to order.</param>
/// <param name="ExpectedReceiptDate">When the goods are expected.</param>
public sealed record OrderFromQuotationRequest(string VendorNo, DateOnly? ExpectedReceiptDate = null);

/// <summary>A vendor who was asked, as it is reported back.</summary>
/// <param name="VendorNo">Who.</param>
/// <param name="VendorName">Their name.</param>
/// <param name="HasAnswered">Whether they answered at all.</param>
/// <param name="DeclinedReason">Why they said no, where they said so.</param>
public sealed record QuotationInvitationView(
    string VendorNo,
    string VendorName,
    bool HasAnswered,
    string? DeclinedReason);

/// <summary>A request for quotation as it is reported back.</summary>
/// <param name="No">Its number.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="RequestDate">The day it was raised.</param>
/// <param name="RespondByDate">When answers are wanted by.</param>
/// <param name="NeededByDate">When the goods are wanted.</param>
/// <param name="LocationCode">Where they are wanted.</param>
/// <param name="RequisitionNo">The requisition it arose from.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Invitations">Who was asked, and whether they answered.</param>
/// <param name="Comparison">Every line with every quote for it.</param>
public sealed record QuotationRequestView(
    string No,
    string Status,
    DateOnly RequestDate,
    DateOnly? RespondByDate,
    DateOnly? NeededByDate,
    string? LocationCode,
    string? RequisitionNo,
    string? Description,
    IReadOnlyList<QuotationInvitationView> Invitations,
    IReadOnlyList<QuotationComparisonRow> Comparison);

/// <summary>One thing asked for on a new requisition, as a client sends it.</summary>
/// <param name="Type">Whether it asks for stock or a cost.</param>
/// <param name="No">The item number, or the account number on a cost line.</param>
/// <param name="Quantity">How much is wanted.</param>
/// <param name="EstimatedUnitCost">What whoever is asking thinks it costs.</param>
/// <param name="Description">What is wanted, in words.</param>
/// <param name="LocationCode">Where it is wanted.</param>
/// <param name="VariantCode">Which variant, on an item that has them.</param>
/// <param name="SuggestedVendorNo">A vendor somebody has in mind. A suggestion, not a commitment.</param>
public sealed record RequisitionLinePayload(
    PurchaseLineType Type,
    string No,
    decimal Quantity,
    decimal EstimatedUnitCost = 0m,
    string? Description = null,
    string? LocationCode = null,
    string? VariantCode = null,
    string? SuggestedVendorNo = null);

/// <summary>What a client sends to ask for something to be bought.</summary>
/// <param name="Lines">What is being asked for.</param>
/// <param name="LocationCode">Where the goods are wanted.</param>
/// <param name="NeededByDate">When they are wanted by.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Justification">Why it is needed, which is what an approver reads.</param>
public sealed record CreateRequisitionRequest(
    IReadOnlyList<RequisitionLinePayload> Lines,
    string? LocationCode = null,
    DateOnly? NeededByDate = null,
    string? Description = null,
    string? Justification = null);

/// <summary>What a client sends to turn part of a requisition into an order.</summary>
/// <param name="VendorNo">Who to buy from.</param>
/// <param name="Lines">Which lines and at what price, or null for everything left at its estimate.</param>
/// <param name="ExpectedReceiptDate">When the goods are expected.</param>
public sealed record OrderFromRequisitionRequest(
    string VendorNo,
    IReadOnlyList<RequisitionOrderLineRequest>? Lines = null,
    DateOnly? ExpectedReceiptDate = null);

/// <summary>What a client sends to turn a requisition down or abandon it.</summary>
/// <param name="Reason">Why.</param>
public sealed record RequisitionReasonRequest(string? Reason = null);

/// <summary>One line of a requisition as it is reported back.</summary>
/// <param name="LineNo">Its position.</param>
/// <param name="Type">Whether it asks for stock or a cost.</param>
/// <param name="No">The item or account number.</param>
/// <param name="VariantCode">Which variant, where the item has them.</param>
/// <param name="Description">What is wanted.</param>
/// <param name="LocationCode">Where it is wanted.</param>
/// <param name="Quantity">How much is wanted.</param>
/// <param name="EstimatedUnitCost">What somebody thinks it costs.</param>
/// <param name="EstimatedAmount">What the line is estimated at.</param>
/// <param name="QuantityOrdered">How much has been turned into an order.</param>
/// <param name="OutstandingToOrder">How much is still waiting to be ordered.</param>
/// <param name="SuggestedVendorNo">The vendor somebody suggested.</param>
public sealed record RequisitionLineView(
    int LineNo,
    string Type,
    string? No,
    string? VariantCode,
    string Description,
    string? LocationCode,
    decimal Quantity,
    decimal EstimatedUnitCost,
    decimal EstimatedAmount,
    decimal QuantityOrdered,
    decimal OutstandingToOrder,
    string? SuggestedVendorNo);

/// <summary>A requisition as it is reported back.</summary>
/// <param name="No">Its number.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="RequisitionDate">The day it was raised.</param>
/// <param name="NeededByDate">When the goods are wanted by.</param>
/// <param name="LocationCode">Where they are wanted.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Justification">Why it is needed.</param>
/// <param name="RequestedByUserName">Who asked.</param>
/// <param name="ApprovedByUserName">Who signed, or turned it down.</param>
/// <param name="ApprovedAtUtc">When.</param>
/// <param name="ApprovedAmount">What it was estimated at when it was signed for.</param>
/// <param name="RejectionReason">Why it was turned down, where it was.</param>
/// <param name="EstimatedAmount">What it is estimated at now.</param>
/// <param name="IsEditable">Whether its lines may still be changed.</param>
/// <param name="CanBeOrdered">Whether orders may still be raised from it.</param>
/// <param name="Lines">What is being asked for.</param>
public sealed record RequisitionView(
    string No,
    string Status,
    DateOnly RequisitionDate,
    DateOnly? NeededByDate,
    string? LocationCode,
    string? Description,
    string? Justification,
    string? RequestedByUserName,
    string? ApprovedByUserName,
    DateTime? ApprovedAtUtc,
    decimal? ApprovedAmount,
    string? RejectionReason,
    decimal EstimatedAmount,
    bool IsEditable,
    bool CanBeOrdered,
    IReadOnlyList<RequisitionLineView> Lines);

/// <summary>What a client sends to send goods back to a vendor.</summary>
/// <param name="Lines">How much of each line is going back, or null for everything that still could.</param>
/// <param name="Reason">Why they are going back.</param>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record PurchaseReturnRequest(
    IReadOnlyList<PurchaseReturnLineRequest>? Lines = null,
    string? Reason = null,
    string? OverrideReason = null);

/// <summary>What a client sends to raise a purchase order.</summary>
/// <param name="VendorNo">Who it is being ordered from.</param>
/// <param name="Lines">What is being bought.</param>
/// <param name="LocationCode">Where the goods are going.</param>
/// <param name="ExpectedReceiptDate">When they are expected.</param>
/// <param name="Description">A note for whoever handles it.</param>
/// <param name="VendorOrderNo">The vendor's own reference.</param>
public sealed record CreatePurchaseOrderRequest(
    string VendorNo,
    IReadOnlyList<PurchaseLinePayload> Lines,
    string? LocationCode = null,
    DateOnly? ExpectedReceiptDate = null,
    string? Description = null,
    string? VendorOrderNo = null);

/// <summary>What a client sends to receive goods.</summary>
/// <param name="Lines">How much of each line arrived, or null for everything outstanding.</param>
/// <param name="VendorDeliveryNo">The number on the vendor's delivery note.</param>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record ReceiveGoodsRequest(
    IReadOnlyList<ReceiptLineRequest>? Lines = null,
    string? VendorDeliveryNo = null,
    string? OverrideReason = null);

/// <summary>What a client sends to post a vendor invoice.</summary>
/// <param name="VendorInvoiceNo">The number on the vendor's own invoice.</param>
/// <param name="Lines">What the invoice covers, or null for everything awaiting one.</param>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record PostPurchaseInvoiceRequest(
    string VendorInvoiceNo,
    IReadOnlyList<InvoiceLineRequest>? Lines = null,
    string? OverrideReason = null);

/// <summary>One line of a purchase order as it is reported back.</summary>
/// <param name="LineNo">Its position.</param>
/// <param name="Type">Whether it buys stock or a cost.</param>
/// <param name="No">The item or account number.</param>
/// <param name="Description">What it is.</param>
/// <param name="LocationCode">Where its goods go.</param>
/// <param name="Quantity">How much was ordered.</param>
/// <param name="DirectUnitCost">The agreed price per unit.</param>
/// <param name="TaxCode">The tax the vendor charges.</param>
/// <param name="LineAmount">What the line comes to before tax.</param>
/// <param name="QuantityReceived">How much has arrived.</param>
/// <param name="QuantityInvoiced">How much has been invoiced.</param>
/// <param name="OutstandingToReceive">How much is still to arrive.</param>
/// <param name="QuantityReturned">How much has gone back to the vendor.</param>
/// <param name="ReturnableQuantity">How much could still go back.</param>
/// <param name="ReceivedNotInvoiced">How much has arrived and is still awaiting an invoice.</param>
/// <param name="VariantCode">Which variant of the item, where the item has them.</param>
public sealed record PurchaseOrderLineView(
    int LineNo,
    string Type,
    string? No,
    string Description,
    string? LocationCode,
    decimal Quantity,
    decimal DirectUnitCost,
    string? TaxCode,
    decimal LineAmount,
    decimal QuantityReceived,
    decimal QuantityInvoiced,
    decimal OutstandingToReceive,
    decimal ReceivedNotInvoiced,
    decimal QuantityReturned,
    decimal ReturnableQuantity,
    string? VariantCode = null);

/// <summary>A purchase order as it is reported back.</summary>
/// <param name="No">Its number.</param>
/// <param name="VendorNo">Who it was ordered from.</param>
/// <param name="VendorName">Their name at the time.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="OrderDate">When it was placed.</param>
/// <param name="ExpectedReceiptDate">When the goods are expected.</param>
/// <param name="LocationCode">Where they are going.</param>
/// <param name="VendorOrderNo">The vendor's own reference.</param>
/// <param name="Description">The note on it.</param>
/// <param name="TotalAmount">What it comes to before tax.</param>
/// <param name="IsEditable">Whether lines may still be changed.</param>
/// <param name="Lines">What is on it.</param>
public sealed record PurchaseOrderView(
    string No,
    string VendorNo,
    string VendorName,
    string Status,
    DateOnly OrderDate,
    DateOnly? ExpectedReceiptDate,
    string? LocationCode,
    string? VendorOrderNo,
    string? Description,
    decimal TotalAmount,
    bool IsEditable,
    IReadOnlyList<PurchaseOrderLineView> Lines)
{
    /// <summary>Who signed for it, on an order that needed signing.</summary>
    public string? ApprovedBy { get; init; }

    /// <summary>When they signed.</summary>
    public DateTime? ApprovedAtUtc { get; init; }

    /// <summary>What it came to when they signed, which is what they signed for.</summary>
    public decimal? ApprovedAmount { get; init; }

    /// <summary>Why it was turned down, where it was.</summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// What the server said, where it said anything.
    /// </summary>
    /// <remarks>
    /// Carried on the view rather than dropped, because the interesting outcome of a release is a
    /// thing that succeeded and still needs saying: an order that went for approval rather than to
    /// the vendor looks identical to one that went out, unless somebody is told.
    /// </remarks>
    public IReadOnlyList<MessageView> Messages { get; init; } = [];
}

/// <summary>Something the server said about a document.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Title">The short form.</param>
/// <param name="Detail">What happened.</param>
/// <param name="Resolution">What to do about it.</param>
public sealed record MessageView(
    string Code,
    string Severity,
    string Title,
    string? Detail,
    string? Resolution);

/// <summary>Purchase orders, goods receipts and vendor invoices.</summary>
public static class PurchasingEndpoints
{
    private const string ReadPermission = "Purchasing.Order.Read";
    private const string CreatePermission = "Purchasing.Order.Create";
    private const string ReceivePermission = "Purchasing.Receipt.Post";
    private const string InvoicePermission = "Purchasing.Invoice.Post";
    private const string OverridePermission = "Purchasing.Order.Override";

    /// <summary>Maps the Purchasing endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPurchasingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/purchasing").RequireAuthorization().WithTags("Purchasing");

        group.MapGet("/orders", ListAsync)
             .WithName("PurchaseOrders")
             .WithSummary("Lists purchase orders, most recently raised first.");

        group.MapGet("/orders/{orderNo}", GetAsync)
             .WithName("PurchaseOrder")
             .WithSummary("Reads one purchase order and its lines.");

        group.MapPost("/orders", CreateAsync)
             .WithName("CreatePurchaseOrder")
             .WithSummary("Raises a purchase order. Nothing posts until goods arrive.");

        group.MapPost("/orders/{orderNo}/release", ReleaseAsync)
             .WithName("ReleasePurchaseOrder")
             .WithSummary("Marks an order as sent to the vendor.");

        group.MapPost("/orders/{orderNo}/approve", ApproveAsync)
             .WithName("ApprovePurchaseOrder")
             .WithSummary("Signs for an order, up to your limit and never one you raised.");

        group.MapPost("/orders/{orderNo}/reject", RejectAsync)
             .WithName("RejectPurchaseOrder")
             .WithSummary("Turns an order down, with a reason the buyer will read.");

        group.MapGet("/approval-limits", ApprovalLimitsAsync)
             .WithName("PurchaseApprovalLimits")
             .WithSummary("How much each person may sign a purchase order for.");

        group.MapPost("/approval-limits", SetApprovalLimitAsync)
             .WithName("SetPurchaseApprovalLimit")
             .WithSummary("Sets what one person may approve.");

        group.MapPost("/orders/{orderNo}/landed-cost", LandedCostAsync)
             .WithName("ApplyLandedCost")
             .WithSummary("Adds freight or duty to the cost of the goods received against an order.");

        group.MapGet("/reports/open-orders", OpenOrdersAsync)
             .WithName("OpenPurchaseOrders")
             .WithSummary("What is on order and has not arrived, latest first.");

        group.MapGet("/reports/vendor-performance", VendorPerformanceAsync)
             .WithName("VendorPerformance")
             .WithSummary("How each vendor has actually behaved over a period.");

        group.MapGet("/reports/purchase-analysis", PurchaseAnalysisAsync)
             .WithName("PurchaseAnalysis")
             .WithSummary("What was bought over a period, by vendor or by item.");

        group.MapGet("/quotations", ListQuotationsAsync)
             .WithName("ListQuotationRequests")
             .WithSummary("What vendors have been asked about, newest first.");



        group.MapGet("/quotations/{requestNo}", GetQuotationAsync)
             .WithName("GetQuotationRequest")
             .WithSummary("One request, with every quote for every line side by side.");



        group.MapPost("/quotations", CreateQuotationAsync)
             .WithName("CreateQuotationRequest")
             .WithSummary("Asks several vendors what something would cost. Commits nothing.");



        group.MapPost("/quotations/{requestNo}/invite", InviteVendorsAsync)
             .WithName("InviteQuotationVendors")
             .WithSummary("Adds vendors to ask.");



        group.MapPost("/quotations/{requestNo}/send", SendQuotationAsync)
             .WithName("SendQuotationRequest")
             .WithSummary("Marks the request as gone out to the vendors.");



        group.MapPost("/quotations/{requestNo}/quote", RecordQuoteAsync)
             .WithName("RecordQuotation")
             .WithSummary("Records what one vendor said. Only from a vendor who was asked.");



        group.MapPost("/quotations/{requestNo}/decline", DeclineQuotationAsync)
             .WithName("DeclineQuotation")
             .WithSummary("Records that a vendor is not quoting, and why.");



        group.MapPost("/quotations/{requestNo}/award", AwardQuotationAsync)
             .WithName("AwardQuotation")
             .WithSummary("Decides which vendor wins each line. A dearer quote needs a reason.");



        group.MapPost("/quotations/{requestNo}/order", OrderFromQuotationAsync)
             .WithName("OrderFromQuotation")
             .WithSummary("Turns one vendor's awarded lines into an order at the price they quoted.");



        group.MapPost("/quotations/{requestNo}/cancel", CancelQuotationAsync)
             .WithName("CancelQuotationRequest")
             .WithSummary("Abandons a request that has produced no orders.");



        group.MapGet("/requisitions", ListRequisitionsAsync)
             .WithName("ListPurchaseRequisitions")
             .WithSummary("What has been asked for, newest first.");



        group.MapGet("/requisitions/{requisitionNo}", GetRequisitionAsync)
             .WithName("GetPurchaseRequisition")
             .WithSummary("One requisition and what is on it.");



        group.MapPost("/requisitions", CreateRequisitionAsync)
             .WithName("CreatePurchaseRequisition")
             .WithSummary("Asks for something to be bought. Commits nothing.");



        group.MapPost("/requisitions/{requisitionNo}/submit", SubmitRequisitionAsync)
             .WithName("SubmitPurchaseRequisition")
             .WithSummary("Sends it for approval, or approves it where none is needed.");



        group.MapPost("/requisitions/{requisitionNo}/approve", ApproveRequisitionAsync)
             .WithName("ApprovePurchaseRequisition")
             .WithSummary("Signs for a requisition. Never your own.");



        group.MapPost("/requisitions/{requisitionNo}/reject", RejectRequisitionAsync)
             .WithName("RejectPurchaseRequisition")
             .WithSummary("Turns a requisition down, and says why.");



        group.MapPost("/requisitions/{requisitionNo}/order", OrderFromRequisitionAsync)
             .WithName("OrderFromPurchaseRequisition")
             .WithSummary("Turns part of an approved requisition into an order for one vendor.");



        group.MapPost("/requisitions/{requisitionNo}/cancel", CancelRequisitionAsync)
             .WithName("CancelPurchaseRequisition")
             .WithSummary("Abandons a requisition before it becomes anything.");


        group.MapPost("/orders/{orderNo}/return", ReturnAsync)
             .WithName("PostPurchaseReturn")
             .WithSummary("Sends goods back at what they cost, and credits what was invoiced.");


        group.MapPost("/orders/{orderNo}/receive", ReceiveAsync)
             .WithName("ReceiveGoods")
             .WithSummary("Records that goods arrived: moves stock and accrues what is owed.");

        group.MapPost("/orders/{orderNo}/invoice", InvoiceAsync)
             .WithName("PostPurchaseInvoice")
             .WithSummary("Turns what arrived into a debt owed to the vendor.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        [FromQuery] string? status,
        [FromQuery] string? vendorNo,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view purchase orders", http);
        }

        var query = context.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines).AsQueryable();

        if (Enum.TryParse<PurchaseOrderStatus>(status, ignoreCase: true, out var wanted))
        {
            query = query.Where(o => o.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(vendorNo))
        {
            query = query.Where(o => o.VendorNo == vendorNo);
        }

        var orders = await query
            .OrderByDescending(o => o.No)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(orders.Select(static o => View(o)));
    }

    private static async Task<IResult> GetAsync(
        string orderNo,
        PurchaseOrderService orders,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view purchase orders", http);
        }

        var order = await orders.LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        return order is null ? Results.NotFound() : Results.Ok(View(order));
    }

    private static async Task<IResult> CreateAsync(
        CreatePurchaseOrderRequest request,
        PurchaseOrderService orders,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, CreatePermission))
        {
            return Forbidden(CreatePermission, "raise purchase orders", http);
        }

        var result = await orders
            .CreateAsync(
                request.VendorNo,
                [.. request.Lines.Select(l => new PurchaseOrderLineRequest(
                    l.Type,
                    l.No,
                    l.Quantity,
                    l.DirectUnitCost,
                    l.Description,
                    l.TaxCode,
                    l.LocationCode,
                    l.VariantCode))],
                request.LocationCode,
                request.ExpectedReceiptDate,
                request.Description,
                request.VendorOrderNo,
                Overrides(user),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                order = View(result.Value),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }
    private static async Task<IResult> OpenOrdersAsync(
        PurchaseReportService reports,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        string? vendorNo = null,
        bool overdueOnly = false)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "read open purchase orders", http);
        }

        return Results.Ok(await reports
            .OpenOrdersAsync(vendorNo, overdueOnly, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> VendorPerformanceAsync(
        PurchaseReportService reports,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "read vendor performance", http);
        }

        var last = to ?? clock.Today;

        return Results.Ok(await reports
            .VendorPerformanceAsync(from ?? last.AddMonths(-3), last, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> PurchaseAnalysisAsync(
        PurchaseReportService reports,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        bool byItem = false)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "read purchase analysis", http);
        }

        var last = to ?? clock.Today;

        return Results.Ok(await reports
            .AnalysisAsync(from ?? last.AddMonths(-3), last, byItem, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> LandedCostAsync(
        string orderNo,
        LandedCostRequest request,
        LandedCostService landed,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Purchasing.LandedCost.Post"))
        {
            return Forbidden("Purchasing.LandedCost.Post", "apply a landed cost", http);
        }

        var result = await landed
            .ApplyAsync(
                orderNo,
                request.Amount,
                request.Basis,
                request.ContraAccountNo,
                request.PostingDate,
                request.Description,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                transactionNo = result.Value.TransactionNo,
                amount = result.Value.Amount,
                toInventory = result.Value.ToInventory,
                toCostOfSales = result.Value.ToCostOfSales,
                shares = result.Value.Shares,
                messages = result.Messages.Select(static m => new MessageView(
                    m.Code.Value,
                    m.Severity.ToString(),
                    m.Title,
                    m.Detail,
                    m.Resolution)),
            });
    }

    private static async Task<IResult> ApproveAsync(
        string orderNo,
        PurchaseOrderService orders,
        PurchaseApprovalService approvals,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Purchasing.Approval.Post"))
        {
            return Forbidden("Purchasing.Approval.Post", "approve a purchase order", http);
        }

        var order = await orders.LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Refused(orders.NotFound(orderNo), http);
        }

        var result = await approvals.ApproveAsync(order, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(order, result.Messages));
    }

    private static async Task<IResult> RejectAsync(
        string orderNo,
        RejectOrderRequest request,
        PurchaseOrderService orders,
        PurchaseApprovalService approvals,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Purchasing.Approval.Post"))
        {
            return Forbidden("Purchasing.Approval.Post", "reject a purchase order", http);
        }

        var order = await orders.LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Refused(orders.NotFound(orderNo), http);
        }

        var result = await approvals.RejectAsync(order, request.Reason, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(order, result.Messages));
    }

    private static async Task<IResult> ApprovalLimitsAsync(
        PurchaseApprovalService approvals,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        bool includeWithdrawn = false)
    {
        if (!Can(user, "Purchasing.Approval.Read"))
        {
            return Forbidden("Purchasing.Approval.Read", "view approval limits", http);
        }

        var rows = await approvals.LimitsAsync(includeWithdrawn, cancellationToken).ConfigureAwait(false);

        return Results.Ok(rows.Select(static l => new ApprovalLimitView(
            l.UserId,
            l.UserName,
            l.DisplayName,
            l.MaximumAmount,
            l.IsActive)));
    }

    private static async Task<IResult> SetApprovalLimitAsync(
        SetApprovalLimitRequest request,
        PurchaseApprovalService approvals,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Purchasing.Approval.Update"))
        {
            return Forbidden("Purchasing.Approval.Update", "set approval limits", http);
        }

        var result = await approvals
            .SetLimitAsync(
                new ApprovalLimitRequest(
                    request.UserId,
                    request.UserName,
                    request.DisplayName,
                    request.MaximumAmount,
                    request.IsActive),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new ApprovalLimitView(
                result.Value.UserId,
                result.Value.UserName,
                result.Value.DisplayName,
                result.Value.MaximumAmount,
                result.Value.IsActive));
    }


    private static async Task<IResult> ReleaseAsync(
        string orderNo,
        PurchaseOrderService orders,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, CreatePermission))
        {
            return Forbidden(CreatePermission, "raise purchase orders", http);
        }

        var result = await orders.ReleaseAsync(orderNo, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(result.Value, result.Messages));
    }
    private static async Task<IResult> ListQuotationsAsync(
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] string? status = null)
    {
        if (!Can(user, "Purchasing.Quotation.Read"))
        {
            return Forbidden("Purchasing.Quotation.Read", "view quotation requests", http);
        }

        var wanted = Enum.TryParse<QuotationRequestStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : (QuotationRequestStatus?)null;

        var found = await quotations.ListAsync(wanted, cancellationToken).ConfigureAwait(false);

        return Results.Ok(found.Select(static r => new
        {
            no = r.No,
            status = r.Status.ToString(),
            requestDate = r.RequestDate,
            respondByDate = r.RespondByDate,
            description = r.Description,
            lineCount = r.Lines.Count,
            vendorCount = r.Invitations.Count,
            answeredCount = r.Invitations.Count(static i => i.HasAnswered),
        }));
    }

    private static async Task<IResult> GetQuotationAsync(
        string requestNo,
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Purchasing.Quotation.Read"))
        {
            return Forbidden("Purchasing.Quotation.Read", "view quotation requests", http);
        }

        var request = await quotations.LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return Results.NotFound();
        }

        var comparison = await quotations.CompareAsync(requestNo, cancellationToken).ConfigureAwait(false);

        return Results.Ok(ViewOf(request, comparison));
    }

    private static async Task<IResult> CreateQuotationAsync(
        CreateQuotationRequest request,
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Purchasing.Quotation.Update"))
        {
            return Forbidden("Purchasing.Quotation.Update", "ask vendors what something costs", http);
        }

        var result = await quotations
            .CreateAsync(
                request.Lines ?? [],
                request.LocationCode,
                request.RespondByDate,
                request.NeededByDate,
                request.Description,
                request.RequisitionNo,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(ViewOf(result.Value, []));
    }

    private static Task<IResult> InviteVendorsAsync(
        string requestNo,
        InviteVendorsRequest request,
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ActOnQuotationAsync(
            quotations,
            user,
            http,
            "invite vendors",
            q => q.InviteAsync(requestNo, request.VendorNos ?? [], cancellationToken),
            requestNo,
            cancellationToken);
    }

    private static Task<IResult> SendQuotationAsync(
        string requestNo,
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
        => ActOnQuotationAsync(
            quotations,
            user,
            http,
            "send a quotation request",
            q => q.SendAsync(requestNo, cancellationToken),
            requestNo,
            cancellationToken);

    private static Task<IResult> RecordQuoteAsync(
        string requestNo,
        RecordQuoteRequest request,
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ActOnQuotationAsync(
            quotations,
            user,
            http,
            "record a quote",
            q => q.RespondAsync(requestNo, request.VendorNo, request.Lines ?? [], cancellationToken),
            requestNo,
            cancellationToken);
    }

    private static Task<IResult> DeclineQuotationAsync(
        string requestNo,
        DeclineToQuoteRequest request,
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ActOnQuotationAsync(
            quotations,
            user,
            http,
            "record a decline",
            q => q.DeclineAsync(requestNo, request.VendorNo, request.Reason, cancellationToken),
            requestNo,
            cancellationToken);
    }

    private static Task<IResult> AwardQuotationAsync(
        string requestNo,
        AwardQuotationRequest request,
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ActOnQuotationAsync(
            quotations,
            user,
            http,
            "award a quotation",
            q => q.AwardAsync(requestNo, request.Awards ?? [], cancellationToken),
            requestNo,
            cancellationToken);
    }

    private static Task<IResult> CancelQuotationAsync(
        string requestNo,
        RequisitionReasonRequest request,
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ActOnQuotationAsync(
            quotations,
            user,
            http,
            "abandon a quotation request",
            q => q.CancelAsync(requestNo, request.Reason, cancellationToken),
            requestNo,
            cancellationToken);
    }

    private static async Task<IResult> OrderFromQuotationAsync(
        string requestNo,
        OrderFromQuotationRequest request,
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The award was a decision; the order is a commitment, and they are not the same right.
        if (!Can(user, "Purchasing.Order.Create"))
        {
            return Forbidden("Purchasing.Order.Create", "raise an order from a quotation", http);
        }

        var result = await quotations
            .OrderAsync(
                requestNo,
                request.VendorNo,
                request.ExpectedReceiptDate,
                Overrides(user),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new { order = View(result.Value), messages = result.Messages });
    }

    private static async Task<IResult> ActOnQuotationAsync(
        PurchaseQuotationService quotations,
        IUserContext user,
        HttpContext http,
        string doing,
        Func<PurchaseQuotationService, Task<Result<PurchaseQuotationRequest>>> work,
        string requestNo,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Purchasing.Quotation.Update"))
        {
            return Forbidden("Purchasing.Quotation.Update", doing, http);
        }

        var result = await work(quotations).ConfigureAwait(false);

        if (result.Failed)
        {
            return Refused(result, http);
        }

        var comparison = await quotations.CompareAsync(requestNo, cancellationToken).ConfigureAwait(false);

        return Results.Ok(ViewOf(result.Value, comparison));
    }

    private static QuotationRequestView ViewOf(
        PurchaseQuotationRequest request,
        IReadOnlyList<QuotationComparisonRow> comparison)
        => new(
            request.No,
            request.Status.ToString(),
            request.RequestDate,
            request.RespondByDate,
            request.NeededByDate,
            request.LocationCode,
            request.RequisitionNo,
            request.Description,
            [.. request.Invitations
                .OrderBy(static i => i.VendorNo, StringComparer.OrdinalIgnoreCase)
                .Select(static i => new QuotationInvitationView(
                    i.VendorNo,
                    i.VendorName,
                    i.HasAnswered,
                    i.DeclinedReason))],
            comparison);

    private static async Task<IResult> ListRequisitionsAsync(
        PurchaseRequisitionService requisitions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] string? status = null)
    {
        if (!Can(user, "Purchasing.Requisition.Read"))
        {
            return Forbidden("Purchasing.Requisition.Read", "view requisitions", http);
        }

        var wanted = Enum.TryParse<PurchaseRequisitionStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : (PurchaseRequisitionStatus?)null;

        var found = await requisitions.ListAsync(wanted, cancellationToken).ConfigureAwait(false);

        return Results.Ok(found.Select(ViewOf));
    }

    private static async Task<IResult> GetRequisitionAsync(
        string requisitionNo,
        PurchaseRequisitionService requisitions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Purchasing.Requisition.Read"))
        {
            return Forbidden("Purchasing.Requisition.Read", "view requisitions", http);
        }

        var found = await requisitions.LoadAsync(requisitionNo, cancellationToken).ConfigureAwait(false);

        return found is null ? Results.NotFound() : Results.Ok(ViewOf(found));
    }

    private static async Task<IResult> CreateRequisitionAsync(
        CreateRequisitionRequest request,
        PurchaseRequisitionService requisitions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Purchasing.Requisition.Create"))
        {
            return Forbidden("Purchasing.Requisition.Create", "ask for something to be bought", http);
        }

        var result = await requisitions
            .CreateAsync(
                [
                    .. (request.Lines ?? []).Select(static l => new PurchaseRequisitionLineRequest(
                        l.Type,
                        l.No,
                        l.Quantity,
                        l.EstimatedUnitCost,
                        l.Description,
                        l.LocationCode,
                        l.VariantCode,
                        l.SuggestedVendorNo)),
                ],
                request.LocationCode,
                request.NeededByDate,
                request.Description,
                request.Justification,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(ViewOf(result.Value));
    }

    private static async Task<IResult> SubmitRequisitionAsync(
        string requisitionNo,
        PurchaseRequisitionService requisitions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Purchasing.Requisition.Create"))
        {
            return Forbidden("Purchasing.Requisition.Create", "submit requisitions", http);
        }

        var result = await requisitions.SubmitAsync(requisitionNo, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(ViewOf(result.Value));
    }

    private static async Task<IResult> ApproveRequisitionAsync(
        string requisitionNo,
        PurchaseRequisitionService requisitions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, "Purchasing.Requisition.Approve"))
        {
            return Forbidden("Purchasing.Requisition.Approve", "sign for a requisition", http);
        }

        var result = await requisitions.ApproveAsync(requisitionNo, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(ViewOf(result.Value));
    }

    private static async Task<IResult> RejectRequisitionAsync(
        string requisitionNo,
        RequisitionReasonRequest request,
        PurchaseRequisitionService requisitions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Purchasing.Requisition.Approve"))
        {
            return Forbidden("Purchasing.Requisition.Approve", "turn a requisition down", http);
        }

        var result = await requisitions
            .RejectAsync(requisitionNo, request.Reason, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(ViewOf(result.Value));
    }

    private static async Task<IResult> OrderFromRequisitionAsync(
        string requisitionNo,
        OrderFromRequisitionRequest request,
        PurchaseRequisitionService requisitions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Raising the order needs what raising an order needs. Somebody who may ask for things
        // does not thereby become somebody who may commit the company to buying them.
        if (!Can(user, "Purchasing.Order.Create"))
        {
            return Forbidden("Purchasing.Order.Create", "raise an order from a requisition", http);
        }

        var result = await requisitions
            .OrderAsync(
                requisitionNo,
                request.VendorNo,
                request.Lines,
                request.ExpectedReceiptDate,
                Overrides(user),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new { order = View(result.Value), messages = result.Messages });
    }

    private static async Task<IResult> CancelRequisitionAsync(
        string requisitionNo,
        RequisitionReasonRequest request,
        PurchaseRequisitionService requisitions,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Purchasing.Requisition.Create"))
        {
            return Forbidden("Purchasing.Requisition.Create", "cancel a requisition", http);
        }

        var result = await requisitions
            .CancelAsync(requisitionNo, request.Reason, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(ViewOf(result.Value));
    }

    private static RequisitionView ViewOf(PurchaseRequisition requisition)
        => new(
            requisition.No,
            requisition.Status.ToString(),
            requisition.RequisitionDate,
            requisition.NeededByDate,
            requisition.LocationCode,
            requisition.Description,
            requisition.Justification,
            requisition.RequestedByUserName,
            requisition.ApprovedByUserName,
            requisition.ApprovedAtUtc,
            requisition.ApprovedAmount,
            requisition.RejectionReason,
            requisition.EstimatedAmount,
            requisition.IsEditable,
            requisition.CanBeOrdered,
            [.. requisition.Lines
                .OrderBy(static l => l.LineNo)
                .Select(static l => new RequisitionLineView(
                    l.LineNo,
                    l.Type.ToString(),
                    l.ItemNo ?? l.AccountNo,
                    l.VariantCode,
                    l.Description,
                    l.LocationCode,
                    l.Quantity,
                    l.EstimatedUnitCost,
                    l.EstimatedAmount,
                    l.QuantityOrdered,
                    l.OutstandingToOrder,
                    l.SuggestedVendorNo))]);

    private static async Task<IResult> ReturnAsync(
        string orderNo,
        PurchaseReturnRequest request,
        PurchaseReturnService returns,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, "Purchasing.Return.Post"))
        {
            return Forbidden("Purchasing.Return.Post", "send goods back to a vendor", http);
        }

        var result = await returns
            .ReturnAsync(
                orderNo,
                request.Lines,
                request.Reason,
                Overrides(user),
                request.OverrideReason,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                orderNo = result.Value.OrderNo,
                creditMemoNo = result.Value.CreditMemoNo,
                stockTransactionNo = result.Value.StockTransactionNo,
                ledgerTransactionNo = result.Value.LedgerTransactionNo,
                lineCount = result.Value.LineCount,
                costAmount = result.Value.CostAmount,
                creditedQuantity = result.Value.CreditedQuantity,
                netAmount = result.Value.NetAmount,
                taxAmount = result.Value.TaxAmount,
                totalAmount = result.Value.TotalAmount,
                messages = result.Messages,
            });
    }


    private static async Task<IResult> ReceiveAsync(
        string orderNo,
        ReceiveGoodsRequest? request,
        PurchaseReceiptService receipts,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReceivePermission))
        {
            return Forbidden(ReceivePermission, "receive goods", http);
        }

        var result = await receipts
            .ReceiveAsync(
                orderNo,
                request?.Lines,
                request?.VendorDeliveryNo,
                Overrides(user),
                request?.OverrideReason,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                orderNo = result.Value.OrderNo,
                transactionNo = result.Value.TransactionNo,
                lineCount = result.Value.LineCount,
                value = result.Value.Value,
                status = result.Value.Status.ToString(),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> InvoiceAsync(
        string orderNo,
        PostPurchaseInvoiceRequest request,
        PurchaseInvoiceService invoices,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, InvoicePermission))
        {
            return Forbidden(InvoicePermission, "post vendor invoices", http);
        }

        var result = await invoices
            .PostAsync(
                orderNo,
                request.VendorInvoiceNo,
                request.Lines,
                Overrides(user),
                request.OverrideReason,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                orderNo = result.Value.OrderNo,
                transactionNo = result.Value.TransactionNo,
                documentNo = result.Value.DocumentNo,
                netAmount = result.Value.NetAmount,
                taxAmount = result.Value.TaxAmount,
                totalAmount = result.Value.TotalAmount,
                status = result.Value.Status.ToString(),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static PurchaseOrderView View(
        PurchaseOrder order,
        IReadOnlyList<ASAP.Platform.Kernel.Messaging.AsapMessage>? messages = null)
        => new(
            order.No,
            order.VendorNo,
            order.VendorName,
            order.Status.ToString(),
            order.OrderDate,
            order.ExpectedReceiptDate,
            order.LocationCode,
            order.VendorOrderNo,
            order.Description,
            order.Lines.Sum(static l => l.LineAmount),
            order.IsEditable,
            [.. order.Lines
                .OrderBy(static l => l.LineNo)
                .Select(static l => new PurchaseOrderLineView(
                    l.LineNo,
                    l.Type.ToString(),
                    l.ItemNo ?? l.AccountNo,
                    l.Description,
                    l.LocationCode,
                    l.Quantity,
                    l.DirectUnitCost,
                    l.TaxCode,
                    l.LineAmount,
                    l.QuantityReceived,
                    l.QuantityInvoiced,
                    l.OutstandingToReceive,
                    l.ReceivedNotInvoiced,
                    l.QuantityReturned,
                    l.ReturnableQuantity))])
        {
            ApprovedBy = order.ApprovedByUserName,
            ApprovedAtUtc = order.ApprovedAtUtc,
            ApprovedAmount = order.ApprovedAmount,
            RejectionReason = order.RejectionReason,
            Messages = messages is null
                ? []
                : [.. messages.Select(static m => new MessageView(
                    m.Code.Value,
                    m.Severity.ToString(),
                    m.Title,
                    m.Detail,
                    m.Resolution))],
        };

    /// <summary>
    /// The overrides this caller holds.
    /// </summary>
    /// <remarks>
    /// Both the purchasing override and the stock one, because receiving goods runs through
    /// Inventory's posting engine and can meet its rules as well as these.
    /// </remarks>
    private static IReadOnlySet<string> Overrides(IUserContext user)
        => new[] { OverridePermission, "Inventory.Stock.Override", "Finance.Party.Override" }
            .Where(permission => Can(user, permission))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool Can(IUserContext user, string permission)
        => user.IsSuperUser || user.Has(permission);

    private static IResult Forbidden(string permission, string doing, HttpContext http)
        => Results.Json(
            AsapProblem.Forbidden(permission, doing, http.Request.Path),
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult Refused(Platform.Kernel.Results.Result result, HttpContext http)
        => Results.Json(
            AsapProblem.From(result, AsapProblem.StatusFor(result.Messages), http.Request.Path),
            statusCode: AsapProblem.StatusFor(result.Messages));
}
