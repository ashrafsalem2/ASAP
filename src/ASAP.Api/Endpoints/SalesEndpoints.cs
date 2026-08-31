using ASAP.Api.Infrastructure;
using ASAP.Modules.Sales.Orders;
using ASAP.Modules.Sales.Pricing;
using ASAP.Modules.Sales.Quotes;
using ASAP.Modules.Sales.Reporting;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Time;
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
/// <param name="VariantCode">Which variant of the item, where the item has them.</param>
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

/// <summary>One line on a new quote, as a client sends it.</summary>
/// <param name="Type">Whether it quotes stock or a charge.</param>
/// <param name="No">The item number, or the account number on a charge line.</param>
/// <param name="Quantity">How much to quote for.</param>
/// <param name="UnitPrice">The price per unit, or zero to take what this customer was agreed.</param>
/// <param name="DiscountPercent">A discount off this line.</param>
/// <param name="Description">What it is.</param>
/// <param name="TaxCode">The tax that would be charged.</param>
/// <param name="LocationCode">Where this line would ship from.</param>
/// <param name="VariantCode">Which variant of the item, where the item has them.</param>
public sealed record QuoteLinePayload(
    SalesLineType Type,
    string No,
    decimal Quantity,
    decimal UnitPrice = 0m,
    decimal DiscountPercent = 0m,
    string? Description = null,
    string? TaxCode = null,
    string? LocationCode = null,
    string? VariantCode = null);

/// <summary>What a client sends to offer a customer a price.</summary>
/// <param name="CustomerNo">Who it is for.</param>
/// <param name="Lines">What is being quoted.</param>
/// <param name="ValidUntil">The last day the prices stand. Defaults to the company's setting.</param>
/// <param name="LocationCode">Where it would ship from.</param>
/// <param name="Description">A note.</param>
/// <param name="CustomerOrderNo">Their own reference.</param>
public sealed record CreateQuoteRequest(
    string CustomerNo,
    IReadOnlyList<QuoteLinePayload> Lines,
    DateOnly? ValidUntil = null,
    string? LocationCode = null,
    string? Description = null,
    string? CustomerOrderNo = null);

/// <summary>What a client sends to accept a quote.</summary>
/// <param name="LocationCode">Where it ships from, if the quote did not say.</param>
/// <param name="RequestedDeliveryDate">When they want it.</param>
public sealed record AcceptQuoteRequest(
    string? LocationCode = null,
    DateOnly? RequestedDeliveryDate = null);

/// <summary>What a client sends to record that the customer said no.</summary>
/// <param name="Reason">Why, where they said.</param>
public sealed record DeclineQuoteRequest(string? Reason = null);

/// <summary>One line of a quote as it is reported back.</summary>
/// <param name="LineNo">Its position.</param>
/// <param name="Type">Whether it quotes stock or a charge.</param>
/// <param name="No">The item or account number.</param>
/// <param name="VariantCode">Which variant, where the item has them.</param>
/// <param name="Description">What it is.</param>
/// <param name="LocationCode">Where it would ship from.</param>
/// <param name="Quantity">How much was quoted for.</param>
/// <param name="UnitPrice">The price offered per unit.</param>
/// <param name="DiscountPercent">The discount on this line.</param>
/// <param name="TaxCode">The tax that would be charged.</param>
/// <param name="LineAmount">What the line comes to after discount, before tax.</param>
public sealed record QuoteLineView(
    int LineNo,
    string Type,
    string? No,
    string? VariantCode,
    string Description,
    string? LocationCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    string? TaxCode,
    decimal LineAmount);

/// <summary>A quote as it is reported back.</summary>
/// <param name="No">Its number.</param>
/// <param name="CustomerNo">Who it is for.</param>
/// <param name="CustomerName">Their name at the time.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="QuoteDate">The day it was quoted.</param>
/// <param name="ValidUntil">The last day the prices stand.</param>
/// <param name="HasExpired">Whether that day has passed.</param>
/// <param name="LocationCode">Where it would ship from.</param>
/// <param name="CustomerOrderNo">Their own reference.</param>
/// <param name="Description">The note on it.</param>
/// <param name="OrderNo">The order it became, once accepted.</param>
/// <param name="DeclineReason">Why the customer said no, where they said.</param>
/// <param name="TotalAmount">What it comes to after discount, before tax.</param>
/// <param name="IsEditable">Whether its lines may still be changed.</param>
/// <param name="Lines">What is on it.</param>
public sealed record QuoteView(
    string No,
    string CustomerNo,
    string CustomerName,
    string Status,
    DateOnly QuoteDate,
    DateOnly ValidUntil,
    bool HasExpired,
    string? LocationCode,
    string? CustomerOrderNo,
    string? Description,
    string? OrderNo,
    string? DeclineReason,
    decimal TotalAmount,
    bool IsEditable,
    IReadOnlyList<QuoteLineView> Lines);

/// <summary>What a client sends to take goods back.</summary>
/// <param name="Lines">How much of each line came back, or null for everything that still could.</param>
/// <param name="Reason">Why they came back, which goes on both postings.</param>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record SalesReturnRequest(
    IReadOnlyList<SalesReturnLineRequest>? Lines = null,
    string? Reason = null,
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
/// <param name="QuantityReturned">How much has come back.</param>
/// <param name="ReturnableQuantity">How much could still come back.</param>
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
    decimal ShippedNotInvoiced,
    decimal QuantityReturned,
    decimal ReturnableQuantity);

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

/// <summary>One agreed price as it is reported back.</summary>
/// <param name="ItemNo">What it is for.</param>
/// <param name="VariantCode">One variant, or null for all of them.</param>
/// <param name="UnitCode">One unit, or null for any.</param>
/// <param name="MinimumQuantity">The least that has to be bought for it.</param>
/// <param name="UnitPrice">What one costs.</param>
/// <param name="DiscountPercent">A discount off that.</param>
/// <param name="ValidFrom">The first day this line applies.</param>
/// <param name="ValidTo">The last day this line applies.</param>
public sealed record PriceListLineView(
    string ItemNo,
    string? VariantCode,
    string? UnitCode,
    decimal MinimumQuantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    DateOnly? ValidFrom,
    DateOnly? ValidTo);

/// <summary>A price list as it is reported back.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="ValidFrom">The first day it applies.</param>
/// <param name="ValidTo">The last day it applies.</param>
/// <param name="IsActive">Whether it may be used.</param>
/// <param name="Lines">The prices on it, most specific first within each item.</param>
public sealed record PriceListView(
    string Code,
    string Name,
    string? NameArabic,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsActive,
    IReadOnlyList<PriceListLineView> Lines);

/// <summary>Which price list a customer is on, as it is reported back.</summary>
/// <param name="CustomerNo">The customer.</param>
/// <param name="PriceListCode">The list they are on.</param>
public sealed record CustomerPriceListView(string CustomerNo, string PriceListCode);

/// <summary>What a client sends to put a customer on a price list.</summary>
/// <param name="PriceListCode">The list, or null to take them off whatever they are on.</param>
public sealed record AssignPriceListRequest(string? PriceListCode);

/// <summary>Sales orders, shipments and invoices.</summary>
public static class SalesEndpoints
{
    private const string ReadPermission = "Sales.Order.Read";
    private const string PriceReadPermission = "Sales.PriceList.Read";
    private const string PriceWritePermission = "Sales.PriceList.Update";
    private const string CreatePermission = "Sales.Order.Create";
    private const string ShipPermission = "Sales.Shipment.Post";
    private const string InvoicePermission = "Sales.Invoice.Post";
    private const string ReturnPermission = "Sales.Return.Post";
    private const string QuoteReadPermission = "Sales.Quote.Read";
    private const string QuoteWritePermission = "Sales.Quote.Create";

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

        group.MapGet("/reports/margin-by-item", MarginByItemAsync)
             .WithName("MarginByItem")
             .WithSummary("Revenue, cost and margin by item, thinnest margin first.");

        group.MapGet("/reports/margin-by-customer", MarginByCustomerAsync)
             .WithName("MarginByCustomer")
             .WithSummary("Revenue, cost and margin by customer, across every channel.");

        group.MapGet("/reports/open-orders", OpenSalesOrdersAsync)
             .WithName("OpenSalesOrders")
             .WithSummary("What is ordered and has not shipped, latest first.");

        group.MapGet("/quotes", ListQuotesAsync)
             .WithName("ListSalesQuotes")
             .WithSummary("The quotes offered, newest first.");

        group.MapGet("/quotes/{quoteNo}", GetQuoteAsync)
             .WithName("GetSalesQuote")
             .WithSummary("One quote and what is on it.");

        group.MapPost("/quotes", CreateQuoteAsync)
             .WithName("CreateSalesQuote")
             .WithSummary("Offers a customer a price, without promising any stock.");

        group.MapPost("/quotes/{quoteNo}/send", SendQuoteAsync)
             .WithName("SendSalesQuote")
             .WithSummary("Marks a quote as sent to the customer.");

        group.MapPost("/quotes/{quoteNo}/accept", AcceptQuoteAsync)
             .WithName("AcceptSalesQuote")
             .WithSummary("Turns an accepted quote into an order at the prices quoted.");

        group.MapPost("/quotes/{quoteNo}/decline", DeclineQuoteAsync)
             .WithName("DeclineSalesQuote")
             .WithSummary("Records that the customer said no, and why.");

        group.MapPost("/quotes/expire", ExpireQuotesAsync)
             .WithName("ExpireSalesQuotes")
             .WithSummary("Marks every quote that ran out without an answer.");

        group.MapPost("/orders/{orderNo}/return", ReturnAsync)
             .WithName("PostSalesReturn")
             .WithSummary("Takes goods back at what they cost and credits the customer.");

        group.MapGet("/price-lists", ListPriceListsAsync)
             .WithName("ListPriceLists")
             .WithSummary("The agreed price lists and everything on them.");

        group.MapGet("/price-lists/assignments", PriceListAssignmentsAsync)
             .WithName("PriceListAssignments")
             .WithSummary("Who is on which price list.");

        group.MapGet("/price-lists/quote", QuotePriceAsync)
             .WithName("QuotePrice")
             .WithSummary("What one customer pays for one item on one day.");

        group.MapGet("/price-lists/{code}", GetPriceListAsync)
             .WithName("GetPriceList")
             .WithSummary("One price list and its prices.");

        group.MapPut("/price-lists/{code}", SavePriceListAsync)
             .WithName("SavePriceList")
             .WithSummary("Writes a price list and everything on it.");

        group.MapPut("/price-lists/assignments/{customerNo}", AssignPriceListAsync)
             .WithName("AssignPriceList")
             .WithSummary("Puts a customer on a price list, or takes them off one.");

        return app;
    }

    private static async Task<IResult> ListQuotesAsync(
        SalesQuoteService quotes,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        string? status = null,
        string? customerNo = null)
    {
        if (!Can(user, QuoteReadPermission))
        {
            return Forbidden(QuoteReadPermission, "view quotes", http);
        }

        var wanted = Enum.TryParse<SalesQuoteStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : (SalesQuoteStatus?)null;

        var found = await quotes.ListAsync(wanted, customerNo, cancellationToken).ConfigureAwait(false);

        return Results.Ok(found.Select(q => ViewOf(q, clock.Today)));
    }

    private static async Task<IResult> GetQuoteAsync(
        string quoteNo,
        SalesQuoteService quotes,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, QuoteReadPermission))
        {
            return Forbidden(QuoteReadPermission, "view quotes", http);
        }

        var quote = await quotes.LoadAsync(quoteNo, cancellationToken).ConfigureAwait(false);

        return quote is null ? Results.NotFound() : Results.Ok(ViewOf(quote, clock.Today));
    }

    private static async Task<IResult> CreateQuoteAsync(
        CreateQuoteRequest request,
        SalesQuoteService quotes,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, QuoteWritePermission))
        {
            return Forbidden(QuoteWritePermission, "offer a customer a price", http);
        }

        var result = await quotes
            .CreateAsync(
                request.CustomerNo,
                [.. (request.Lines ?? []).Select(static l => new SalesQuoteLineRequest(
                    l.Type,
                    l.No,
                    l.Quantity,
                    l.UnitPrice,
                    l.DiscountPercent,
                    l.Description,
                    l.TaxCode,
                    l.LocationCode,
                    l.VariantCode))],
                request.ValidUntil,
                request.LocationCode,
                request.Description,
                request.CustomerOrderNo,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                quote = ViewOf(result.Value, clock.Today),
                messages = result.Messages,
            });
    }

    private static async Task<IResult> SendQuoteAsync(
        string quoteNo,
        SalesQuoteService quotes,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, QuoteWritePermission))
        {
            return Forbidden(QuoteWritePermission, "send quotes", http);
        }

        var result = await quotes.SendAsync(quoteNo, cancellationToken).ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(ViewOf(result.Value, clock.Today));
    }

    private static async Task<IResult> AcceptQuoteAsync(
        string quoteNo,
        AcceptQuoteRequest request,
        SalesQuoteService quotes,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Accepting writes an order, so it needs what taking an order needs. Somebody who may
        // quote but not order can offer a price and not commit the company to it.
        if (!Can(user, CreatePermission))
        {
            return Forbidden(CreatePermission, "turn a quote into an order", http);
        }

        var result = await quotes
            .AcceptAsync(
                quoteNo,
                request.LocationCode,
                request.RequestedDeliveryDate,
                Overrides(user),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                order = View(result.Value),
                messages = result.Messages,
            });
    }

    private static async Task<IResult> DeclineQuoteAsync(
        string quoteNo,
        DeclineQuoteRequest request,
        SalesQuoteService quotes,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, QuoteWritePermission))
        {
            return Forbidden(QuoteWritePermission, "decline quotes", http);
        }

        var result = await quotes
            .DeclineAsync(quoteNo, request.Reason, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(ViewOf(result.Value, clock.Today));
    }

    private static async Task<IResult> ExpireQuotesAsync(
        SalesQuoteService quotes,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, QuoteWritePermission))
        {
            return Forbidden(QuoteWritePermission, "expire quotes", http);
        }

        var count = await quotes.ExpireAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { expired = count });
    }

    private static QuoteView ViewOf(SalesQuote quote, DateOnly today)
        => new(
            quote.No,
            quote.CustomerNo,
            quote.CustomerName,
            quote.Status.ToString(),
            quote.QuoteDate,
            quote.ValidUntil,
            !quote.StandsOn(today),
            quote.LocationCode,
            quote.CustomerOrderNo,
            quote.Description,
            quote.OrderNo,
            quote.DeclineReason,
            quote.TotalAmount,
            quote.IsEditable && quote.StandsOn(today),
            [.. quote.Lines
                .OrderBy(static l => l.LineNo)
                .Select(static l => new QuoteLineView(
                    l.LineNo,
                    l.Type.ToString(),
                    l.ItemNo ?? l.AccountNo,
                    l.VariantCode,
                    l.Description,
                    l.LocationCode,
                    l.Quantity,
                    l.UnitPrice,
                    l.DiscountPercent,
                    l.TaxCode,
                    l.LineAmount))]);

    private static async Task<IResult> ReturnAsync(
        string orderNo,
        SalesReturnRequest request,
        SalesReturnService returns,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, ReturnPermission))
        {
            return Forbidden(ReturnPermission, "take goods back", http);
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
                netAmount = result.Value.NetAmount,
                discountAmount = result.Value.DiscountAmount,
                taxAmount = result.Value.TaxAmount,
                totalAmount = result.Value.TotalAmount,
                messages = result.Messages,
            });
    }

    private static async Task<IResult> ListPriceListsAsync(
        PricingService pricing,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, PriceReadPermission))
        {
            return Forbidden(PriceReadPermission, "view price lists", http);
        }

        var lists = await pricing.ListsAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(lists.Select(ViewOf));
    }

    private static async Task<IResult> GetPriceListAsync(
        string code,
        PricingService pricing,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, PriceReadPermission))
        {
            return Forbidden(PriceReadPermission, "view price lists", http);
        }

        var list = await pricing.FindAsync(code, cancellationToken).ConfigureAwait(false);

        return list is null ? Results.NotFound() : Results.Ok(ViewOf(list));
    }

    private static async Task<IResult> SavePriceListAsync(
        string code,
        PriceListRequest request,
        PricingService pricing,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, PriceWritePermission))
        {
            return Forbidden(PriceWritePermission, "maintain price lists", http);
        }

        var result = await pricing
            .SaveAsync(request with { Code = code }, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(ViewOf(result.Value));
    }

    private static async Task<IResult> PriceListAssignmentsAsync(
        PricingService pricing,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, PriceReadPermission))
        {
            return Forbidden(PriceReadPermission, "view price list assignments", http);
        }

        var assignments = await pricing.AssignmentsAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(assignments.Select(static a => new CustomerPriceListView(
            a.CustomerNo,
            a.PriceListCode)));
    }

    private static async Task<IResult> AssignPriceListAsync(
        string customerNo,
        AssignPriceListRequest request,
        PricingService pricing,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, PriceWritePermission))
        {
            return Forbidden(PriceWritePermission, "assign price lists", http);
        }

        var result = await pricing
            .AssignAsync(customerNo, request.PriceListCode, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.NoContent();
    }

    private static async Task<IResult> QuotePriceAsync(
        PricingService pricing,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        string customerNo = "",
        string itemNo = "",
        decimal quantity = 1m,
        string? variantCode = null,
        string? unitCode = null,
        DateOnly? on = null)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "quote a price", http);
        }

        var result = await pricing
            .PriceForAsync(
                customerNo,
                itemNo,
                quantity,
                on ?? clock.Today,
                variantCode,
                unitCode,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Failed ? Refused(result, http) : Results.Ok(result.Value);
    }

    private static PriceListView ViewOf(Modules.Sales.Pricing.PriceList list)
        => new(
            list.Code,
            list.Name,
            list.NameArabic,
            list.ValidFrom,
            list.ValidTo,
            list.IsActive,
            [.. list.Lines
                .OrderBy(l => l.ItemNo, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(l => l.Specificity)
                .ThenBy(l => l.MinimumQuantity)
                .Select(static l => new PriceListLineView(
                    l.ItemNo,
                    l.VariantCode,
                    l.UnitCode,
                    l.MinimumQuantity,
                    l.UnitPrice,
                    l.DiscountPercent,
                    l.ValidFrom,
                    l.ValidTo))]);

    private static async Task<IResult> MarginByItemAsync(
        SalesReportService reports,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "read margin by item", http);
        }

        var last = to ?? clock.Today;

        return Results.Ok(await reports
            .MarginByItemAsync(from ?? last.AddMonths(-3), last, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> MarginByCustomerAsync(
        SalesReportService reports,
        IUserContext user,
        IClock clock,
        HttpContext http,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "read margin by customer", http);
        }

        var last = to ?? clock.Today;

        return Results.Ok(await reports
            .MarginByCustomerAsync(from ?? last.AddMonths(-3), last, cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> OpenSalesOrdersAsync(
        SalesReportService reports,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken,
        string? customerNo = null,
        bool overdueOnly = false)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "read open sales orders", http);
        }

        return Results.Ok(await reports
            .OpenOrdersAsync(customerNo, overdueOnly, cancellationToken)
            .ConfigureAwait(false));
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
                    l.ShippedNotInvoiced,
                    l.QuantityReturned,
                    l.ReturnableQuantity))]);

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
