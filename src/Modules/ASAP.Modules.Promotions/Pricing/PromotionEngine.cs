using ASAP.Modules.Promotions.Offers;
using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Modules.Promotions.Pricing;

/// <summary>What an item is called, for messages. Supplied by the caller, which already knows.</summary>
/// <param name="ItemNo">The item.</param>
/// <param name="Description">What it is called.</param>
public readonly record struct ItemName(string ItemNo, string? Description);

/// <summary>
/// Decides which offers apply to a basket, and what they take off.
/// </summary>
/// <remarks>
/// <para>
/// The rule people expect is that the customer gets the best offer available, and the engine
/// starts there. What complicates it is that "best" has to be decided per line and then checked
/// against the basket, because a blocking offer somewhere can switch everything else off.
/// </para>
/// <para>
/// Nothing here touches the database or the clock. Which offers are running, what things cost and
/// who is buying are all resolved by the caller and passed in, which is what lets three-for-two,
/// happy hours and margin floors be tested without a shop.
/// </para>
/// </remarks>
public sealed class PromotionEngine(IMessageCatalog messages)
{
    /// <summary>
    /// Prices a basket against the offers that are running.
    /// </summary>
    /// <param name="lines">What is being sold, with today's costs.</param>
    /// <param name="offers">Every offer that exists. Eligibility is decided here.</param>
    /// <param name="context">When, where and who.</param>
    /// <param name="marginFloorPercent">The least margin this company accepts.</param>
    /// <param name="names">What the items are called, for messages.</param>
    /// <param name="found">Where messages are collected.</param>
    /// <returns>What came off, per line and per offer.</returns>
    /// <remarks>
    /// An offer that would break the floor is left out and said so, as a warning. It is
    /// emphatically not a refusal here: a shop must not stop selling water because somebody
    /// misconfigured a promotion on it last week. The refusal belongs where it can be acted on,
    /// which is the screen the offer was written on.
    /// </remarks>
    public PricedBasket Price(
        IReadOnlyList<BasketLine> lines,
        IReadOnlyList<Offer> offers,
        BasketContext context,
        decimal marginFloorPercent,
        IReadOnlyDictionary<string, string?>? names,
        List<AsapMessage> found)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(found);

        var running = offers.Where(offer => IsEligible(offer, context)).ToList();

        if (running.Count == 0 || lines.Count == 0)
        {
            return new PricedBasket([], 0m);
        }

        // A blocking offer switches everything else off, so it is settled before anything is
        // worked out per line. An offer whose funding depends on being the only one cannot be
        // discovered to be one of several after the arithmetic is done.
        var blocking = running
            .Where(static o => o.Stacking is StackingRule.Blocking)
            .Where(offer => lines.Any(line => Applies(offer, line)))
            .OrderByDescending(static o => o.Priority)
            .FirstOrDefault();

        if (blocking is not null)
        {
            running = [blocking];
        }

        var applied = new List<AppliedDiscount>();

        foreach (var line in lines)
        {
            var candidates = running.Where(offer => Applies(offer, line)).ToList();

            if (candidates.Count == 0)
            {
                continue;
            }

            var qualifying = QualifyingAmount(lines, candidates);

            foreach (var discount in ChooseFor(line, candidates, qualifying))
            {
                var check = MarginGuard.Check(line, TotalOn(applied, line.LineNo) + discount.Amount, marginFloorPercent);

                if (!check.IsAcceptable)
                {
                    var offer = candidates.Single(o => o.Code == discount.OfferCode);

                    // Left out, and said so. The customer pays the ordinary price and the shop
                    // keeps trading; somebody who maintains offers gets told what to look at.
                    found.Add(messages.Render(
                        PromotionsMessages.OfferNotApplied,
                        MarginGuard.Arguments(check, offer, names?.GetValueOrDefault(line.ItemNo)),
                        MarginGuard.TargetFor(check)));

                    continue;
                }

                applied.Add(discount);
            }
        }

        return new PricedBasket(applied, Round(applied.Sum(static d => d.Amount)));
    }

    /// <summary>
    /// Which offers actually get applied to a line, in the order they are taken off.
    /// </summary>
    /// <remarks>
    /// The exclusive one worth most to the customer wins, and priority only breaks a tie. An offer
    /// that won on priority while being worth less would be a shop quietly choosing the cheaper
    /// discount, which is the sort of thing that ends up in a newspaper.
    /// </remarks>
    private static List<AppliedDiscount> ChooseFor(
        BasketLine line,
        IReadOnlyList<Offer> candidates,
        decimal qualifying)
    {
        var valued = candidates
            .Select(offer => (Offer: offer, Amount: OfferCalculator.DiscountFor(offer, line, qualifying)))
            .Where(static c => c.Amount > 0m)
            .ToList();

        if (valued.Count == 0)
        {
            return [];
        }

        var best = valued
            .Where(static c => c.Offer.Stacking is not StackingRule.Stacks)
            .OrderByDescending(static c => c.Amount)
            .ThenByDescending(static c => c.Offer.Priority)
            .Select(static c => (c.Offer, c.Amount))
            .FirstOrDefault();

        var stackable = valued
            .Where(static c => c.Offer.Stacking is StackingRule.Stacks)
            .OrderByDescending(static c => c.Offer.Priority)
            .ToList();

        // An exclusive offer applies alone. Where it beats everything the stackable ones would
        // have come to together, it is the better deal and it wins; where it does not, the
        // customer keeps the combination.
        if (best.Offer is not null)
        {
            var stacked = stackable.Sum(static c => c.Amount);

            if (best.Amount >= stacked)
            {
                return [Discount(line, best.Offer, best.Amount)];
            }
        }

        return [.. stackable.Select(c => Discount(line, c.Offer, c.Amount))];
    }

    /// <summary>
    /// What the qualifying lines come to, for an offer measured across several of them.
    /// </summary>
    /// <remarks>
    /// Only the lines an offer actually applies to. Measured across the whole basket, a bag of
    /// crisps would unlock a threshold discount on furniture.
    /// </remarks>
    private static decimal QualifyingAmount(IReadOnlyList<BasketLine> lines, IReadOnlyList<Offer> candidates)
        => Round(lines
            .Where(line => candidates.Any(offer => Applies(offer, line)))
            .Sum(OfferCalculator.NetAmount));

    /// <summary>Whether an offer is running at all, ignoring what is in the basket.</summary>
    private static bool IsEligible(Offer offer, BasketContext context)
    {
        if (!offer.IsActive || !offer.RunsOn(context.On) || !offer.RunsAt(context.At))
        {
            return false;
        }

        if ((offer.Channels & context.Channel) == 0)
        {
            return false;
        }

        if (offer.BranchId is { } branch && branch != context.BranchId)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(offer.CustomerGroup)
            && !string.Equals(offer.CustomerGroup, context.CustomerGroup, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A coupon offer is off until somebody produces the coupon. That is what a coupon is.
        return string.IsNullOrWhiteSpace(offer.CouponCode)
               || context.CouponCodes?.Contains(offer.CouponCode, StringComparer.OrdinalIgnoreCase) == true;
    }

    /// <summary>Whether an offer covers what is on this line.</summary>
    private static bool Applies(Offer offer, BasketLine line)
        => offer.Scope switch
        {
            OfferScope.Everything => true,
            OfferScope.Item => offer.Targets.Any(t =>
                string.Equals(t.ItemNo, line.ItemNo, StringComparison.OrdinalIgnoreCase)),
            OfferScope.Category => line.CategoryId is { } category
                                   && offer.Targets.Any(t => t.CategoryId == category),
            _ => false,
        };

    private static AppliedDiscount Discount(BasketLine line, Offer offer, decimal amount)
        => new(line.LineNo, offer.Code, offer.Name, amount);

    private static decimal TotalOn(IReadOnlyList<AppliedDiscount> applied, int lineNo)
        => applied.Where(d => d.LineNo == lineNo).Sum(static d => d.Amount);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
