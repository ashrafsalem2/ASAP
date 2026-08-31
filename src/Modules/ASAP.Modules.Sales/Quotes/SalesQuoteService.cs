using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Sales.Orders;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Sales.Quotes;

/// <summary>One line asked for on a new quote.</summary>
/// <param name="Type">Whether it quotes stock or a charge.</param>
/// <param name="No">The item number, or the account number on a charge line.</param>
/// <param name="Quantity">How much to quote for. Always positive.</param>
/// <param name="UnitPrice">
/// The price per unit, or zero to take whatever this customer has been agreed.
/// </param>
/// <param name="DiscountPercent">A discount off this line.</param>
/// <param name="Description">What it is. Falls back to the item or account name.</param>
/// <param name="TaxCode">The tax that would be charged.</param>
/// <param name="LocationCode">Where this line would ship from.</param>
/// <param name="VariantCode">Which variant of the item, where the item has them.</param>
public readonly record struct SalesQuoteLineRequest(
    SalesLineType Type,
    string No,
    decimal Quantity,
    decimal UnitPrice = 0m,
    decimal DiscountPercent = 0m,
    string? Description = null,
    string? TaxCode = null,
    string? LocationCode = null,
    string? VariantCode = null);

/// <summary>
/// Offers a customer a price, and turns it into an order when they accept.
/// </summary>
/// <remarks>
/// <para>
/// Two decisions run through everything here.
/// </para>
/// <para>
/// The first is that a quote checks price and not availability. It refuses a customer who does not
/// exist and an item that does not exist, because neither can be quoted for at all; it says nothing
/// about stock, because quoting for goods that have not arrived is the ordinary use of a lead time
/// rather than a mistake. Availability is decided when the goods are picked, and only then.
/// </para>
/// <para>
/// The second is that accepting carries the quoted prices onto the order verbatim, rather than
/// looking them up again. The price list may well have moved between the quote and the acceptance,
/// and if it has, the customer accepted the number in front of them and not the number the list
/// now holds. Re-pricing on acceptance would quietly charge somebody something they never agreed
/// to, and it would look correct in every report.
/// </para>
/// <para>
/// Which is also why an expired quote is refused rather than repriced. Repricing silently is the
/// same wrong in a different coat.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="orders">Turns an accepted quote into an order, with all the checks that implies.</param>
/// <param name="pricing">Says what this customer pays, where an arrangement exists.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="numbers">Issues the quote number.</param>
/// <param name="setup">Supplies the number series and how long a quote stands by default.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="userContext">Says who is quoting.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records quotes.</param>
public sealed class SalesQuoteService(
    AsapDbContext context,
    SalesOrderService orders,
    Pricing.PricingService pricing,
    IMessageCatalog messages,
    INumberSeriesService numbers,
    ISetupService setup,
    ITenantContext tenancy,
    IUserContext userContext,
    IClock clock,
    ILogger<SalesQuoteService> logger)
{
    /// <summary>
    /// Offers a customer a price.
    /// </summary>
    /// <param name="customerNo">Who it is for.</param>
    /// <param name="lines">What is being quoted.</param>
    /// <param name="validUntil">The last day the prices stand. Defaults to the company's setting.</param>
    /// <param name="locationCode">Where it would ship from.</param>
    /// <param name="description">A note.</param>
    /// <param name="customerOrderNo">Their own reference.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The quote, or every reason it was refused.</returns>
    public async Task<Result<SalesQuote>> CreateAsync(
        string customerNo,
        IReadOnlyList<SalesQuoteLineRequest> lines,
        DateOnly? validUntil = null,
        string? locationCode = null,
        string? description = null,
        string? customerOrderNo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var found = new List<AsapMessage>();

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CustomerNo"] = customerNo,
        };

        var customer = await context.Set<Customer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.No == customerNo, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            found.Add(messages.Render(SalesMessages.CustomerNotFound, arguments));
        }

        if (lines.Count == 0)
        {
            found.Add(messages.Render(SalesMessages.OrderHasNoLines, arguments));
        }

        var today = clock.Today;

        var until = validUntil ?? today.AddDays(await DefaultDaysAsync(cancellationToken).ConfigureAwait(false));

        if (until < today)
        {
            found.Add(messages.Render(
                SalesMessages.QuoteExpiresBeforeItIsMade,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ValidUntil"] = until,
                    ["Today"] = today,
                }));
        }

        var items = await ItemsAsync(lines, cancellationToken).ConfigureAwait(false);

        // A quote checks that what it names exists, and stops there. Whether the goods are on the
        // shelf is a question for the day they are picked.
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineNo = index + 1;
            var target = MessageTarget.OnField($"Lines[{lineNo}]");

            var lineArguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["LineNo"] = lineNo,
                ["ItemNo"] = line.No,
            };

            if (line.Quantity <= 0m)
            {
                found.Add(messages.Render(SalesMessages.QuantityZero, lineArguments, target));
                continue;
            }

            if (line.Type is SalesLineType.Item && !items.ContainsKey(line.No))
            {
                found.Add(messages.Render(SalesMessages.ItemNotFound, lineArguments, target));
            }
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<SalesQuote>.Failure(found);
        }

        var priced = await PriceAsync(customer!.No, lines, items, today, cancellationToken)
            .ConfigureAwait(false);

        if (priced.Failed)
        {
            return Result<SalesQuote>.FailureFrom(priced);
        }

        var seriesCode = await SeriesCodeAsync(cancellationToken).ConfigureAwait(false);
        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<SalesQuote>.FailureFrom(numbered);
        }

        var quote = new SalesQuote
        {
            TenantId = tenancy.TenantId ?? Guid.Empty,
            CompanyId = tenancy.RequireCompanyId(),
            No = numbered.Value,
            CustomerNo = customer.No,
            CustomerName = customer.Name,
            Status = SalesQuoteStatus.Draft,
            QuoteDate = today,
            ValidUntil = until,
            LocationCode = locationCode,
            Description = description,
            CustomerOrderNo = customerOrderNo,
            CreatedBy = userContext.UserId,
        };

        var lineNumber = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var item = line.Type is SalesLineType.Item ? items.GetValueOrDefault(line.No) : null;
            var agreed = priced.Value[index];

            quote.Lines.Add(new SalesQuoteLine
            {
                TenantId = quote.TenantId,
                CompanyId = quote.CompanyId,
                LineNo = ++lineNumber * 10,
                Type = line.Type,
                ItemNo = line.Type is SalesLineType.Item ? line.No : null,
                VariantCode = line.Type is SalesLineType.Item ? line.VariantCode : null,
                AccountNo = line.Type is SalesLineType.GlAccount ? line.No : null,
                Description = line.Description ?? item?.Description ?? line.No,
                LocationCode = line.LocationCode,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice != 0m ? line.UnitPrice : agreed.UnitPrice,
                DiscountPercent = line.DiscountPercent != 0m
                    ? line.DiscountPercent
                    : agreed.DiscountPercent,
                TaxCode = line.TaxCode,
            });
        }

        context.Set<SalesQuote>().Add(quote);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Quoted {QuoteNo} to {CustomerNo}, standing until {ValidUntil}.",
            quote.No,
            quote.CustomerNo,
            quote.ValidUntil);

        return Result<SalesQuote>.Success(quote, found);
    }

    /// <summary>Marks a quote as sent to the customer.</summary>
    /// <param name="quoteNo">The quote.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The quote, or the reason it could not be sent.</returns>
    public async Task<Result<SalesQuote>> SendAsync(
        string quoteNo,
        CancellationToken cancellationToken = default)
    {
        var quote = await LoadAsync(quoteNo, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return NotFound(quoteNo);
        }

        if (quote.Status is SalesQuoteStatus.Draft)
        {
            quote.Status = SalesQuoteStatus.Sent;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result<SalesQuote>.Success(quote);
    }

    /// <summary>
    /// Turns an accepted quote into an order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The prices go across exactly as quoted. Between the quote and the acceptance the price list
    /// may have moved, and the customer accepted the number in front of them rather than the number
    /// the list now holds. Looking it up again would charge somebody something they never agreed to
    /// and it would look right in every report.
    /// </para>
    /// <para>
    /// Everything else is checked afresh, through the ordinary order path: the location, whether
    /// the customer has since been blocked, whether the goods are sellable from where the order
    /// says. Those are properties of the order rather than of the quote, and a quote from three
    /// weeks ago has nothing useful to say about them.
    /// </para>
    /// </remarks>
    /// <param name="quoteNo">The quote the customer accepted.</param>
    /// <param name="locationCode">Where it ships from, if the quote did not say.</param>
    /// <param name="requestedDeliveryDate">When they want it.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The order it became, or every reason it was refused.</returns>
    public async Task<Result<SalesOrder>> AcceptAsync(
        string quoteNo,
        string? locationCode = null,
        DateOnly? requestedDeliveryDate = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        CancellationToken cancellationToken = default)
    {
        var quote = await LoadAsync(quoteNo, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return Result<SalesOrder>.FailureFrom(NotFound(quoteNo));
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["QuoteNo"] = quote.No,
            ["Status"] = quote.Status.ToString(),
            ["ValidUntil"] = quote.ValidUntil,
            ["OrderNo"] = quote.OrderNo,
        };

        if (quote.Status is SalesQuoteStatus.Accepted)
        {
            // Two orders for one acceptance would both look legitimate, and the goods would go
            // out twice.
            return Result<SalesOrder>.Failure(
                messages.Render(SalesMessages.QuoteAlreadyAccepted, arguments));
        }

        if (quote.Status is SalesQuoteStatus.Declined)
        {
            return Result<SalesOrder>.Failure(
                messages.Render(SalesMessages.QuoteWasDeclined, arguments));
        }

        var today = clock.Today;

        if (!quote.StandsOn(today))
        {
            // Refused rather than repriced. Repricing without saying so charges the customer
            // something they never accepted, and it is the accepting that makes it a contract.
            return Result<SalesOrder>.Failure(
                messages.Render(SalesMessages.QuoteHasExpired, arguments));
        }

        var lines = quote.Lines
            .OrderBy(static l => l.LineNo)
            .Select(static l => new SalesOrderLineRequest(
                l.Type,
                l.Type is SalesLineType.Item ? l.ItemNo! : l.AccountNo!,
                l.Quantity,

                // Carried across, never looked up again.
                l.UnitPrice,
                l.DiscountPercent,
                l.Description,
                l.TaxCode,
                l.LocationCode,
                l.VariantCode))
            .ToList();

        var order = await orders
            .CreateAsync(
                quote.CustomerNo,
                lines,
                locationCode ?? quote.LocationCode,
                requestedDeliveryDate,
                quote.Description,
                quote.CustomerOrderNo,
                heldOverridePermissions,
                cancellationToken)
            .ConfigureAwait(false);

        if (order.Failed)
        {
            return order;
        }

        quote.Status = SalesQuoteStatus.Accepted;
        quote.OrderNo = order.Value.No;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Quote {QuoteNo} was accepted and became order {OrderNo}.",
            quote.No,
            order.Value.No);

        return order;
    }

    /// <summary>Records that the customer said no.</summary>
    /// <param name="quoteNo">The quote.</param>
    /// <param name="reason">Why, where they said.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The quote, or the reason it could not be declined.</returns>
    public async Task<Result<SalesQuote>> DeclineAsync(
        string quoteNo,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var quote = await LoadAsync(quoteNo, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return NotFound(quoteNo);
        }

        if (quote.Status is SalesQuoteStatus.Accepted)
        {
            return Result<SalesQuote>.Failure(messages.Render(
                SalesMessages.QuoteAlreadyAccepted,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["QuoteNo"] = quote.No,
                    ["Status"] = quote.Status.ToString(),
                    ["OrderNo"] = quote.OrderNo,
                }));
        }

        quote.Status = SalesQuoteStatus.Declined;
        quote.DeclineReason = reason;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<SalesQuote>.Success(quote);
    }

    /// <summary>
    /// Marks every quote that ran out without an answer.
    /// </summary>
    /// <remarks>
    /// A quote expires whether or not anybody runs this: acceptance reads the date. This exists so
    /// the list reads truthfully, and so that a quote nobody answered can be told apart from one
    /// still waiting — which is the whole of what a win rate is made of.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many were marked.</returns>
    public async Task<int> ExpireAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        var stale = await context.Set<SalesQuote>()
            .Where(q => q.ValidUntil < today
                && (q.Status == SalesQuoteStatus.Draft || q.Status == SalesQuoteStatus.Sent))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var quote in stale)
        {
            quote.Status = SalesQuoteStatus.Expired;
        }

        if (stale.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Marked {Count} quote(s) as expired.", stale.Count);
        }

        return stale.Count;
    }

    /// <summary>Loads a quote and its lines.</summary>
    /// <param name="quoteNo">The quote number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The quote, or null when nothing carries that number.</returns>
    public Task<SalesQuote?> LoadAsync(string quoteNo, CancellationToken cancellationToken = default)
        => context.Set<SalesQuote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.No == quoteNo, cancellationToken);

    /// <summary>The quotes, newest first.</summary>
    /// <param name="status">One status, or null for all of them.</param>
    /// <param name="customerNo">One customer, or null for all of them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The quotes.</returns>
    public async Task<IReadOnlyList<SalesQuote>> ListAsync(
        SalesQuoteStatus? status = null,
        string? customerNo = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<SalesQuote>().AsNoTracking().Include(q => q.Lines).AsQueryable();

        if (status is { } wanted)
        {
            query = query.Where(q => q.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(customerNo))
        {
            query = query.Where(q => q.CustomerNo == customerNo);
        }

        return await query
            .OrderByDescending(q => q.No)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Builds the refusal for a quote number that matches nothing.</summary>
    /// <param name="quoteNo">The number that was asked for.</param>
    /// <returns>The failure.</returns>
    public Result<SalesQuote> NotFound(string quoteNo)
        => Result<SalesQuote>.Failure(messages.Render(
            SalesMessages.QuoteNotFound,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["QuoteNo"] = quoteNo,
            }));

    private async Task<Result<List<(decimal UnitPrice, decimal DiscountPercent)>>> PriceAsync(
        string customerNo,
        IReadOnlyList<SalesQuoteLineRequest> lines,
        IReadOnlyDictionary<string, Item> items,
        DateOnly on,
        CancellationToken cancellationToken)
    {
        var agreed = new List<(decimal UnitPrice, decimal DiscountPercent)>();
        var refusals = new List<AsapMessage>();

        foreach (var line in lines)
        {
            if (line.Type is not SalesLineType.Item)
            {
                agreed.Add((0m, 0m));
                continue;
            }

            var quoted = await pricing
                .PriceForAsync(
                    customerNo,
                    line.No,
                    line.Quantity,
                    on,
                    line.VariantCode,
                    unitCode: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (quoted.Failed)
            {
                refusals.AddRange(quoted.Messages);
                agreed.Add((0m, 0m));
                continue;
            }

            agreed.Add((quoted.Value.UnitPrice, quoted.Value.DiscountPercent));
        }

        return refusals.Count > 0
            ? Result<List<(decimal UnitPrice, decimal DiscountPercent)>>.Failure(refusals)
            : Result<List<(decimal UnitPrice, decimal DiscountPercent)>>.Success(agreed);
    }

    private async Task<Dictionary<string, Item>> ItemsAsync(
        IReadOnlyList<SalesQuoteLineRequest> lines,
        CancellationToken cancellationToken)
    {
        var itemNos = lines
            .Where(static l => l.Type is SalesLineType.Item)
            .Select(static l => l.No)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return itemNos.Count == 0
            ? []
            : await context.Set<Item>()
                .AsNoTracking()
                .Where(i => itemNos.Contains(i.No))
                .ToDictionaryAsync(static i => i.No, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<int> DefaultDaysAsync(CancellationToken cancellationToken)
        => await setup
            .GetAsync<int>($"{SalesModule.Id}.Quotes.ValidForDays", cancellationToken)
            .ConfigureAwait(false);

    private async Task<string> SeriesCodeAsync(CancellationToken cancellationToken)
        => await setup
               .GetAsync<string>($"{SalesModule.Id}.Quotes.NumberSeries", cancellationToken)
               .ConfigureAwait(false)
           ?? "SALES-QTE";
}
