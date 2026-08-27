using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Modules;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// Holds the help topics to the promises the messages make about them.
/// </summary>
/// <remarks>
/// <para>
/// Nearly every refusal in ASAP carries a help topic, and a help topic that leads nowhere is
/// worse than none at all: somebody follows it at the moment they are already stuck. The topics
/// are written in both languages, checked here, and the check runs in both directions — a topic
/// nothing points at is a page nobody will ever be sent to, and is usually a renamed message
/// that left its documentation behind.
/// </para>
/// <para>
/// This is the same bargain as the message catalogue itself. Documentation that is optional
/// decays; documentation a build refuses to go without does not.
/// </para>
/// </remarks>
public sealed class HelpTopicTests
{
    /// <summary>Every module ASAP ships.</summary>
    private static readonly IAsapModule[] Modules =
    [
        new PlatformModule(),
        new ASAP.Modules.Finance.FinanceModule(),
        new ASAP.Modules.Inventory.InventoryModule(),
        new ASAP.Modules.Purchasing.PurchasingModule(),
        new ASAP.Modules.Promotions.PromotionsModule(),
        new ASAP.Modules.Hr.HrModule(),
        new ASAP.Modules.Sales.SalesModule(),
        new ASAP.Modules.Pos.PosModule(),
    ];

    [Fact]
    public void Every_topic_a_message_points_at_is_written_in_both_languages()
    {
        var missing = new List<string>();

        foreach (var topic in Referenced())
        {
            foreach (var language in new[] { "en", "ar" })
            {
                var path = PathFor(topic, language);

                if (!File.Exists(path))
                {
                    missing.Add($"{topic} ({language})");
                    continue;
                }

                if (new FileInfo(path).Length < 200)
                {
                    // A file with a heading and nothing under it passes an existence check and
                    // fails the person reading it.
                    missing.Add($"{topic} ({language}) is too short to be an explanation");
                }
            }
        }

        missing.ShouldBeEmpty(
            "a help topic that leads nowhere is followed at the moment somebody is already stuck:\n"
            + string.Join("\n", missing));
    }

    [Fact]
    public void Every_topic_written_is_one_something_points_at()
    {
        var referenced = Referenced().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = HelpRoot();

        if (!Directory.Exists(root))
        {
            return;
        }

        var orphans = Directory
            .EnumerateFiles(root, "*.en.md", SearchOption.AllDirectories)
            .Select(path => TopicOf(root, path))
            .Where(topic => !referenced.Contains(topic))
            .ToList();

        orphans.ShouldBeEmpty(
            "a topic nothing points at is a page nobody is ever sent to, and is usually a renamed "
            + "message that left its documentation behind:\n" + string.Join("\n", orphans));
    }

    [Fact]
    public void The_rule_is_capable_of_failing()
    {
        // The check is worth exactly what its ability to fail is worth.
        File.Exists(PathFor("nothing/at/all", "en")).ShouldBeFalse();
        Referenced().ShouldNotBeEmpty();
    }

    /// <summary>Every help topic any module points at, from a message or a setting.</summary>
    private static IEnumerable<string> Referenced()
        => ASAP.Platform.Core.Messaging.PlatformMessages.All
            .Select(static d => d.HelpTopic)
            .Concat(Modules.SelectMany(static m => m.Messages
                .Select(static d => d.HelpTopic)
                .Concat(m.Setups.Select(static s => s.HelpTopic))))
            .Where(static t => !string.IsNullOrWhiteSpace(t))
            .Select(static t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static t => t, StringComparer.OrdinalIgnoreCase);

    private static string TopicOf(string root, string path)
        => Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(".en.md", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string PathFor(string topic, string language)
        => Path.Combine(HelpRoot(), $"{topic.Replace('/', Path.DirectorySeparatorChar)}.{language}.md");

    private static string HelpRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ASAP.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? string.Empty, "docs", "help");
    }
}
