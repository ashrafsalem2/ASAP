using ASAP.Platform.Core.Dimensions;
using Shouldly;

namespace ASAP.Platform.Tests.Dimensions;

public sealed class DimensionCombinationTests
{
    private static readonly Guid Department = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Project = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CostCentre = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid Sales = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Admin = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RiyadhTower = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void Two_combinations_built_in_different_orders_are_the_same()
    {
        // The whole point of the canonical ordering: Sales and Finance can build the same
        // combination in whatever order suits their code and still hit one stored set.
        var first = DimensionCombination.From(
        [
            new DimensionPair(Department, Sales),
            new DimensionPair(Project, RiyadhTower),
        ]);

        var second = DimensionCombination.From(
        [
            new DimensionPair(Project, RiyadhTower),
            new DimensionPair(Department, Sales),
        ]);

        first.ShouldBe(second);
        first.ComputeFingerprint().ShouldBe(second.ComputeFingerprint());
        first.ToCanonicalString().ShouldBe(second.ToCanonicalString());
    }

    [Fact]
    public void Different_combinations_have_different_fingerprints()
    {
        var sales = DimensionCombination.From([new DimensionPair(Department, Sales)]);
        var admin = DimensionCombination.From([new DimensionPair(Department, Admin)]);

        sales.ComputeFingerprint().ShouldNotBe(admin.ComputeFingerprint());
    }

    [Fact]
    public void Keeps_the_last_value_when_a_dimension_is_given_twice()
    {
        // Lets a caller concatenate document defaults and line overrides without filtering first.
        var combination = DimensionCombination.From(
        [
            new DimensionPair(Department, Sales),
            new DimensionPair(Department, Admin),
        ]);

        combination.Count.ShouldBe(1);
        combination.ValueOf(Department).ShouldBe(Admin);
    }

    [Fact]
    public void Layers_an_override_over_a_default()
    {
        // How dimensions actually flow: the customer supplies defaults, the line overrides one.
        var customerDefaults = DimensionCombination.From(
        [
            new DimensionPair(Department, Sales),
            new DimensionPair(CostCentre, Admin),
        ]);

        var lineOverride = DimensionCombination.From([new DimensionPair(Department, Admin)]);

        var effective = customerDefaults.OverrideWith(lineOverride);

        effective.ValueOf(Department).ShouldBe(Admin);
        effective.ValueOf(CostCentre).ShouldBe(Admin);
        effective.Count.ShouldBe(2);
    }

    [Fact]
    public void Layering_an_empty_override_changes_nothing()
    {
        var defaults = DimensionCombination.From([new DimensionPair(Department, Sales)]);

        defaults.OverrideWith(DimensionCombination.Empty).ShouldBe(defaults);
    }

    [Fact]
    public void An_override_can_add_a_dimension_the_default_did_not_set()
    {
        var defaults = DimensionCombination.From([new DimensionPair(Department, Sales)]);
        var addition = DimensionCombination.From([new DimensionPair(Project, RiyadhTower)]);

        var effective = defaults.OverrideWith(addition);

        effective.Count.ShouldBe(2);
        effective.ValueOf(Project).ShouldBe(RiyadhTower);
    }

    [Fact]
    public void An_empty_combination_is_empty()
    {
        DimensionCombination.Empty.IsEmpty.ShouldBeTrue();
        DimensionCombination.Empty.Count.ShouldBe(0);
        DimensionCombination.Empty.ToCanonicalString().ShouldBe(string.Empty);
        DimensionCombination.From([]).ShouldBe(DimensionCombination.Empty);
    }

    [Fact]
    public void A_default_constructed_combination_behaves_as_empty()
    {
        // Guards the struct default: an uninitialised ImmutableArray throws on enumeration,
        // and a value type can always be default-constructed whatever the API intends.
        var uninitialised = default(DimensionCombination);

        uninitialised.IsEmpty.ShouldBeTrue();
        uninitialised.Pairs.ShouldBeEmpty();
        uninitialised.ToCanonicalString().ShouldBe(string.Empty);
        Should.NotThrow(() => uninitialised.ComputeFingerprint());
    }

    [Fact]
    public void Reports_null_for_a_dimension_that_is_not_set()
    {
        var combination = DimensionCombination.From([new DimensionPair(Department, Sales)]);

        combination.ValueOf(Project).ShouldBeNull();
    }

    [Fact]
    public void Produces_a_fingerprint_of_the_expected_width()
    {
        var combination = DimensionCombination.From([new DimensionPair(Department, Sales)]);

        combination.ComputeFingerprint().Length.ShouldBe(32);
    }

    [Fact]
    public void Round_trips_through_a_stored_set()
    {
        var original = DimensionCombination.From(
        [
            new DimensionPair(Department, Sales),
            new DimensionPair(Project, RiyadhTower),
        ]);

        var stored = new DimensionSet
        {
            Fingerprint = original.ComputeFingerprint(),
            Signature = original.ToCanonicalString(),
            Entries =
            [
                new DimensionSetEntry { DimensionId = Department, DimensionValueId = Sales },
                new DimensionSetEntry { DimensionId = Project, DimensionValueId = RiyadhTower },
            ],
        };

        stored.ToCombination().ShouldBe(original);
    }
}
