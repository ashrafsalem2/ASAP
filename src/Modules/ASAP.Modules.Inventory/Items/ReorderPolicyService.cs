using ASAP.Modules.Inventory.Locations;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Items;

/// <summary>A reorder policy as somebody asks for it to be saved.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="LocationCode">Where it is stocked.</param>
/// <param name="Kind">Whether to order a fixed quantity or up to a maximum.</param>
/// <param name="ReorderPoint">The level at or below which it should be reordered.</param>
/// <param name="ReorderQuantity">How much to order, on a fixed-quantity policy.</param>
/// <param name="MaximumInventory">The level to order back up to, on an up-to-maximum policy.</param>
/// <param name="MinimumOrderQuantity">The least the vendor will ship.</param>
/// <param name="OrderMultiple">The pack it is sold in.</param>
/// <param name="LeadTimeDays">Days between ordering and arrival.</param>
/// <param name="VendorNo">A vendor it is normally bought from.</param>
/// <param name="VariantCode">Which variant, on an item that has them.</param>
/// <param name="IsActive">Whether the worksheet still looks at it.</param>
public sealed record ReorderPolicyRequest(
    string ItemNo,
    string LocationCode,
    ReorderKind Kind = ReorderKind.FixedQuantity,
    decimal ReorderPoint = 0m,
    decimal ReorderQuantity = 0m,
    decimal MaximumInventory = 0m,
    decimal MinimumOrderQuantity = 0m,
    decimal OrderMultiple = 0m,
    int LeadTimeDays = 0,
    string? VendorNo = null,
    string? VariantCode = null,
    bool IsActive = true);

/// <summary>
/// Keeps the reorder policies: what each place wants to hold of each item.
/// </summary>
/// <remarks>
/// The refusals here are all the same kind of thing — a policy that could never suggest anything.
/// A maximum below the reorder point, a fixed-quantity policy with no quantity, an up-to-maximum
/// policy with no maximum: each of them saves cleanly, sits in the list looking configured, and
/// contributes nothing to every run thereafter. Refusing them at the point of entry is the only
/// moment anybody is looking.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenancy">Says which company this is.</param>
public sealed class ReorderPolicyService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenancy)
{
    /// <summary>Every policy, newest first by item.</summary>
    /// <param name="locationCode">One location, or null for all of them.</param>
    /// <param name="activeOnly">Whether to leave out the ones switched off.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The policies.</returns>
    public async Task<IReadOnlyList<ReorderPolicy>> ListAsync(
        string? locationCode = null,
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<ReorderPolicy>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(locationCode))
        {
            var code = locationCode.Trim().ToUpperInvariant();
            query = query.Where(p => p.LocationCode == code);
        }

        if (activeOnly)
        {
            query = query.Where(static p => p.IsActive);
        }

        return await query
            .OrderBy(p => p.ItemNo)
            .ThenBy(p => p.LocationCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Writes a policy, replacing whatever the item and place had before.</summary>
    /// <param name="request">The policy.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The saved policy, or why it could not be saved.</returns>
    public async Task<Result<ReorderPolicy>> SaveAsync(
        ReorderPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var itemNo = request.ItemNo?.Trim().ToUpperInvariant() ?? string.Empty;
        var locationCode = request.LocationCode?.Trim().ToUpperInvariant() ?? string.Empty;

        if (itemNo.Length == 0 || locationCode.Length == 0)
        {
            return Result<ReorderPolicy>.Failure(
                messages.Render(InventoryMessages.ReorderPolicyIncomplete, Args()));
        }

        var found = new List<AsapMessage>();
        var arguments = Args(
            ("ItemNo", itemNo),
            ("LocationCode", locationCode),
            ("ReorderPoint", request.ReorderPoint),
            ("MaximumInventory", request.MaximumInventory));

        var item = await context.Set<Item>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.No == itemNo, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            found.Add(messages.Render(InventoryMessages.ItemNotFound, arguments));
        }

        var location = await context.Set<Location>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Code == locationCode, cancellationToken)
            .ConfigureAwait(false);

        if (location is null)
        {
            found.Add(messages.Render(InventoryMessages.LocationNotFound, arguments));
        }

        if (request.Kind is ReorderKind.UpToMaximum)
        {
            if (request.MaximumInventory <= 0m)
            {
                found.Add(messages.Render(InventoryMessages.ReorderMaximumMissing, arguments));
            }
            else if (request.MaximumInventory <= request.ReorderPoint)
            {
                found.Add(messages.Render(InventoryMessages.ReorderMaximumBelowPoint, arguments));
            }
        }
        else if (request.ReorderQuantity <= 0m)
        {
            found.Add(messages.Render(InventoryMessages.ReorderQuantityMissing, arguments));
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<ReorderPolicy>.Failure(found);
        }

        var policy = await context.Set<ReorderPolicy>()
            .FirstOrDefaultAsync(
                p => p.ItemNo == itemNo && p.LocationCode == locationCode,
                cancellationToken)
            .ConfigureAwait(false);

        if (policy is null)
        {
            policy = new ReorderPolicy
            {
                TenantId = tenancy.RequireTenantId(),
                CompanyId = tenancy.RequireCompanyId(),
                ItemNo = itemNo,
                LocationCode = locationCode,
            };

            context.Set<ReorderPolicy>().Add(policy);
        }

        policy.VariantCode = request.VariantCode?.Trim().ToUpperInvariant();
        policy.Kind = request.Kind;
        policy.ReorderPoint = request.ReorderPoint;
        policy.ReorderQuantity = request.ReorderQuantity;
        policy.MaximumInventory = request.MaximumInventory;
        policy.MinimumOrderQuantity = request.MinimumOrderQuantity;
        policy.OrderMultiple = request.OrderMultiple;
        policy.LeadTimeDays = request.LeadTimeDays;
        policy.VendorNo = request.VendorNo?.Trim().ToUpperInvariant();
        policy.IsActive = request.IsActive;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ReorderPolicy>.Success(policy, found);
    }

    /// <summary>Removes a policy, leaving the place with no rule for that item.</summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="locationCode">Where it is stocked.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether anything was removed.</returns>
    public async Task<Result> RemoveAsync(
        string itemNo,
        string locationCode,
        CancellationToken cancellationToken = default)
    {
        var item = itemNo?.Trim().ToUpperInvariant() ?? string.Empty;
        var location = locationCode?.Trim().ToUpperInvariant() ?? string.Empty;

        var policy = await context.Set<ReorderPolicy>()
            .FirstOrDefaultAsync(
                p => p.ItemNo == item && p.LocationCode == location,
                cancellationToken)
            .ConfigureAwait(false);

        if (policy is not null)
        {
            context.Set<ReorderPolicy>().Remove(policy);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}
