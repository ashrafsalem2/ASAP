using System.Reflection;
using ASAP.Api.Infrastructure;
using ASAP.Platform.Kernel.Messaging;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// Holds the wire shape of a message to the message itself.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MessagePayload"/> exists so that every endpoint projects a message the same way,
/// and its own documentation names the failure mode it cannot prevent on its own: adding a field
/// to <see cref="AsapMessage"/> means remembering the projection, and nothing complains when
/// somebody does not. The field simply never arrives and the screen goes on rendering something
/// subtly incomplete.
/// </para>
/// <para>
/// That is exactly what happened to <c>HelpTopic</c>. It sat on the message for months, every
/// refusal carried one, and the client had no way to know: the projection had never been told.
/// This test is the complaint the comment wished for.
/// </para>
/// </remarks>
public sealed class MessageProjectionTests
{
    /// <summary>
    /// Properties that are deliberately not on the wire, with the reason.
    /// </summary>
    /// <remarks>
    /// A list rather than an attribute, so leaving something out is a decision somebody wrote
    /// down here rather than one they made by forgetting.
    /// </remarks>
    private static readonly Dictionary<string, string> NotSent = new(StringComparer.Ordinal)
    {
        ["IsFailure"] = "derived from Severity, which is sent; the client decides for itself",
        ["IsOverridable"] = "derived from OverridePermission, which is sent",
    };

    [Fact]
    public void Every_field_of_a_message_reaches_the_client()
    {
        var sent = typeof(MessagePayload)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = typeof(AsapMessage)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static p => p.Name)
            .Where(name => !sent.Contains(name) && !NotSent.ContainsKey(name))
            .ToList();

        missing.ShouldBeEmpty(
            "a field on AsapMessage that MessagePayload does not carry never arrives, and nothing "
            + "anywhere says so:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Nothing_is_left_out_by_accident()
    {
        // The exemptions have to still be real properties. One that was renamed leaves an
        // exemption covering nothing, and the next field with that name is silently excused.
        var properties = typeof(AsapMessage)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var stale = NotSent.Keys.Where(name => !properties.Contains(name)).ToList();

        stale.ShouldBeEmpty(
            "an exemption for a property that no longer exists excuses the next one to take its "
            + "name:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void The_rule_is_capable_of_failing()
    {
        // Worth exactly what its ability to fail is worth.
        typeof(AsapMessage).GetProperty("Code").ShouldNotBeNull();
        typeof(MessagePayload).GetProperty("NoSuchThing").ShouldBeNull();
    }
}
