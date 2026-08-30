using ASAP.Modules.Inventory.Adjustments;
using Shouldly;

namespace ASAP.Modules.Inventory.Tests.Adjustments;

/// <summary>
/// Covers which way a reason may move stock.
/// </summary>
/// <remarks>
/// The check earns its place because getting it wrong is invisible. Breakage recorded as an
/// increase produces an entry that looks perfectly valid in every report that reads it: the
/// quantity is right, the account is right, and the only thing wrong is that the company appears
/// to have gained stock by breaking it.
/// </remarks>
public sealed class AdjustmentReasonTests
{
    private static AdjustmentReason Reason(AdjustmentDirection direction)
        => new() { Code = "X", Name = "X", Direction = direction };

    [Theory]
    [InlineData(AdjustmentDirection.DecreaseOnly, -5, true)]
    [InlineData(AdjustmentDirection.DecreaseOnly, 5, false)]
    [InlineData(AdjustmentDirection.IncreaseOnly, 5, true)]
    [InlineData(AdjustmentDirection.IncreaseOnly, -5, false)]
    [InlineData(AdjustmentDirection.Either, 5, true)]
    [InlineData(AdjustmentDirection.Either, -5, true)]
    public void A_reason_only_moves_stock_the_way_it_is_meant_to(
        AdjustmentDirection direction,
        decimal quantity,
        bool permitted)
        => Reason(direction).Permits(quantity).ShouldBe(permitted);

    [Fact]
    public void A_one_way_reason_permits_nothing_at_all_in_either_direction_when_nothing_moves()
    {
        // Nought is neither up nor down. The zero-quantity check catches it first and says so in
        // its own words; this one must not quietly wave it through as an increase.
        Reason(AdjustmentDirection.IncreaseOnly).Permits(0m).ShouldBeFalse();
        Reason(AdjustmentDirection.DecreaseOnly).Permits(0m).ShouldBeFalse();
    }

    [Fact]
    public void A_reason_is_in_use_until_somebody_says_otherwise()
    {
        // Withdrawing is a decision. A reason that arrived switched off would be a list nobody
        // could use until they had been through it.
        new AdjustmentReason { Code = "X", Name = "X" }.IsActive.ShouldBeTrue();
    }
}
