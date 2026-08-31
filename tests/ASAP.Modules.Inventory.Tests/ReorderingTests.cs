using ASAP.Modules.Inventory.Items;
using Shouldly;

namespace ASAP.Modules.Inventory.Tests;

/// <summary>
/// How much a reorder policy asks for.
/// </summary>
/// <remarks>
/// The one that matters is what happens when goods are already on a lorry. A worksheet that looks
/// only at the shelf suggests the same order every morning until they arrive, and nobody notices
/// until four times what was wanted turns up on the same delivery.
/// </remarks>
public sealed class ReorderingTests
{
    /// <summary>What is coming counts as much as what is here.</summary>
    [Fact]
    public void What_is_on_order_counts_towards_what_is_available()
        => Reordering.Projected(quantityOnHand: 2m, quantityReserved: 0m, quantityOnOrder: 40m)
            .ShouldBe(42m);

    /// <summary>What is promised to somebody else does not count.</summary>
    [Fact]
    public void What_is_promised_to_somebody_else_does_not_count()
        => Reordering.Projected(quantityOnHand: 50m, quantityReserved: 45m, quantityOnOrder: 0m)
            .ShouldBe(5m);

    /// <summary>Above the point, nothing is ordered.</summary>
    [Fact]
    public void Nothing_is_ordered_above_the_point()
        => Reordering.Suggest(Fixed(point: 10m, quantity: 50m), projected: 11m).ShouldBe(0m);

    /// <summary>At the point, it is ordered. The point is a level to be at, not to pass.</summary>
    [Fact]
    public void The_point_itself_triggers_the_order()
        => Reordering.Suggest(Fixed(point: 10m, quantity: 50m), projected: 10m).ShouldBe(50m);

    /// <summary>
    /// A shop with two on the shelf and forty on a lorry does not need forty more.
    /// </summary>
    /// <remarks>
    /// This is the case the whole worksheet exists to get right. Without it a policy that fires
    /// once fires every day until the goods land, and the shop pays for all of them.
    /// </remarks>
    [Fact]
    public void Goods_already_coming_stop_the_order_being_placed_twice()
    {
        var policy = Fixed(point: 10m, quantity: 40m);

        var yesterday = Reordering.Projected(2m, 0m, 0m);
        Reordering.Suggest(policy, yesterday).ShouldBe(40m, "nothing was coming yesterday");

        var today = Reordering.Projected(2m, 0m, 40m);
        Reordering.Suggest(policy, today).ShouldBe(0m, "the same forty are on their way");
    }

    /// <summary>An up-to-maximum policy orders the shortfall, not a fixed amount.</summary>
    [Fact]
    public void Ordering_up_to_a_maximum_orders_the_shortfall()
    {
        var policy = UpTo(point: 10m, maximum: 100m);

        Reordering.Suggest(policy, projected: 10m).ShouldBe(90m);
        Reordering.Suggest(policy, projected: 4m).ShouldBe(96m);
    }

    /// <summary>A negative balance is a shortfall like any other, and a larger one.</summary>
    [Fact]
    public void Stock_already_gone_negative_asks_for_more_not_less()
        => Reordering.Suggest(UpTo(point: 10m, maximum: 100m), projected: -5m).ShouldBe(105m);

    /// <summary>The vendor's minimum lifts a small order up to it.</summary>
    [Fact]
    public void The_vendors_minimum_lifts_a_small_order()
    {
        var policy = UpTo(point: 10m, maximum: 100m);
        policy.MinimumOrderQuantity = 50m;

        Reordering.Suggest(policy, projected: 98m).ShouldBe(0m, "above the point, nothing is wanted");
        Reordering.Suggest(policy, projected: 9m).ShouldBe(91m, "the shortfall already clears it");

        policy.MaximumInventory = 20m;
        Reordering.Suggest(policy, projected: 9m).ShouldBe(50m, "eleven is below what they will ship");
    }

    /// <summary>
    /// A pack is rounded up to, never down.
    /// </summary>
    /// <remarks>
    /// Rounding down would leave the shop below the level it just decided it needed to be above,
    /// and the worksheet would suggest the same order again tomorrow.
    /// </remarks>
    [Fact]
    public void A_pack_is_always_rounded_up()
    {
        var policy = UpTo(point: 10m, maximum: 100m);
        policy.OrderMultiple = 12m;

        Reordering.Suggest(policy, projected: 9m).ShouldBe(96m, "ninety-one wanted, eight cases");
        Reordering.Suggest(policy, projected: -44m).ShouldBe(144m, "a hundred and forty-four wanted exactly");
    }

    /// <summary>The pack has the last word over the vendor's minimum.</summary>
    [Fact]
    public void The_pack_has_the_last_word_over_the_minimum()
    {
        var policy = Fixed(point: 10m, quantity: 4m);
        policy.MinimumOrderQuantity = 10m;
        policy.OrderMultiple = 12m;

        Reordering.Suggest(policy, projected: 0m).ShouldBe(12m, "ten is not a quantity they ship");
    }

    /// <summary>An up-to-maximum policy whose maximum is already met asks for nothing.</summary>
    [Fact]
    public void A_maximum_already_met_asks_for_nothing()
        => Reordering.Suggest(UpTo(point: 100m, maximum: 100m), projected: 100m).ShouldBe(0m);

    private static ReorderPolicy Fixed(decimal point, decimal quantity) => new()
    {
        TenantId = Guid.Empty,
        CompanyId = Guid.Empty,
        ItemNo = "ITEM-1001",
        LocationCode = "MAIN",
        Kind = ReorderKind.FixedQuantity,
        ReorderPoint = point,
        ReorderQuantity = quantity,
    };

    private static ReorderPolicy UpTo(decimal point, decimal maximum) => new()
    {
        TenantId = Guid.Empty,
        CompanyId = Guid.Empty,
        ItemNo = "ITEM-1001",
        LocationCode = "MAIN",
        Kind = ReorderKind.UpToMaximum,
        ReorderPoint = point,
        MaximumInventory = maximum,
    };
}
