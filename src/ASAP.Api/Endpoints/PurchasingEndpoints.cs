using ASAP.Api.Infrastructure;
using ASAP.Modules.Purchasing;
using ASAP.Modules.Purchasing.Approvals;
using ASAP.Modules.Purchasing.Costing;
using ASAP.Modules.Purchasing.Orders;
using ASAP.Modules.Purchasing.Reporting;
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
                    l.ReceivedNotInvoiced))])
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
