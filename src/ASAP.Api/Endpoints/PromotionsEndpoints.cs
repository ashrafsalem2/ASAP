using ASAP.Api.Infrastructure;
using ASAP.Modules.Promotions.Offers;
using ASAP.Platform.Kernel.Security;
using Microsoft.AspNetCore.Mvc;

namespace ASAP.Api.Endpoints;

/// <summary>One thing an offer applies to, as a client sends it.</summary>
/// <param name="ItemNo">The item, on an item-scoped offer.</param>
/// <param name="CategoryId">The category, on a category-scoped offer.</param>
public sealed record OfferTargetPayload(string? ItemNo = null, Guid? CategoryId = null);

/// <summary>What a client sends to write an offer.</summary>
/// <param name="Code">Its code, which appears on receipts.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Kind">What shape it takes.</param>
/// <param name="Scope">What it applies to.</param>
/// <param name="Value">The percentage, amount, threshold or fixed price.</param>
/// <param name="BuyQuantity">On a buy-X-get-Y, how many must be bought.</param>
/// <param name="GetQuantity">On a buy-X-get-Y, how many are then free or reduced.</param>
/// <param name="GetDiscountPercent">What percentage off the free ones get.</param>
/// <param name="StartsOn">The first day it runs.</param>
/// <param name="EndsOn">The last day, or null for open-ended.</param>
/// <param name="StartsAt">The first minute of the day, for a happy hour.</param>
/// <param name="EndsAt">The last minute of the day.</param>
/// <param name="DaysOfWeek">Which days, as a bit per day, or null for every day.</param>
/// <param name="Channels">Where it applies.</param>
/// <param name="BranchId">The branch it is limited to, or null for every branch.</param>
/// <param name="CustomerGroup">The customer group, or null for everybody.</param>
/// <param name="CouponCode">The coupon that unlocks it, or null when nothing does.</param>
/// <param name="Stacking">What happens when more than one offer could apply.</param>
/// <param name="Priority">Which offer wins a tie.</param>
/// <param name="IsActive">Whether it may be applied at all.</param>
/// <param name="Targets">What it applies to, when the scope is not everything.</param>
/// <param name="OverrideReason">Why a protection is being pushed past.</param>
public sealed record SaveOfferRequest(
    string Code,
    string Name,
    OfferKind Kind,
    OfferScope Scope,
    DateOnly StartsOn,
    string? NameArabic = null,
    decimal Value = 0m,
    decimal BuyQuantity = 0m,
    decimal GetQuantity = 0m,
    decimal GetDiscountPercent = 100m,
    DateOnly? EndsOn = null,
    TimeOnly? StartsAt = null,
    TimeOnly? EndsAt = null,
    int? DaysOfWeek = null,
    SalesChannel Channels = SalesChannel.All,
    Guid? BranchId = null,
    string? CustomerGroup = null,
    string? CouponCode = null,
    StackingRule Stacking = StackingRule.Stacks,
    int Priority = 0,
    bool IsActive = true,
    IReadOnlyList<OfferTargetPayload>? Targets = null,
    string? OverrideReason = null);

/// <summary>Offers, and what they would do before they run.</summary>
public static class PromotionsEndpoints
{
    private const string ReadPermission = "Promotions.Offer.Read";
    private const string CreatePermission = "Promotions.Offer.Create";
    private const string UpdatePermission = "Promotions.Offer.Update";

    /// <summary>Maps the Promotions endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPromotionsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/promotions").RequireAuthorization().WithTags("Promotions");

        group.MapGet("/offers", ListAsync)
             .WithName("Offers")
             .WithSummary("Lists offers, most recently starting first.");

        group.MapGet("/offers/{offerCode}", GetAsync)
             .WithName("Offer")
             .WithSummary("Reads one offer and what it applies to.");

        group.MapPost("/offers", SaveAsync)
             .WithName("SaveOffer")
             .WithSummary("Writes an offer, once it makes sense and clears the margin floor.");

        group.MapPost("/offers/preview", PreviewAsync)
             .WithName("PreviewOffer")
             .WithSummary("What an offer would do to every item it covers, at today's costs.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        OfferService offers,
        IUserContext user,
        HttpContext http,
        [FromQuery] bool? activeOnly,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view offers", http);
        }

        var found = await offers.ListAsync(activeOnly ?? false, cancellationToken).ConfigureAwait(false);

        return Results.Ok(found.Select(View));
    }

    private static async Task<IResult> GetAsync(
        string offerCode,
        OfferService offers,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "view offers", http);
        }

        var offer = await offers.LoadAsync(offerCode, cancellationToken).ConfigureAwait(false);

        return offer is null ? Results.NotFound() : Results.Ok(View(offer));
    }

    private static async Task<IResult> SaveAsync(
        SaveOfferRequest request,
        OfferService offers,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, CreatePermission) && !Can(user, UpdatePermission))
        {
            return Forbidden(CreatePermission, "write offers", http);
        }

        var result = await offers
            .SaveAsync(From(request), Overrides(user), request.OverrideReason, cancellationToken)
            .ConfigureAwait(false);

        return result.Failed
            ? Refused(result, http)
            : Results.Ok(new
            {
                offer = View(result.Value),
                messages = MessagePayload.FromAll(result.Messages),
            });
    }

    /// <summary>
    /// What an offer would do, without saving it.
    /// </summary>
    /// <remarks>
    /// The whole reason the offer screen is worth building. Somebody choosing "twenty per cent off
    /// furniture" is picking a percentage, not reading a cost sheet, and this is what shows them
    /// which pieces of furniture that ruins before they commit rather than at a till a fortnight
    /// later.
    /// </remarks>
    private static async Task<IResult> PreviewAsync(
        SaveOfferRequest request,
        OfferService offers,
        IUserContext user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Can(user, ReadPermission))
        {
            return Forbidden(ReadPermission, "preview offers", http);
        }

        var rows = await offers.PreviewAsync(From(request), cancellationToken).ConfigureAwait(false);
        var floor = await offers.FloorAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            floorPercent = floor,
            worst = rows.Count > 0 ? rows[0].MarginPercent : (decimal?)null,
            breaches = rows.Count(static r => !r.IsAcceptable),
            rows,
        });
    }

    private static Offer From(SaveOfferRequest request)
    {
        var offer = new Offer
        {
            Code = request.Code,
            Name = request.Name,
            NameArabic = request.NameArabic,
            Kind = request.Kind,
            Scope = request.Scope,
            Value = request.Value,
            BuyQuantity = request.BuyQuantity,
            GetQuantity = request.GetQuantity,
            GetDiscountPercent = request.GetDiscountPercent,
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            DaysOfWeek = request.DaysOfWeek,
            Channels = request.Channels,
            BranchId = request.BranchId,
            CustomerGroup = request.CustomerGroup,
            CouponCode = request.CouponCode,
            Stacking = request.Stacking,
            Priority = request.Priority,
            IsActive = request.IsActive,
        };

        foreach (var target in request.Targets ?? [])
        {
            offer.Targets.Add(new OfferTarget
            {
                ItemNo = target.ItemNo,
                CategoryId = target.CategoryId,
            });
        }

        return offer;
    }

    private static object View(Offer offer)
        => new
        {
            code = offer.Code,
            name = offer.Name,
            nameArabic = offer.NameArabic,
            kind = offer.Kind.ToString(),
            scope = offer.Scope.ToString(),
            value = offer.Value,
            buyQuantity = offer.BuyQuantity,
            getQuantity = offer.GetQuantity,
            getDiscountPercent = offer.GetDiscountPercent,
            startsOn = offer.StartsOn,
            endsOn = offer.EndsOn,
            startsAt = offer.StartsAt,
            endsAt = offer.EndsAt,
            daysOfWeek = offer.DaysOfWeek,
            channels = offer.Channels.ToString(),
            branchId = offer.BranchId,
            customerGroup = offer.CustomerGroup,
            couponCode = offer.CouponCode,
            stacking = offer.Stacking.ToString(),
            priority = offer.Priority,
            isActive = offer.IsActive,
            timesApplied = offer.TimesApplied,
            totalGivenAway = offer.TotalGivenAway,
            targets = offer.Targets.Select(static t => new OfferTargetPayload(t.ItemNo, t.CategoryId)),
        };

    /// <summary>
    /// The overrides this caller holds.
    /// </summary>
    /// <remarks>
    /// Only the margin one. Running an offer that sells below the floor is a decision somebody is
    /// entitled to make — clearing old stock is a real reason — but it is made here, on the screen
    /// the offer is written on, and not by a cashier at a counter.
    /// </remarks>
    private static IReadOnlySet<string> Overrides(IUserContext user)
        => new[] { "Promotions.Offer.Override" }
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
