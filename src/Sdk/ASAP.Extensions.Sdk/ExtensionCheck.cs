using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Modules;

namespace ASAP.Extensions.Sdk;

/// <summary>
/// Runs over an extension's own declarations the same checks ASAP runs over its modules.
/// </summary>
/// <remarks>
/// <para>
/// ASAP holds its own modules to rules a conformance test enforces: a refusal that blocks
/// somebody must say what to do about it, every message must exist in both languages, a menu
/// entry must ask for a permission that is declared. Those rules are not ASAP's private taste —
/// they are what makes a refusal answerable and a menu honest — and an extension that ignores
/// them is an extension whose users are worse served than the rest of the system.
/// </para>
/// <para>
/// So the checks are shipped rather than described. An author calls this from their own test
/// suite and finds out in a second, instead of finding out from a customer that a message they
/// wrote says something is impossible and not what to do instead.
/// </para>
/// </remarks>
public static class ExtensionCheck
{
    /// <summary>
    /// Checks an extension's declarations.
    /// </summary>
    /// <param name="module">The extension.</param>
    /// <returns>Everything wrong with it, in words. Empty means it conforms.</returns>
    public static IReadOnlyList<string> Problems(IAsapModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(module.ModuleId))
        {
            problems.Add("The extension has no module id, so nothing it declares can be told apart from anything else's.");
        }

        CheckMessages(module, problems);
        CheckPermissions(module, problems);
        CheckSettings(module, problems);
        CheckNavigation(module, problems);

        return problems;
    }

    /// <summary>
    /// Throws when an extension does not conform, listing every reason.
    /// </summary>
    /// <param name="module">The extension.</param>
    /// <exception cref="InvalidOperationException">The extension does not conform.</exception>
    /// <remarks>
    /// For an author who wants one line in their test suite. Every problem is listed at once,
    /// because being told about one, fixing it, and being told about the next is how a five-minute
    /// job becomes an afternoon.
    /// </remarks>
    public static void ThrowIfNotConforming(IAsapModule module)
    {
        var problems = Problems(module);

        if (problems.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{problems.Count} problem(s) with this extension's declarations:"
            + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", problems));
    }

    private static void CheckMessages(IAsapModule module, List<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in module.Messages)
        {
            var code = message.Code.Value;

            if (!seen.Add(code))
            {
                problems.Add($"Message {code} is declared twice, so which one is shown is a coin toss.");
            }

            if (!code.StartsWith($"{module.ModuleId.ToUpperInvariant()}.", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"Message {code} does not begin with {module.ModuleId.ToUpperInvariant()}., so it "
                    + "could collide with another extension's.");
            }

            // The rule the whole message catalogue exists for. Telling somebody they may not do
            // something, and not what to do instead, leaves them stuck with a sentence.
            if (message.Severity is MessageSeverity.Blocked
                && string.IsNullOrWhiteSpace(message.Resolution?.English))
            {
                problems.Add($"Message {code} blocks somebody and does not say what to do about it.");
            }

            if (string.IsNullOrWhiteSpace(message.Title.Arabic))
            {
                problems.Add($"Message {code} has no Arabic title, so an Arabic reader gets the English.");
            }

            if (message.Detail is { } detail && string.IsNullOrWhiteSpace(detail.Arabic))
            {
                problems.Add($"Message {code} has no Arabic detail.");
            }

            foreach (var name in Placeholders(message.Detail?.English)
                         .Except(Placeholders(message.Detail?.Arabic)))
            {
                // A placeholder dropped in translation loses the number itself, not just the
                // wording: the Arabic reader is told something happened but not to what.
                problems.Add($"Message {code} uses {{{name}}} in English and not in Arabic.");
            }
        }
    }

    private static void CheckPermissions(IAsapModule module, List<string> problems)
    {
        var declared = new HashSet<string>(
            module.Permissions.Select(static p => p.Key),
            StringComparer.OrdinalIgnoreCase);

        foreach (var permission in module.Permissions)
        {
            if (!permission.Key.StartsWith($"{module.ModuleId}.", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"Permission {permission.Key} does not begin with {module.ModuleId}., so granting "
                    + "it could grant somebody else's.");
            }

            foreach (var implied in permission.Implies)
            {
                // A permission implying one nobody declares is a grant that half works: the
                // holder gets the outer power and not the inner one it was supposed to carry.
                if (!declared.Contains(implied)
                    && implied.StartsWith($"{module.ModuleId}.", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"Permission {permission.Key} implies {implied}, which this extension does not declare.");
                }
            }
        }
    }

    private static void CheckSettings(IAsapModule module, List<string> problems)
    {
        var declared = new HashSet<string>(
            module.Permissions.Select(static p => p.Key),
            StringComparer.OrdinalIgnoreCase);

        foreach (var setting in module.Setups)
        {
            if (!setting.Key.StartsWith($"{module.ModuleId}.", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Setting {setting.Key} does not begin with {module.ModuleId}., so two extensions could share it.");
            }

            if (setting.RequiresPermission is { Length: > 0 } needed
                && needed.StartsWith($"{module.ModuleId}.", StringComparison.OrdinalIgnoreCase)
                && !declared.Contains(needed))
            {
                // A setting guarded by a permission nobody declares is a setting nobody can be
                // granted the right to change, including the administrator.
                problems.Add($"Setting {setting.Key} needs {needed}, which this extension does not declare.");
            }
        }
    }

    private static void CheckNavigation(IAsapModule module, List<string> problems)
    {
        var declared = new HashSet<string>(
            module.Permissions.Select(static p => p.Key),
            StringComparer.OrdinalIgnoreCase);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in module.Navigation)
        {
            if (!ids.Add(item.Id))
            {
                problems.Add($"Menu entry {item.Id} is declared twice, which the client keys its rendering on.");
            }

            if (item.RequiresPermission is { Length: > 0 } needed
                && needed.StartsWith($"{module.ModuleId}.", StringComparison.OrdinalIgnoreCase)
                && !declared.Contains(needed))
            {
                // Invisible to every user including the administrator, which looks exactly like a
                // feature that was never finished.
                problems.Add($"Menu entry {item.Id} needs {needed}, which this extension does not declare.");
            }
        }
    }

    /// <summary>The placeholders a message's text uses.</summary>
    private static HashSet<string> Placeholders(string? text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(text))
        {
            return found;
        }

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] != '{')
            {
                continue;
            }

            var close = text.IndexOf('}', at);

            if (close < 0)
            {
                break;
            }

            var inner = text[(at + 1)..close];

            // A placeholder may carry a format or an alignment: {Amount:N2}, {Name,-10}. The name
            // is what has to survive translation; the formatting is the translator's business.
            var cut = inner.IndexOfAny([':', ',']);

            found.Add(cut >= 0 ? inner[..cut] : inner);
            at = close;
        }

        return found;
    }
}
