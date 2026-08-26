using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Platform.Kernel.Setup;

/// <summary>
/// One configurable setting a module offers, declared at startup.
/// </summary>
/// <remarks>
/// <para>
/// Declaring settings rather than reading loose configuration keys is what makes ASAP setup
/// discoverable. The setup screen is generated from these descriptors, so a setting cannot
/// exist in code without appearing in the UI with a name, an explanation, a type, a default,
/// and a record of who last changed it. There is no hidden configuration.
/// </para>
/// <para>
/// A third-party extension declares its settings the same way and they appear on the same
/// screen, grouped under the extension.
/// </para>
/// </remarks>
public sealed record SetupDescriptor
{
    /// <summary>
    /// The setting key, shaped <c>Module.Group.Name</c>, for example
    /// <c>Inventory.Costing.AllowNegativeStock</c>. Stable across versions.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Module that owns the setting.</summary>
    public required string Module { get; init; }

    /// <summary>Group heading the setting appears under on the setup screen.</summary>
    public required LocalizedText Group { get; init; }

    /// <summary>Label shown next to the input.</summary>
    public required LocalizedText DisplayName { get; init; }

    /// <summary>What the setting actually does, and what changing it will affect.</summary>
    public required LocalizedText Description { get; init; }

    /// <summary>What kind of value the setting holds.</summary>
    public required SetupValueType ValueType { get; init; }

    /// <summary>The widest scope this setting may be set at.</summary>
    public SetupScope Scope { get; init; } = SetupScope.Company;

    /// <summary>
    /// Whether a narrower scope may override the value. Costing method is company-wide and must
    /// not vary by branch; a discount ceiling is exactly the sort of thing that should.
    /// </summary>
    public bool AllowsNarrowerOverride { get; init; } = true;

    /// <summary>
    /// The value used when nobody has set one, in its string form. Every setting has a working
    /// default, so a fresh company runs sensibly before anyone visits the setup screen.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// The permitted values for an <see cref="SetupValueType.Option"/> setting, each with the
    /// label the dropdown shows.
    /// </summary>
    public IReadOnlyList<SetupOption> AllowedValues { get; init; } = [];

    /// <summary>
    /// For an <see cref="SetupValueType.EntityReference"/>, the logical type being pointed at,
    /// for example <c>Finance.GlAccount</c>. Tells the screen which lookup to show.
    /// </summary>
    public string? ReferencedEntityType { get; init; }

    /// <summary>Smallest permitted value, for numeric settings.</summary>
    public decimal? Minimum { get; init; }

    /// <summary>Largest permitted value, for numeric settings.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>
    /// Permission required to change the setting. Reading setup is broadly available; changing
    /// something like the costing method is not. Null means the general setup permission is enough.
    /// </summary>
    public string? RequiresPermission { get; init; }

    /// <summary>
    /// True when changing the value invalidates data already posted, as changing a costing
    /// method would. The screen warns before saving, and the change is refused outright once
    /// there are posted entries.
    /// </summary>
    public bool IsLockedAfterFirstPosting { get; init; }

    /// <summary>Anchor into the user documentation explaining the setting in full.</summary>
    public string? HelpTopic { get; init; }

    /// <inheritdoc />
    public override string ToString() => Key;
}

/// <summary>One choice on an <see cref="SetupValueType.Option"/> setting.</summary>
/// <param name="Value">The stored value, stable across versions and translations.</param>
/// <param name="Label">What the dropdown shows.</param>
/// <param name="Description">What choosing this option means, shown as help under the dropdown.</param>
public readonly record struct SetupOption(string Value, LocalizedText Label, LocalizedText? Description = null);
