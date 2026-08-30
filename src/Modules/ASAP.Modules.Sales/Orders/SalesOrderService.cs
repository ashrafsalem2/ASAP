using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Locations;
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

namespace ASAP.Modules.Sales.Orders;

/// <summary>One line asked for on a new sales order.</summary>
/// <param name="Type">Whether it sells stock or a charge.</param>
/// <param name="No">The item number, or the account number on a charge line.</param>
/// <param name="Quantity">How much to sell. Always positive.</param>
/// <param name="UnitPrice">
/// The price per unit, or zero to take whatever this customer has been agreed.
/// </param>
/// <param name="DiscountPercent">A discount off this line.</param>
/// <param name="Description">What it is. Falls back to the item or account name.</param>
/// <param name="TaxCode">The tax to charge.</param>
/// <param name="LocationCode">Where this line ships from, when it differs from the order.</param>
/// <param name="VariantCode">Which variant of the item, where the item has them.</param>
public readonly record struct SalesOrderLineRequest(
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
/// Takes and amends sales orders.
/// </summary>
/// <remarks>
/// Nothing here posts, and nothing here reserves stock. An order is a promise to the customer, and
/// whether it can be kept is answered when somebody tries to ship it — by the costing engine, which
/// is the only thing that knows what is actually on the shelf at that moment.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="overrides">Records every protection this order pushed past.</param>
/// <param name="numbers">Issues the order number.</param>
/// <param name="setup">Supplies the number series to use.</param>
/// <param name="tenantContext">Supplies the company.</param>
/// <param name="userContext">Records who took it.</param>
/// <param name="pricing">Says what this customer pays, where an arrangement exists.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records orders taken.</param>
public sealed class SalesOrderService(
    AsapDbContext context,
    IMessageCatalog messages,
    OverrideAuditor overrides,
    INumberSeriesService numbers,
    ISetupService setup,
    ITenantContext tenantContext,
    IUserContext userContext,
    IClock clock,
    Pricing.PricingService pricing,
    ILogger<SalesOrderService> logger)
{
    /// <summary>
    /// Takes an order.
    /// </summary>
    /// <param name="customerNo">Who it is for.</param>
    /// <param name="lines">What they are buying.</param>
    /// <param name="locationCode">Where it ships from.</param>
    /// <param name="requestedDeliveryDate">When they want it.</param>
    /// <param name="description">A note for whoever picks it.</param>
    /// <param name="customerOrderNo">Their own order number.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The order, or every reason it was refused.</returns>
    public async Task<Result<SalesOrder>> CreateAsync(
        string customerNo,
        IReadOnlyList<SalesOrderLineRequest> lines,
        string? locationCode = null,
        DateOnly? requestedDeliveryDate = null,
        string? description = null,
        string? customerOrderNo = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var found = new List<AsapMessage>();

        var customer = await context.Set<Customer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.No == customerNo, cancellationToken)
            .ConfigureAwait(false);

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CustomerNo"] = customerNo,
            ["CustomerName"] = customer?.Name,
        };

        if (customer is null)
        {
            found.Add(messages.Render(SalesMessages.CustomerNotFound, arguments));
        }
        else if (customer.IsBlocked)
        {
            found.Add(Raise(SalesMessages.CustomerBlocked, arguments, heldOverridePermissions));
        }

        var wanted = lines.Where(static l => l.Quantity != 0m).ToList();

        if (wanted.Count == 0)
        {
            found.Add(messages.Render(SalesMessages.OrderHasNoLines, arguments));
        }

        var today = clock.Today;

        // Priced before the lines are checked, because the below-cost warning has to be measured
        // against what this customer actually pays. Measuring it against the counter price would
        // let every contract sale below cost through in silence, which is the one case where it
        // matters most.
        var agreed = new List<(decimal UnitPrice, decimal DiscountPercent)>();
        var priceRefusals = new List<AsapMessage>();

        foreach (var line in wanted)
        {
            if (line.Type is not SalesLineType.Item)
            {
                agreed.Add((0m, 0m));
                continue;
            }

            var quoted = await pricing
                .PriceForAsync(
                    customer?.No ?? string.Empty,
                    line.No,
                    line.Quantity,
                    today,
                    line.VariantCode,
                    unitCode: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (quoted.Failed)
            {
                // An item that does not exist is the line check's story to tell, and it tells it
                // against the right line. Saying it twice helps nobody.
                priceRefusals.AddRange(quoted.Messages.Where(static m
                    => m.Code != Inventory.InventoryMessages.ItemNotFound));

                agreed.Add((0m, 0m));
                continue;
            }

            agreed.Add((quoted.Value.UnitPrice, quoted.Value.DiscountPercent));
        }

        if (priceRefusals.Count > 0)
        {
            return Result<SalesOrder>.Failure(priceRefusals);
        }

        var items = await CheckLinesAsync(
                wanted,
                agreed,
                found,
                locationCode,
                heldOverridePermissions,
                cancellationToken)
            .ConfigureAwait(false);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<SalesOrder>.Failure(found);
        }

        var seriesCode = await SeriesCodeAsync(cancellationToken).ConfigureAwait(false);
        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<SalesOrder>.FailureFrom(numbered);
        }

        var order = new SalesOrder
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.RequireCompanyId(),
            No = numbered.Value,
            CustomerId = customer!.Id,
            CustomerNo = customer.No,

            // Copied at creation. An invoice reprinted in three years should say who the customer
            // was when the order was taken.
            CustomerName = customer.Name,
            OrderDate = today,
            RequestedDeliveryDate = requestedDeliveryDate,
            LocationCode = locationCode,
            Status = SalesOrderStatus.Open,
            CustomerOrderNo = customerOrderNo,
            Description = description,
            CreatedBy = userContext.UserId,
        };

        var lineNo = 0;
        var priceIndex = 0;

        foreach (var line in wanted)
        {
            var item = line.Type is SalesLineType.Item ? items.GetValueOrDefault(line.No) : null;
            var arranged = agreed[priceIndex++];

            order.Lines.Add(new SalesOrderLine
            {
                TenantId = order.TenantId,
                CompanyId = order.CompanyId,
                LineNo = ++lineNo * 10,
                Type = line.Type,
                ItemNo = line.Type is SalesLineType.Item ? line.No : null,
                VariantCode = line.Type is SalesLineType.Item ? line.VariantCode : null,
                AccountNo = line.Type is SalesLineType.GlAccount ? line.No : null,
                Description = line.Description ?? item?.Description ?? line.No,
                LocationCode = line.LocationCode,
                Quantity = line.Quantity,

                // What this customer has been agreed, where there is an arrangement, and the
                // counter price where there is not. A figure typed on the line still wins: whoever
                // took the order may have agreed something on the telephone that no list knows.
                UnitPrice = line.UnitPrice != 0m ? line.UnitPrice : arranged.UnitPrice,
                DiscountPercent = line.DiscountPercent != 0m
                    ? line.DiscountPercent
                    : arranged.DiscountPercent,
                TaxCode = line.TaxCode,
            });
        }

        context.Set<SalesOrder>().Add(order);

        overrides.Record(found, "Sales.Order", order.No);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Took sales order {OrderNo} for {CustomerNo} with {LineCount} line(s).",
            order.No,
            order.CustomerNo,
            order.Lines.Count);

        return Result<SalesOrder>.Success(order, found);
    }

    /// <summary>Marks an order as confirmed with the customer.</summary>
    /// <param name="orderNo">The order to release.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The order, or the reason it could not be released.</returns>
    public async Task<Result<SalesOrder>> ReleaseAsync(
        string orderNo,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return NotFound(orderNo);
        }

        if (order.Status is SalesOrderStatus.Open)
        {
            order.Status = SalesOrderStatus.Released;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result<SalesOrder>.Success(order);
    }

    /// <summary>Loads an order and its lines.</summary>
    /// <param name="orderNo">The order number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The order, or null when nothing carries that number.</returns>
    public Task<SalesOrder?> LoadAsync(string orderNo, CancellationToken cancellationToken = default)
        => context.Set<SalesOrder>()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.No == orderNo, cancellationToken);

    /// <summary>Builds the refusal for an order number that matches nothing.</summary>
    /// <param name="orderNo">The number that was asked for.</param>
    /// <returns>The failure.</returns>
    public Result<SalesOrder> NotFound(string orderNo)
        => Result<SalesOrder>.Failure(messages.Render(
            SalesMessages.OrderNotFound,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["OrderNo"] = orderNo,
            }));

    private async Task<Dictionary<string, Item>> CheckLinesAsync(
        IReadOnlyList<SalesOrderLineRequest> lines,
        IReadOnlyList<(decimal UnitPrice, decimal DiscountPercent)> agreed,
        List<AsapMessage> found,
        string? orderLocationCode,
        IReadOnlySet<string>? heldOverridePermissions,
        CancellationToken cancellationToken)
    {
        var itemNos = lines
            .Where(static l => l.Type is SalesLineType.Item)
            .Select(static l => l.No)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = itemNos.Count == 0
            ? []
            : await context.Set<Item>()
                .AsNoTracking()
                .Where(i => itemNos.Contains(i.No))
                .ToDictionaryAsync(static i => i.No, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);

        var locations = await ResolveLocationsAsync(lines, orderLocationCode, cancellationToken)
            .ConfigureAwait(false);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineNo = index + 1;
            var target = MessageTarget.OnField($"Lines[{lineNo}]");

            var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["LineNo"] = lineNo,
                ["ItemNo"] = line.No,
            };

            if (line.Quantity <= 0m)
            {
                found.Add(messages.Render(SalesMessages.QuantityZero, arguments, target));
                continue;
            }

            if (line.Type is not SalesLineType.Item)
            {
                continue;
            }

            if (!items.TryGetValue(line.No, out var item))
            {
                found.Add(messages.Render(SalesMessages.ItemNotFound, arguments, target));
                continue;
            }

            var from = line.LocationCode ?? orderLocationCode;

            if (string.IsNullOrWhiteSpace(from))
            {
                found.Add(messages.Render(SalesMessages.NoLocation, arguments, target));
            }
            else
            {
                CheckLocation(from, locations, arguments, target, heldOverridePermissions, found);
            }

            WarnIfBelowCost(line, item, agreed[index], arguments, target, found);
        }

        return items;
    }

    /// <summary>Loads every location the order or its lines name, in one query.</summary>
    private async Task<Dictionary<string, Location>> ResolveLocationsAsync(
        IReadOnlyList<SalesOrderLineRequest> lines,
        string? orderLocationCode,
        CancellationToken cancellationToken)
    {
        var codes = lines
            .Select(l => l.LocationCode ?? orderLocationCode)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Select(static code => code!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return codes.Count == 0
            ? new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase)
            : await context.Set<Location>()
                .AsNoTracking()
                .Where(l => codes.Contains(l.Code))
                .ToDictionaryAsync(static l => l.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Says so when a line promises goods from somewhere they cannot come from.
    /// </summary>
    /// <remarks>
    /// Asked here rather than left to the shipment, which is where it used to be asked. An order
    /// naming a location that does not exist was taken happily, and the mistake surfaced at the
    /// despatch bay days later, in front of a customer who had been promised a date. The person
    /// who can still fix it cheaply is the one keying the order.
    /// <para>
    /// Both messages belong to Inventory, which owns locations and has already worded them for
    /// exactly this: the sellable one says the stock "must not be promised to a customer", which
    /// is a sentence about an order and not about a lorry.
    /// </para>
    /// </remarks>
    private void CheckLocation(
        string code,
        IReadOnlyDictionary<string, Location> locations,
        Dictionary<string, object?> arguments,
        MessageTarget target,
        IReadOnlySet<string>? held,
        List<AsapMessage> found)
    {
        arguments["Location"] = code;

        if (!locations.TryGetValue(code, out var location))
        {
            found.Add(messages.Render(InventoryMessages.LocationNotFound, arguments, target));
            return;
        }

        // In transit is not a place goods can be picked from, and saying "not sellable" is the
        // truthful answer for it as much as for a quarantine bay.
        if (!location.IsSellable || location.IsInTransit || location.IsBlocked)
        {
            found.Add(Raise(InventoryMessages.LocationNotSellable, arguments, held, target));
        }
    }

    /// <summary>
    /// Says so when a line would sell for less than the goods cost.
    /// </summary>
    /// <remarks>
    /// A warning rather than a refusal, because clearing old stock at a loss is a real decision
    /// somebody is entitled to make. What it must not be is invisible until the margin report
    /// three weeks later, by which time the same price has been quoted to four more customers.
    /// </remarks>
    private void WarnIfBelowCost(
        SalesOrderLineRequest line,
        Item item,
        (decimal UnitPrice, decimal DiscountPercent) agreed,
        Dictionary<string, object?> arguments,
        MessageTarget target,
        List<AsapMessage> found)
    {
        var cost = item.UnitCost > 0m ? item.UnitCost : item.LastDirectCost;

        if (cost <= 0m)
        {
            return;
        }

        var price = line.UnitPrice != 0m ? line.UnitPrice : agreed.UnitPrice;
        var discount = line.DiscountPercent != 0m ? line.DiscountPercent : agreed.DiscountPercent;
        var net = price * (1m - (discount / 100m));

        if (net >= cost)
        {
            return;
        }

        arguments["NetPrice"] = net;
        arguments["UnitCost"] = cost;
        arguments["Shortfall"] = cost - net;

        found.Add(messages.Render(SalesMessages.BelowCost, arguments, target));
    }

    private async Task<string> SeriesCodeAsync(CancellationToken cancellationToken)
        => await setup
               .GetAsync<string>($"{SalesModule.Id}.Orders.NumberSeries", cancellationToken)
               .ConfigureAwait(false)
           ?? "SALES-ORD";

    /// <summary>Renders a message, downgrading a block the caller may override.</summary>
    private AsapMessage Raise(
        MessageCode code,
        Dictionary<string, object?> arguments,
        IReadOnlySet<string>? held,
        MessageTarget target = default)
    {
        var rendered = messages.Render(code, arguments, target);

        return rendered.OverridePermission is { } permission && held?.Contains(permission) == true
            ? messages.AsOverridden(rendered)
            : rendered;
    }
}
