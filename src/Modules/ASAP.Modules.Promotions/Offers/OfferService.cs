using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Promotions.Pricing;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Promotions.Offers;

/// <summary>What an offer would do to the margin on one item, as a report reads it.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is called.</param>
/// <param name="UnitPrice">What it sells for now.</param>
/// <param name="UnitCost">What it costs now.</param>
/// <param name="OfferPrice">What the offer would charge.</param>
/// <param name="MarginPercent">What would be left.</param>
/// <param name="ShortfallPerUnit">How far under the floor, per unit, when it is under.</param>
/// <param name="IsAcceptable">Whether it clears the floor.</param>
public readonly record struct OfferMarginRow(
    string ItemNo,
    string Description,
    decimal UnitPrice,
    decimal UnitCost,
    decimal OfferPrice,
    decimal MarginPercent,
    decimal ShortfallPerUnit,
    bool IsAcceptable);

/// <summary>
/// Keeps offers, and tells somebody what one would do before it runs.
/// </summary>
/// <remarks>
/// The margin preview is the point of this service. An offer is written weeks before it starts,
/// and the person writing it is choosing a percentage, not looking at a cost sheet. Showing what
/// it would do to each item at today's costs, at the moment they save it, is what turns margin
/// protection from a refusal at a till into a decision somebody made on purpose.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="setup">Supplies the margin floor.</param>
/// <param name="logger">Records offers saved.</param>
public sealed class OfferService(
    AsapDbContext context,
    IMessageCatalog messages,
    ISetupService setup,
    ILogger<OfferService> logger)
{
    /// <summary>Every offer, most recently starting first.</summary>
    /// <param name="activeOnly">Whether to leave out the ones switched off.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The offers, with what they apply to.</returns>
    public async Task<IReadOnlyList<Offer>> ListAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<Offer>().AsNoTracking().Include(o => o.Targets).AsQueryable();

        if (activeOnly)
        {
            query = query.Where(static o => o.IsActive);
        }

        return await query
            .OrderByDescending(static o => o.StartsOn)
            .ThenBy(static o => o.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Every offer that could apply today, for the pricing engine to choose between.
    /// </summary>
    /// <remarks>
    /// Narrowed by date here rather than in the engine, because a shop that has been running for
    /// three years has three years of expired offers and a till cannot read them all to sell a
    /// bottle of water. Everything else the engine decides, because everything else depends on
    /// what is in the basket.
    /// </remarks>
    /// <param name="on">The day.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The offers running that day.</returns>
    public async Task<IReadOnlyList<Offer>> RunningAsync(
        DateOnly on,
        CancellationToken cancellationToken = default)
        => await context.Set<Offer>()
            .AsNoTracking()
            .Include(o => o.Targets)
            .Where(o => o.IsActive && o.StartsOn <= on && (o.EndsOn == null || o.EndsOn >= on))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Reads one offer.</summary>
    /// <param name="offerCode">Its code.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The offer, or null when nothing is coded that.</returns>
    public Task<Offer?> LoadAsync(string offerCode, CancellationToken cancellationToken = default)
        => context.Set<Offer>()
            .Include(o => o.Targets)
            .FirstOrDefaultAsync(o => o.Code == offerCode, cancellationToken);

    /// <summary>
    /// Saves an offer, once it makes sense and clears the floor.
    /// </summary>
    /// <param name="offer">The offer, with its targets.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The offer, or every reason it was refused.</returns>
    public async Task<Result<Offer>> SaveAsync(
        Offer offer,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);

        var found = new List<AsapMessage>();

        await CheckAsync(offer, heldOverridePermissions, found, cancellationToken).ConfigureAwait(false);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<Offer>.Failure(found);
        }

        var existing = await LoadAsync(offer.Code, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            context.Set<Offer>().Add(offer);
        }
        else
        {
            Apply(existing, offer);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Saved offer {OfferCode} ({Kind}) running from {StartsOn}.",
            offer.Code,
            offer.Kind,
            offer.StartsOn);

        return Result<Offer>.Success(existing ?? offer, found);
    }

    /// <summary>
    /// What an offer would do to every item it covers, at today's costs.
    /// </summary>
    /// <remarks>
    /// Read by the screen that writes offers, so somebody choosing "twenty per cent off
    /// furniture" sees which pieces of furniture that ruins before they save it rather than at a
    /// till a fortnight later.
    /// </remarks>
    /// <param name="offer">The offer.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per item, worst margin first.</returns>
    public async Task<IReadOnlyList<OfferMarginRow>> PreviewAsync(
        Offer offer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);

        var floor = await FloorAsync(cancellationToken).ConfigureAwait(false);
        var items = await CoveredItemsAsync(offer, cancellationToken).ConfigureAwait(false);

        var rows = new List<OfferMarginRow>();

        foreach (var item in items)
        {
            // One of each, which is the unit the preview reports in. A buy-three-get-one reports
            // the price across a whole deal, because that is what the customer pays for one.
            var quantity = offer.Kind is OfferKind.BuyXGetY
                ? Math.Max(offer.BuyQuantity + offer.GetQuantity, 1m)
                : 1m;

            var line = new BasketLine(
                LineNo: 1,
                ItemNo: item.No,
                CategoryId: item.CategoryId,
                Quantity: quantity,
                UnitPrice: item.UnitPrice,
                UnitCost: item.UnitCost);

            var discount = OfferCalculator.DiscountFor(offer, line, OfferCalculator.NetAmount(line));
            var check = MarginGuard.Check(line, discount, floor);

            rows.Add(new OfferMarginRow(
                item.No,
                item.Description,
                item.UnitPrice,
                item.UnitCost,
                check.OfferUnitPrice,
                check.MarginPercent,
                check.ShortfallPerUnit,
                check.IsAcceptable));
        }

        return [.. rows.OrderBy(static r => r.MarginPercent)];
    }

    /// <summary>The least margin this company accepts.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The floor, as a percentage of the selling price.</returns>
    public async Task<decimal> FloorAsync(CancellationToken cancellationToken = default)
        => await setup
            .GetAsync<decimal>($"{PromotionsModule.Id}.Margin.FloorPercent", cancellationToken)
            .ConfigureAwait(false);

    private async Task CheckAsync(
        Offer offer,
        IReadOnlySet<string>? held,
        List<AsapMessage> found,
        CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OfferCode"] = offer.Code,
            ["Scope"] = offer.Scope.ToString(),
            ["Value"] = offer.Value,
            ["BuyQuantity"] = offer.BuyQuantity,
            ["GetQuantity"] = offer.GetQuantity,
            ["StartsOn"] = offer.StartsOn,
            ["EndsOn"] = offer.EndsOn,
        };

        if (offer.EndsOn is { } ends && ends < offer.StartsOn)
        {
            found.Add(messages.Render(PromotionsMessages.WindowEndsBeforeItStarts, arguments));
        }

        if (offer.Scope is not OfferScope.Everything && offer.Targets.Count == 0)
        {
            found.Add(messages.Render(PromotionsMessages.OfferHasNoTargets, arguments));
        }

        if (offer.Kind is OfferKind.Percentage && offer.Value is < 0m or > 100m)
        {
            found.Add(messages.Render(PromotionsMessages.PercentageOutOfRange, arguments));
        }

        if (offer.Kind is OfferKind.BuyXGetY && (offer.BuyQuantity <= 0m || offer.GetQuantity <= 0m))
        {
            found.Add(messages.Render(PromotionsMessages.BuyGetIncomplete, arguments));
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return;
        }

        // Priced against live cost even here, weeks before it runs. It will be asked again at
        // every till, because costs move -- but somebody choosing a percentage deserves to be
        // told now rather than to hear about it from a cashier.
        foreach (var row in await PreviewAsync(offer, cancellationToken).ConfigureAwait(false))
        {
            if (row.IsAcceptable)
            {
                continue;
            }

            var floor = await FloorAsync(cancellationToken).ConfigureAwait(false);

            found.Add(Raise(
                PromotionsMessages.BelowMarginFloor,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OfferCode"] = offer.Code,
                    ["OfferName"] = offer.Name,
                    ["ItemNo"] = row.ItemNo,
                    ["Description"] = row.Description,
                    ["UnitCost"] = row.UnitCost,
                    ["OfferPrice"] = row.OfferPrice,
                    ["MarginPercent"] = row.MarginPercent,
                    ["FloorPercent"] = floor,
                    ["Shortfall"] = row.ShortfallPerUnit,
                },
                held));
        }
    }

    /// <summary>Every item an offer covers.</summary>
    private async Task<List<Item>> CoveredItemsAsync(Offer offer, CancellationToken cancellationToken)
    {
        var query = context.Set<Item>().AsNoTracking().Where(static i => !i.IsBlocked);

        query = offer.Scope switch
        {
            OfferScope.Item => Named(query, offer),
            OfferScope.Category => InCategories(query, offer),
            _ => query,
        };

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IQueryable<Item> Named(IQueryable<Item> query, Offer offer)
    {
        var itemNos = offer.Targets
            .Select(static t => t.ItemNo)
            .Where(static no => !string.IsNullOrWhiteSpace(no))
            .Select(static no => no!)
            .ToList();

        return query.Where(i => itemNos.Contains(i.No));
    }

    private static IQueryable<Item> InCategories(IQueryable<Item> query, Offer offer)
    {
        var categories = offer.Targets
            .Where(static t => t.CategoryId is not null)
            .Select(static t => t.CategoryId!.Value)
            .ToList();

        return query.Where(i => i.CategoryId != null && categories.Contains(i.CategoryId.Value));
    }

    /// <summary>Copies what may be changed onto the offer that already exists.</summary>
    /// <remarks>
    /// The counters are not copied. They belong to what the offer has done, not to what somebody
    /// is editing, and a save that reset them would lose the uptake report.
    /// </remarks>
    private static void Apply(Offer existing, Offer wanted)
    {
        existing.Name = wanted.Name;
        existing.NameArabic = wanted.NameArabic;
        existing.Kind = wanted.Kind;
        existing.Scope = wanted.Scope;
        existing.Value = wanted.Value;
        existing.BuyQuantity = wanted.BuyQuantity;
        existing.GetQuantity = wanted.GetQuantity;
        existing.GetDiscountPercent = wanted.GetDiscountPercent;
        existing.StartsOn = wanted.StartsOn;
        existing.EndsOn = wanted.EndsOn;
        existing.StartsAt = wanted.StartsAt;
        existing.EndsAt = wanted.EndsAt;
        existing.DaysOfWeek = wanted.DaysOfWeek;
        existing.Channels = wanted.Channels;
        existing.BranchId = wanted.BranchId;
        existing.CustomerGroup = wanted.CustomerGroup;
        existing.CouponCode = wanted.CouponCode;
        existing.Stacking = wanted.Stacking;
        existing.Priority = wanted.Priority;
        existing.IsActive = wanted.IsActive;

        existing.Targets.Clear();

        foreach (var target in wanted.Targets)
        {
            existing.Targets.Add(new OfferTarget
            {
                TenantId = existing.TenantId,
                CompanyId = existing.CompanyId,
                ItemNo = target.ItemNo,
                CategoryId = target.CategoryId,
            });
        }
    }

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
