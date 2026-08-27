using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory.Items;
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

namespace ASAP.Modules.Purchasing.Orders;

/// <summary>One line asked for on a new purchase order.</summary>
/// <param name="Type">Whether it buys stock or a cost.</param>
/// <param name="No">The item number, or the account number on a cost line.</param>
/// <param name="Quantity">How much to order. Always positive.</param>
/// <param name="DirectUnitCost">The agreed price per unit, before tax.</param>
/// <param name="Description">What it is. Falls back to the item or account name.</param>
/// <param name="TaxCode">The tax the vendor will charge.</param>
/// <param name="LocationCode">Where this line's goods go, when it differs from the order.</param>
public readonly record struct PurchaseOrderLineRequest(
    PurchaseLineType Type,
    string No,
    decimal Quantity,
    decimal DirectUnitCost,
    string? Description = null,
    string? TaxCode = null,
    string? LocationCode = null);

/// <summary>
/// Raises and amends purchase orders.
/// </summary>
/// <remarks>
/// Nothing here posts. An order is a statement of intent, and the value of keeping it that way is
/// that the two things which do post -- the receipt and the invoice -- can then happen in any
/// order, more than once, and for different quantities, which is what actually happens.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="overrides">Records every protection this order pushed past.</param>
/// <param name="numbers">Issues the order number.</param>
/// <param name="setup">Supplies the number series to use.</param>
/// <param name="tenantContext">Supplies the company.</param>
/// <param name="userContext">Records who raised it.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records orders raised.</param>
public sealed class PurchaseOrderService(
    AsapDbContext context,
    IMessageCatalog messages,
    OverrideAuditor overrides,
    INumberSeriesService numbers,
    ISetupService setup,
    ITenantContext tenantContext,
    IUserContext userContext,
    IClock clock,
    ILogger<PurchaseOrderService> logger)
{
    /// <summary>
    /// Raises an order.
    /// </summary>
    /// <param name="vendorNo">Who it is being ordered from.</param>
    /// <param name="lines">What is being bought.</param>
    /// <param name="locationCode">Where the goods are going.</param>
    /// <param name="expectedReceiptDate">When they are expected.</param>
    /// <param name="description">A note for whoever handles it.</param>
    /// <param name="vendorOrderNo">The vendor's own reference.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The order, or every reason it was refused.</returns>
    public async Task<Result<PurchaseOrder>> CreateAsync(
        string vendorNo,
        IReadOnlyList<PurchaseOrderLineRequest> lines,
        string? locationCode = null,
        DateOnly? expectedReceiptDate = null,
        string? description = null,
        string? vendorOrderNo = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var refusals = new List<AsapMessage>();

        var vendor = await context.Set<Vendor>()
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.No == vendorNo, cancellationToken)
            .ConfigureAwait(false);

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["VendorNo"] = vendorNo,
            ["VendorName"] = vendor?.Name,
        };

        if (vendor is null)
        {
            refusals.Add(messages.Render(PurchasingMessages.VendorNotFound, arguments));
        }
        else if (vendor.IsBlocked)
        {
            refusals.Add(Raise(PurchasingMessages.VendorBlocked, arguments, heldOverridePermissions));
        }

        var wanted = lines.Where(static l => l.Quantity != 0m).ToList();

        if (wanted.Count == 0)
        {
            refusals.Add(messages.Render(PurchasingMessages.OrderHasNoLines, arguments));
        }

        var items = await ResolveItemsAsync(wanted, refusals, locationCode, cancellationToken)
            .ConfigureAwait(false);

        // Every fault at once. Sending them back one at a time turns one correction into four
        // attempts at the same screen.
        if (refusals.Exists(static m => m.IsFailure))
        {
            return Result<PurchaseOrder>.Failure(refusals);
        }

        var today = clock.Today;
        var seriesCode = await SeriesCodeAsync(cancellationToken).ConfigureAwait(false);
        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<PurchaseOrder>.FailureFrom(numbered);
        }

        var order = new PurchaseOrder
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.RequireCompanyId(),
            No = numbered.Value,
            VendorId = vendor!.Id,
            VendorNo = vendor.No,

            // Copied at creation. An order printed in three years should say who the vendor was
            // when it was raised, not who has since taken over the name.
            VendorName = vendor.Name,
            OrderDate = today,
            ExpectedReceiptDate = expectedReceiptDate,
            LocationCode = locationCode,
            Status = PurchaseOrderStatus.Open,
            VendorOrderNo = vendorOrderNo,
            Description = description,
            CreatedBy = userContext.UserId,
        };

        var lineNo = 0;

        foreach (var line in wanted)
        {
            var item = line.Type is PurchaseLineType.Item ? items.GetValueOrDefault(line.No) : null;

            order.Lines.Add(new PurchaseOrderLine
            {
                TenantId = order.TenantId,
                CompanyId = order.CompanyId,

                // In tens, so a line can be inserted between two others without renumbering the
                // rest and invalidating every reference to them.
                LineNo = ++lineNo * 10,
                Type = line.Type,
                ItemNo = line.Type is PurchaseLineType.Item ? line.No : null,
                AccountNo = line.Type is PurchaseLineType.GlAccount ? line.No : null,
                Description = line.Description ?? item?.Description ?? line.No,
                LocationCode = line.LocationCode,
                Quantity = line.Quantity,

                // The item's last known cost when nobody typed one, which is nearly always what
                // was meant and is at least a figure somebody can recognise as wrong.
                DirectUnitCost = line.DirectUnitCost != 0m
                    ? line.DirectUnitCost
                    : item?.LastDirectCost ?? 0m,
                TaxCode = line.TaxCode,
            });
        }

        context.Set<PurchaseOrder>().Add(order);

        // Ordering from a blocked vendor is somebody's decision, taken on their permission.
        overrides.Record(refusals, "Purchasing.Order", order.No);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Raised purchase order {OrderNo} to {VendorNo} with {LineCount} line(s).",
            order.No,
            order.VendorNo,
            order.Lines.Count);

        // Warnings that survived -- a blocked vendor the caller was entitled to override -- travel
        // back with the success, because the order went through despite them.
        return Result<PurchaseOrder>.Success(order, refusals);
    }

    /// <summary>
    /// Marks an order as sent to the vendor.
    /// </summary>
    /// <param name="orderNo">The order to release.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The order, or the reason it could not be released.</returns>
    public async Task<Result<PurchaseOrder>> ReleaseAsync(
        string orderNo,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(orderNo, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return NotFound(orderNo);
        }

        if (order.Status is PurchaseOrderStatus.Open)
        {
            order.Status = PurchaseOrderStatus.Released;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result<PurchaseOrder>.Success(order);
    }

    /// <summary>Loads an order and its lines.</summary>
    /// <param name="orderNo">The order number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The order, or null when nothing carries that number.</returns>
    public Task<PurchaseOrder?> LoadAsync(string orderNo, CancellationToken cancellationToken = default)
        => context.Set<PurchaseOrder>()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.No == orderNo, cancellationToken);

    /// <summary>Builds the refusal for an order number that matches nothing.</summary>
    /// <param name="orderNo">The number that was asked for.</param>
    /// <returns>The failure.</returns>
    public Result<PurchaseOrder> NotFound(string orderNo)
        => Result<PurchaseOrder>.Failure(messages.Render(
            PurchasingMessages.OrderNotFound,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["OrderNo"] = orderNo,
            }));

    private async Task<Dictionary<string, Item>> ResolveItemsAsync(
        IReadOnlyList<PurchaseOrderLineRequest> lines,
        List<AsapMessage> refusals,
        string? orderLocationCode,
        CancellationToken cancellationToken)
    {
        var itemNos = lines
            .Where(static l => l.Type is PurchaseLineType.Item)
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
                refusals.Add(messages.Render(PurchasingMessages.QuantityZero, arguments, target));
                continue;
            }

            if (line.Type is not PurchaseLineType.Item)
            {
                continue;
            }

            if (!items.ContainsKey(line.No))
            {
                refusals.Add(messages.Render(PurchasingMessages.ItemNotFound, arguments, target));
                continue;
            }

            // Stock has to land somewhere, and finding that out at receipt -- with a lorry at the
            // gate -- is later than anybody wants to find it out.
            if (string.IsNullOrWhiteSpace(line.LocationCode)
                && string.IsNullOrWhiteSpace(orderLocationCode))
            {
                refusals.Add(messages.Render(PurchasingMessages.NoLocation, arguments, target));
            }
        }

        return items;
    }

    private async Task<string> SeriesCodeAsync(CancellationToken cancellationToken)
        => await setup
               .GetAsync<string>($"{PurchasingModule.Id}.Orders.NumberSeries", cancellationToken)
               .ConfigureAwait(false)
           ?? "PURCH-ORD";

    /// <summary>Renders a message, downgrading a block the caller may override.</summary>
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
}
