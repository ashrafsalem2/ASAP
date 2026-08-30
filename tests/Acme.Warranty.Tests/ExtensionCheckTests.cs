using ASAP.Extensions.Sdk;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using Shouldly;

namespace Acme.Warranty.Tests;

/// <summary>
/// Proves the SDK's conformance check catches what it claims to.
/// </summary>
/// <remarks>
/// A check that has only ever been run against something correct proves nothing. Each case below
/// is a mistake an extension author actually makes, made deliberately, so the check is shown
/// failing as well as passing.
/// </remarks>
public sealed class ExtensionCheckTests
{
    [Fact]
    public void A_refusal_that_does_not_say_what_to_do_is_caught()
    {
        // The rule the whole message catalogue exists for. Telling somebody they may not do
        // something, and not what to do instead, leaves them stuck with a sentence.
        var problems = ExtensionCheck.Problems(new Broken
        {
            Message = new MessageDefinition
            {
                Code = new MessageCode("BROKEN.THING.STOPPED"),
                Severity = MessageSeverity.Blocked,
                Title = new LocalizedText("Stopped", "توقف"),
                Detail = new LocalizedText("It stopped.", "توقف."),
            },
        });

        problems.ShouldContain(p => p.Contains("does not say what to do"));
    }

    [Fact]
    public void A_message_with_no_arabic_is_caught()
    {
        var problems = ExtensionCheck.Problems(new Broken
        {
            Message = new MessageDefinition
            {
                Code = new MessageCode("BROKEN.THING.HALF"),
                Severity = MessageSeverity.Warning,
                Title = new LocalizedText("Half a message", string.Empty),
            },
        });

        problems.ShouldContain(p => p.Contains("no Arabic title"));
    }

    [Fact]
    public void A_placeholder_dropped_in_translation_is_caught()
    {
        // The one that costs a reader the number itself, not just the wording: they are told
        // something happened but not to what.
        var problems = ExtensionCheck.Problems(new Broken
        {
            Message = new MessageDefinition
            {
                Code = new MessageCode("BROKEN.THING.LOST"),
                Severity = MessageSeverity.Warning,
                Title = new LocalizedText("Lost", "ضاع"),
                Detail = new LocalizedText("{DocumentNo} went missing.", "ضاع المستند."),
            },
        });

        problems.ShouldContain(p => p.Contains("{DocumentNo}"));
    }

    [Fact]
    public void A_code_belonging_to_somebody_else_is_caught()
    {
        var problems = ExtensionCheck.Problems(new Broken
        {
            Message = new MessageDefinition
            {
                Code = new MessageCode("FIN.JOURNAL.OUT_OF_BALANCE"),
                Severity = MessageSeverity.Warning,
                Title = new LocalizedText("Not mine", "ليس لي"),
            },
        });

        problems.ShouldContain(p => p.Contains("could collide"));
    }

    [Fact]
    public void A_menu_entry_needing_a_permission_nothing_declares_is_caught()
    {
        // Invisible to every user including the administrator, which looks exactly like a feature
        // that was never finished and is usually one that was finished and misspelt.
        var problems = ExtensionCheck.Problems(new Broken
        {
            Entry = new NavigationItem
            {
                Id = "Broken.Screen",
                Module = "Broken",
                DisplayName = new LocalizedText("A screen", "شاشة"),
                Route = "/broken/screen",
                RequiresPermission = "Broken.Thing.Read",
            },
        });

        problems.ShouldContain(p => p.Contains("does not declare"));
    }

    [Fact]
    public void A_setting_guarded_by_a_permission_nothing_declares_is_caught()
    {
        var problems = ExtensionCheck.Problems(new Broken
        {
            Declared = new SetupDescriptor
            {
                Key = "Broken.Months",
                Module = "Broken",
                Group = new LocalizedText("Group", "مجموعة"),
                DisplayName = new LocalizedText("Months", "أشهر"),
                Description = new LocalizedText("How many.", "كم."),
                ValueType = SetupValueType.Integer,
                RequiresPermission = "Broken.Thing.Update",
            },
        });

        problems.ShouldContain(p => p.Contains("does not declare"));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        // Being told about one, fixing it, and being told about the next is how a five-minute job
        // becomes an afternoon.
        var problems = ExtensionCheck.Problems(new Broken
        {
            Message = new MessageDefinition
            {
                Code = new MessageCode("FIN.THING.STOPPED"),
                Severity = MessageSeverity.Blocked,
                Title = new LocalizedText("Stopped", string.Empty),
            },
            Entry = new NavigationItem
            {
                Id = "Broken.Screen",
                Module = "Broken",
                DisplayName = new LocalizedText("A screen", "شاشة"),
                Route = "/broken/screen",
                RequiresPermission = "Broken.Thing.Read",
            },
        });

        problems.Count.ShouldBeGreaterThanOrEqualTo(4);
    }

    /// <summary>An extension made wrong on purpose, one mistake at a time.</summary>
    private sealed class Broken : AsapExtension
    {
        public MessageDefinition? Message { get; init; }

        public NavigationItem? Entry { get; init; }

        public SetupDescriptor? Declared { get; init; }

        public override string ModuleId => "Broken";

        public override LocalizedText DisplayName => new("Broken", "معطوب");

        public override string Publisher => "Nobody";

        public override IReadOnlyCollection<MessageDefinition> Messages
            => Message is null ? [] : [Message];

        public override IReadOnlyCollection<NavigationItem> Navigation
            => Entry is null ? [] : [Entry];

        public override IReadOnlyCollection<SetupDescriptor> Setups
            => Declared is null ? [] : [Declared];
    }
}
