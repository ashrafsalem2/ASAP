using System.Globalization;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Security;

namespace ASAP.Platform.Core.Messaging;

/// <summary>
/// The running message catalogue, assembled at startup from what every loaded module declares.
/// </summary>
/// <remarks>
/// Built once and then read-only, so it is safe to share as a singleton across requests. The
/// user language is read per call from <see cref="IUserContext"/> rather than captured at
/// construction, which is what lets one instance serve an English accountant and an Arabic
/// cashier at the same time.
/// </remarks>
public sealed class MessageCatalog : IMessageCatalog
{
    private readonly IReadOnlyDictionary<string, MessageDefinition> _definitions;
    private readonly IUserContext? _userContext;

    /// <summary>
    /// Builds the catalogue from a set of declarations.
    /// </summary>
    /// <param name="definitions">Every message declared by every loaded module.</param>
    /// <param name="userContext">
    /// Supplies the language to render in. Null renders in English, which is what background
    /// jobs and the seeder want.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Two modules declared the same code, or a declaration breaks the rules ASAP sets for its
    /// own messages. Both are refused at startup rather than discovered by a user later.
    /// </exception>
    public MessageCatalog(IEnumerable<MessageDefinition> definitions, IUserContext? userContext = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _userContext = userContext;

        var map = new Dictionary<string, MessageDefinition>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        foreach (var definition in definitions)
        {
            if (definition.Validate() is { } problem)
            {
                problems.Add(problem);
                continue;
            }

            if (!map.TryAdd(definition.Code.Value, definition))
            {
                problems.Add(
                    $"Message code {definition.Code} is declared more than once. "
                    + "Codes are part of the ASAP public contract and must be unique across all modules.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The ASAP message catalogue is invalid and the host will not start:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(static p => "  - " + p)));
        }

        _definitions = map;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<MessageDefinition> All => (IReadOnlyCollection<MessageDefinition>)_definitions.Values;

    /// <inheritdoc />
    public MessageDefinition? Find(MessageCode code)
        => _definitions.GetValueOrDefault(code.Value);

    /// <inheritdoc />
    public AsapMessage Render(
        MessageCode code,
        IReadOnlyDictionary<string, object?>? arguments = null,
        MessageTarget target = default)
    {
        if (!_definitions.TryGetValue(code.Value, out var definition))
        {
            throw new KeyNotFoundException(
                $"Message code {code} is not registered. Declare it on the Messages collection of "
                + "the module that raises it, so it can be translated and documented.");
        }

        var cultureName = _userContext?.Culture;
        var culture = ResolveCulture(cultureName);

        return new AsapMessage
        {
            Code = definition.Code,
            Severity = definition.Severity,
            Title = MessageTemplateRenderer.Render(definition.Title.For(cultureName), arguments, culture),
            Detail = definition.Detail is { } detail
                ? MessageTemplateRenderer.Render(detail.For(cultureName), arguments, culture)
                : null,
            Resolution = definition.Resolution is { } resolution
                ? MessageTemplateRenderer.Render(resolution.For(cultureName), arguments, culture)
                : null,
            Target = target,
            Arguments = arguments ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            OverridePermission = definition.OverridePermission,
            HelpTopic = definition.HelpTopic,
        };
    }

    private static CultureInfo ResolveCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return CultureInfo.CurrentCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            // A stored preference can outlive the culture that produced it, or arrive from a
            // client sending something odd. Formatting numbers in the server culture is a far
            // better outcome than failing to render the message at all.
            return CultureInfo.CurrentCulture;
        }
    }
}
