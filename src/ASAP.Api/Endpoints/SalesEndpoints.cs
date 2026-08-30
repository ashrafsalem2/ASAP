using ASAP.Api.Infrastructure;
using ASAP.Modules.Sales.Orders;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Endpoints;

/// <summary>One line on a new sales order, as a client sends it.</summary>
/// <param name="Type">Whether it sells stock or a charge.</param>
/// <param name="No">The item number, or the account number on a charge line.</param>
/// <param name="Quantity">How much to sell.</param>
/// <param name="UnitPrice">The price per unit, or zero to take the item's own price.</param>
/// <param name="DiscountPercent">A discount off this line.</param>
/// <param name="Description">What it is.</param>
/// <param name="TaxCode">The tax to charge.</param>
/// <param name="LocationCode">Where this line ships from, when it differs from the order.</param>
public sealed record SalesLinePayload(
    SalesLineType Type,
    string No,
    decimal Quantity,
    decimal UnitPrice = 0m,
    decimal DiscountPercent = 0m,
    string? Description = null,
    string? TaxCode = null,
    string? LocationCode = null,
    string? VariantCode = null);

/// <summary>What a client sends to take a sales order.</summary>
/// <param name="CustomerNo">Who it is for.</param>
/// <param name="Lines">What they are buying.</param>
/// <param name="LocationCode">Where it ships from.</param>
/// <param name="RequestedDeliveryDate">When they want it.</param>
/// <param name="Description">A note for whoever picks it.</param>
/// <param name="CustomerOrderNo">Their own order number.</param>
public sealed record CreateSalesOrderRequest(
    string CustomerNo,
    IReadOnlyList<SalesLinePayload> Lines,
    string? LocationCode = null,
    DateOnly? RequestedDeliveryDate = null,
    string? Description = null,
    string? CustomerOrderNo = null);

/// <summary>What a client sends to ship goods.</summary>
/// <param name="Lines">How much of each line went, or null for everything outstanding.</param>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record ShipGoodsRequest(
    IReadOnlyList<ShipmentLineRequest>? Lines = null,
    string? OverrideReason = null);

/// <summary>What a client sends to post a sales invoice.</summary>
/// <param name="Lines">What the invoice covers, or null for everything shipped and unbilled.</param>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record PostSalesInvoiceRequest(
    IReadOnlyList<SalesInvoiceLineRequest>? Lines = null,
    string? OverrideReason = null);

/// <summary>One line of a sales order as it is reported back.</summary>
/// <param name="LineNo">Its position.</param>
/// <param name="Type">Whether it sells stock or a charge.</param>
/// <param name="No">The item or account number.</param>
/// <param name="Description">What it is.</param>
/// <param name="LocationCode">Where it ships from.</param>
/// <param name="Quantity">How much was ordered.</param>
/// <param name="UnitPrice">The list price per unit.</param>
/// <param name="DiscountPercent">The discount on this line.</param>
/// <param name="TaxCode">The tax charged.</param>
/// <param name="LineAmount">What the line comes to after discount, before tax.</param>
/// <param name="QuantityShipped">How much has gone.</param>
/// <param name="QuantityInvoiced">How much has been invoiced.</param>
/// <param name="OutstandingToShip">How much is still to go.</param>
/// <param name="ShippedNotInvoiced">How much has gone and is still unbilled.</param>
public sealed record SalesOrderLineView(
    int LineNo,
    string Type,
    string? No,
    string Description,
    string? LocationCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    string? TaxCode,
    decimal LineAmount,
    decimal QuantityShipped,
    decimal QuantityInvoiced,
    decimal OutstandingToShip,
    decimal ShippedNotInvoiced);

/// <summary>A sales order as it is reported back.</summary>
/// <param name="No">Its number.</param>
/// <param name="CustomerNo">Who it is for.</param>
/// <param name="CustomerName">Their name at the time.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="OrderDate">When it was taken.</param>
/// <param name="RequestedDeliveryDate">When they want it.</param>
/// <param name="LocationCode">Where it ships from.</param>
/// <param name="CustomerOrderNo">Their own order number.</param>
/// <param name="Description">The note on it.</param>
/// <param name="TotalAmount">What it comes to after discount, before tax.</param>
/// <param name="IsEditable">Whether lines may still be changed.</param>
/// <param name="Lines">What is on it.</param>
public sealed record SalesOrderView(
    string No,
    string CustomerNo,
    string CustomerName,
    string Status,
    DateOnly OrderDate,
    DateOnly? RequestedDeliveryDate,
    string? LocationCode,
    string? CustomerOrderNo,
    string? Description,
    decimal TotalAmount,
    bool IsEditable,
    IReadOnlyList<SalesOrderLineView> Lines);

/// <summary>Sales orders, shipments and invoices.</summary>
public static class SalesEndpoints
{
    private const string ReadPermission = "Sales.Order.Read";
    private const string CreatePermission = "Sales.Order.Create";
    private const string ShipPermission = "Sales.Shipment.Post";
    private const string InvoicePermission = "Sales.Invoice.Post";

    /// <summary>Maps the Sales endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/sales").RequireAuthorization().WithTags("Sales");

        group.MapGet("/orders", ListAsync)
             .WithName("SalesOrders")
             .WithSummary("Lists sales orders, most recently taken first.");

        group.MapGet("/orders/{orderNo}", GetAsync)
             .WithName("SalesOrder")
             .WithSummary("Reads one sales order and its lines.");

        group.MapPost("/orders", CreateAsync)
             .WithName("CreateSalesOrder")
             .WithSummary("Takes a sales order. Nothing posts until goods ship.");

        group.MapPost("/orders/{orderNo}/release", ReleaseAsync)
             .WithName("ReleaseSalesOrder")
             .WithSummary("Marks an order as confirmed with the customer.");

        group.MapPost("/orders/{orderNo}/ship", ShipAsync)
             .WithName("ShipGoods")
             .WithSummary("Records that goods left: takes stock out and charges cost of sales.");

        group.MapPost("/orders/{orderNo}/invoice", InvoiceAsync)
             .WithName("PostSalesInvoice")
             .WithSummary("Turns what shipped into a debt the customer owes.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        AsapDbContext context,
        IUserContext user,
        HttpContext http,
        [FromQuery] string? status,
        [FromQuery] string? customerNo,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view sales orders", http);
        }

        var query = context.Set<SalesOrder>().AsNoTracking().Include(o => o.Lines).AsQueryable();

        if (Enum.TryParse<SalesOrderStatus>(status, ignoreCase: true, out var wanted))
        {
            query = query.Where(o => o.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(customerNo))
        {
            query = query.Where(o => o.CustomerNo == customerNo);
        }

        var orders = await query
            .OrderByDescending(o => o.No)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(orders.Select(View));
    }

    private static async Task<IResult> GetAsync(
        string orderNo,
        SalesOrderService orders,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view sales orders", http);
        }

        var order = await orders.LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        return order is null ? Results.NotFound() : Results.Ok(View(order));
    }

    private static async Task<IResult> CreateAsync(
        CreateSalesOrderRequest request,
        SalesOrderService orders,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, CreatePermission))
        {
            return Forbidden(CreatePermission, "take sales orders", http);
        }

        var result = await orders
            .CreateAsync(
                request.CustomerNo,
                [.. request.Lines.Select(l => new SalesOrderLineRequest(
                    l.Type,
                    l.No,
                    l.Quantity,
                    l.UnitPrice,
                    l.DiscountPercent,
                    l.Description,
                    l.TaxCode,
                    l.LocationCode,
                    l.VariantCode))],
                request.LocationCode,
                request.RequestedDeliveryDate,
                request.Description,
                request.CustomerOrderNo,
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

    private static async Task<IResult> ReleaseAsync(
        string orderNo,
        SalesOrderService orders,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, CreatePermission))
        {
            return Forbidden(CreatePermission, "take sales orders", http);
        }

        var result = await orders.ReleaseAsync(orderNo, cancellationToken).ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(View(result.Value));
    }

    private static async Task<IResult> ShipAsync(
        string orderNo,
        ShipGoodsRequest? request,
        SalesShipmentService shipments,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ShipPermission))
        {
            return Forbidden(ShipPermission, "ship goods", http);
        }

        var result = await shipments
            .ShipAsync(
                orderNo,
                request?.Lines,
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
                costAmount = result.Value.CostAmount,
                status = result.Value.Status.ToString(),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static async Task<IResult> InvoiceAsync(
        string orderNo,
        PostSalesInvoiceRequest? request,
        SalesInvoiceService invoices,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, InvoicePermission))
        {
            return Forbidden(InvoicePermission, "post sales invoices", http);
        }

        var result = await invoices
            .PostAsync(
                orderNo,
                request?.Lines,
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
                documentNo = result.Value.DocumentNo,
                netAmount = result.Value.NetAmount,
                discountAmount = result.Value.DiscountAmount,
                taxAmount = result.Value.TaxAmount,
                totalAmount = result.Value.TotalAmount,
                status = result.Value.Status.ToString(),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    private static SalesOrderView View(SalesOrder order)
        => new(
            order.No,
            order.CustomerNo,
            order.CustomerName,
            order.Status.ToString(),
            order.OrderDate,
            order.RequestedDeliveryDate,
            order.LocationCode,
            order.CustomerOrderNo,
            order.Description,
            order.Lines.Sum(static l => l.LineAmount),
            order.IsEditable,
            [.. order.Lines
                .OrderBy(static l => l.LineNo)
                .Select(static l => new SalesOrderLineView(
                    l.LineNo,
                    l.Type.ToString(),
                    l.ItemNo ?? l.AccountNo,
                    l.Description,
                    l.LocationCode,
                    l.Quantity,
                    l.UnitPrice,
                    l.DiscountPercent,
                    l.TaxCode,
                    l.LineAmount,
                    l.QuantityShipped,
                    l.QuantityInvoiced,
                    l.OutstandingToShip,
                    l.ShippedNotInvoiced))]);

    /// <summary>
    /// The overrides this caller holds.
    /// </summary>
    /// <remarks>
    /// Shipping runs through Inventory's posting engine and invoicing through Finance's, so a
    /// sale can meet rules belonging to all three modules.
    /// </remarks>
    private static IReadOnlySet<string> Overrides(IUserContext user)
        => new[] { "Sales.Order.Override", "Inventory.Stock.Override", "Finance.Party.Override" }
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
