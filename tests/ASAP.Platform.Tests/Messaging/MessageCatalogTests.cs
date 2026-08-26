using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Security;
using NSubstitute;
using Shouldly;

namespace ASAP.Platform.Tests.Messaging;

public sealed class MessageCatalogTests
{
    private static MessageDefinition OutOfBalance() => new()
    {
        Code = "FIN.JOURNAL.OUT_OF_BALANCE",
        Severity = MessageSeverity.Error,
        Title = new LocalizedText("Journal is out of balance", "دفتر اليومية غير متوازن"),
        Detail = new LocalizedText(
            "Debits total {Debit:N2} and credits total {Credit:N2}, a difference of {Difference:N2} {Currency}.",
            "إجمالي المدين {Debit:N2} وإجمالي الدائن {Credit:N2}، بفارق {Difference:N2} {Currency}."),
        Resolution = new LocalizedText("Add a line for the difference, or use Suggest Balancing Line."),
    };

    private static MessageDefinition BelowCost() => new()
    {
        Code = "PROMO.OFFER.BELOW_COST",
        Severity = MessageSeverity.Blocked,
        Title = "Offer would sell below cost",
        Detail = "Item {ItemNo} costs {Cost:N2} but the offer prices it at {Price:N2}.",
        Resolution = "Raise the offer price above {Cost:N2}, or exclude this item from the offer.",
        OverridePermission = "Promotions.Offer.Override",
    };

    [Fact]
    public void Renders_the_declared_text_with_the_real_values_substituted()
    {
        var catalog = new MessageCatalog([OutOfBalance()]);

        var message = catalog.Render(
            "FIN.JOURNAL.OUT_OF_BALANCE",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Debit"] = 5150m,
                ["Credit"] = 5000m,
                ["Difference"] = 150m,
                ["Currency"] = "SAR",
            });

        message.Title.ShouldBe("Journal is out of balance");
        message.Detail!.ShouldContain("150.00 SAR");
        message.Severity.ShouldBe(MessageSeverity.Error);
        message.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Carries_the_raw_values_alongside_the_rendered_text()
    {
        // The client re-formats these for its own locale, and integrations read the figures
        // without having to parse the prose back apart.
        var catalog = new MessageCatalog([OutOfBalance()]);
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Difference"] = 150m,
        };

        var message = catalog.Render("FIN.JOURNAL.OUT_OF_BALANCE", arguments);

        message.Arguments["Difference"].ShouldBe(150m);
    }

    [Fact]
    public void Renders_in_arabic_for_an_arabic_user()
    {
        var user = Substitute.For<IUserContext>();
        user.Culture.Returns("ar-SA");

        var catalog = new MessageCatalog([OutOfBalance()], user);

        var message = catalog.Render("FIN.JOURNAL.OUT_OF_BALANCE");

        message.Title.ShouldBe("دفتر اليومية غير متوازن");
    }

    [Fact]
    public void Falls_back_to_english_when_a_translation_is_missing()
    {
        // A partly translated deployment must still read sensibly rather than showing blanks.
        var user = Substitute.For<IUserContext>();
        user.Culture.Returns("ar-SA");

        var catalog = new MessageCatalog([BelowCost()], user);

        var message = catalog.Render("PROMO.OFFER.BELOW_COST");

        message.Title.ShouldBe("Offer would sell below cost");
    }

    [Fact]
    public void Reports_the_override_permission_on_a_blocked_message()
    {
        var catalog = new MessageCatalog([BelowCost()]);

        var message = catalog.Render(
            "PROMO.OFFER.BELOW_COST",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ItemNo"] = "ITEM-100",
                ["Cost"] = 40m,
                ["Price"] = 35m,
            });

        message.Severity.ShouldBe(MessageSeverity.Blocked);
        message.IsOverridable.ShouldBeTrue();
        message.OverridePermission.ShouldBe("Promotions.Offer.Override");
        message.Resolution!.ShouldContain("40.00");
    }

    [Fact]
    public void Refuses_to_start_when_a_blocking_message_offers_no_way_forward()
    {
        // The central rule of the ASAP messaging system: refusing a user without telling them
        // how to proceed is a defect, and it is caught at startup rather than in production.
        var deadEnd = new MessageDefinition
        {
            Code = "INV.STOCK.NEGATIVE_BLOCKED",
            Severity = MessageSeverity.Blocked,
            Title = "Not allowed",
        };

        var act = () => new MessageCatalog([deadEnd]);

        act.ShouldThrow<InvalidOperationException>()
           .Message.ShouldContain("offers no resolution");
    }

    [Fact]
    public void Refuses_to_start_when_two_modules_declare_the_same_code()
    {
        var act = () => new MessageCatalog([OutOfBalance(), OutOfBalance()]);

        act.ShouldThrow<InvalidOperationException>()
           .Message.ShouldContain("declared more than once");
    }

    [Fact]
    public void Throws_a_helpful_error_for_an_unregistered_code()
    {
        var catalog = new MessageCatalog([OutOfBalance()]);

        var act = () => catalog.Render("FIN.JOURNAL.NEVER_DECLARED");

        act.ShouldThrow<KeyNotFoundException>()
           .Message.ShouldContain("Declare it on the Messages collection");
    }

    [Theory]
    [InlineData("FIN.JOURNAL.OUT_OF_BALANCE")]
    [InlineData("fin.journal.out_of_balance")] // normalised up, not rejected
    [InlineData("PROMO.OFFER.BELOW_COST")]
    public void Accepts_a_well_shaped_code(string value)
    {
        new MessageCode(value).Value.ShouldBe(value.ToUpperInvariant());
    }

    [Theory]
    [InlineData("FIN.JOURNAL")]      // only two segments
    [InlineData("FIN")]              // only one
    [InlineData("FIN..OUT")]         // empty segment
    [InlineData("FIN.JOUR NAL.OUT")] // space
    [InlineData("FIN-JOURNAL-OUT")]  // dashes rather than dots
    [InlineData("  ")]
    public void Rejects_a_badly_shaped_code(string value)
    {
        Should.Throw<ArgumentException>(() => new MessageCode(value));
    }

    [Fact]
    public void Reads_the_owning_module_off_a_code()
    {
        new MessageCode("FIN.JOURNAL.OUT_OF_BALANCE").Module.ShouldBe("FIN");
    }
}
