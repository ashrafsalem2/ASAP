using System.Globalization;
using ASAP.Platform.Core.Setup;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Setup;
using Shouldly;

namespace ASAP.Platform.Tests.Setup;

public sealed class SetupValueConverterTests
{
    private static SetupDescriptor Descriptor(
        SetupValueType type,
        decimal? minimum = null,
        decimal? maximum = null,
        params SetupOption[] options) => new()
    {
        Key = "Test.Group.Setting",
        Module = "Test",
        Group = new LocalizedText("Group"),
        DisplayName = new LocalizedText("Setting"),
        Description = new LocalizedText("A setting used in tests."),
        ValueType = type,
        Minimum = minimum,
        Maximum = maximum,
        AllowedValues = options,
    };

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("false")]
    public void Accepts_a_boolean(string value)
    {
        SetupValueConverter.Validate(Descriptor(SetupValueType.Boolean), value).ShouldBeNull();
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void Rejects_something_that_is_not_a_boolean(string value)
    {
        SetupValueConverter.Validate(Descriptor(SetupValueType.Boolean), value)
            .ShouldNotBeNull()
            .ShouldContain("yes or no");
    }

    [Fact]
    public void Enforces_a_minimum()
    {
        SetupValueConverter.Validate(Descriptor(SetupValueType.Integer, minimum: 1), "0")
            .ShouldNotBeNull()
            .ShouldContain("at least 1");
    }

    [Fact]
    public void Enforces_a_maximum()
    {
        SetupValueConverter.Validate(Descriptor(SetupValueType.Integer, maximum: 50), "51")
            .ShouldNotBeNull()
            .ShouldContain("at most 50");
    }

    [Fact]
    public void Accepts_a_value_inside_its_range()
    {
        SetupValueConverter.Validate(Descriptor(SetupValueType.Integer, 1, 50), "25").ShouldBeNull();
    }

    [Fact]
    public void Rejects_an_option_that_is_not_on_the_list()
    {
        var descriptor = Descriptor(
            SetupValueType.Option,
            options: [new SetupOption("en", "English"), new SetupOption("ar", "Arabic")]);

        SetupValueConverter.Validate(descriptor, "fr")
            .ShouldNotBeNull()
            .ShouldContain("one of: en, ar");
    }

    [Fact]
    public void Matches_an_option_without_regard_to_case()
    {
        var descriptor = Descriptor(SetupValueType.Option, options: [new SetupOption("en", "English")]);

        SetupValueConverter.Validate(descriptor, "EN").ShouldBeNull();
    }

    [Fact]
    public void Always_accepts_null_because_it_clears_an_override()
    {
        SetupValueConverter.Validate(Descriptor(SetupValueType.Integer, minimum: 10), null).ShouldBeNull();
    }

    [Theory]
    [InlineData("{\"a\":1}")]
    [InlineData("[1,2,3]")]
    public void Accepts_something_json_shaped(string value)
    {
        SetupValueConverter.Validate(Descriptor(SetupValueType.Json), value).ShouldBeNull();
    }

    [Fact]
    public void Rejects_something_that_is_not_json_shaped()
    {
        SetupValueConverter.Validate(Descriptor(SetupValueType.Json), "not json")
            .ShouldNotBeNull()
            .ShouldContain("valid JSON");
    }

    [Fact]
    public void Parses_a_decimal_the_same_way_whatever_culture_is_running()
    {
        // The defect this guards: an accountant working in a culture where the comma is the
        // decimal separator saves "0.5", a background job reads it back under another culture,
        // and a tolerance of half a unit silently becomes five. It surfaces months later as an
        // unexplained rounding difference nobody can trace.
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            SetupValueConverter.Parse<decimal>("0.5").ShouldBe(0.5m);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            SetupValueConverter.Parse<decimal>("0.5").ShouldBe(0.5m);

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            SetupValueConverter.Parse<decimal>("0.5").ShouldBe(0.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Validates_a_decimal_the_same_way_whatever_culture_is_running()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            SetupValueConverter.Validate(Descriptor(SetupValueType.Decimal, maximum: 1), "0.5")
                .ShouldBeNull();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Parses_a_boolean(string stored, bool expected)
    {
        SetupValueConverter.Parse<bool>(stored).ShouldBe(expected);
    }

    [Fact]
    public void Parses_an_integer()
    {
        SetupValueConverter.Parse<int>("42").ShouldBe(42);
    }

    [Fact]
    public void Parses_a_date()
    {
        SetupValueConverter.Parse<DateOnly>("2026-08-26").ShouldBe(new DateOnly(2026, 8, 26));
    }

    [Fact]
    public void Parses_an_enum_without_regard_to_case()
    {
        SetupValueConverter.Parse<SetupScope>("company").ShouldBe(SetupScope.Company);
    }

    [Fact]
    public void Returns_the_default_when_nothing_is_stored()
    {
        SetupValueConverter.Parse<int>(null).ShouldBe(0);
        SetupValueConverter.Parse<string>(null).ShouldBeNull();
    }

    [Fact]
    public void Reports_a_stored_value_that_cannot_be_read_as_a_defect()
    {
        // Reaching this means something wrote a value without validating it, which is a bug in
        // the writer rather than a mistake by a user.
        Should.Throw<InvalidCastException>(() => SetupValueConverter.Parse<int>("not a number"))
              .Message.ShouldContain("without passing validation");
    }

    [Fact]
    public void Describes_what_a_setting_expects_including_its_range()
    {
        SetupValueConverter
            .Describe(Descriptor(SetupValueType.Integer, 0, 50))
            .ShouldBe("a whole number between 0 and 50");
    }

    [Fact]
    public void Describes_an_option_setting_by_listing_its_choices()
    {
        var descriptor = Descriptor(
            SetupValueType.Option,
            options: [new SetupOption("en", "English"), new SetupOption("ar", "Arabic")]);

        SetupValueConverter.Describe(descriptor).ShouldBe("one of: en, ar");
    }
}
