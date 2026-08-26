namespace ASAP.Platform.Kernel.Setup;

/// <summary>
/// What kind of value a setup holds. Drives how the setup screen renders the input, how the
/// value is validated on save, and how it is parsed on read.
/// </summary>
public enum SetupValueType
{
    /// <summary>A yes or no switch.</summary>
    Boolean = 0,

    /// <summary>A whole number, such as a number of days.</summary>
    Integer = 1,

    /// <summary>A decimal, such as a percentage or a tolerance amount.</summary>
    Decimal = 2,

    /// <summary>Free text.</summary>
    Text = 3,

    /// <summary>A calendar date, with no time part.</summary>
    Date = 4,

    /// <summary>
    /// One of a fixed list, declared in <see cref="SetupDescriptor.AllowedValues"/>. Rendered
    /// as a dropdown, and rejected on save if the value is not on the list.
    /// </summary>
    Option = 5,

    /// <summary>
    /// A pointer to another record, such as a G/L account or a location. The descriptor names
    /// the target type so the screen can show a proper lookup rather than asking for a GUID.
    /// </summary>
    EntityReference = 6,

    /// <summary>
    /// Structured JSON, for settings genuinely too rich for a single field, such as a printer
    /// station layout. Used sparingly: a JSON blob is a setting the setup screen cannot help with.
    /// </summary>
    Json = 7,

    /// <summary>
    /// Text stored encrypted at rest and never returned to the client, such as a gateway API
    /// key. The screen shows whether a value is present and lets it be replaced, never read back.
    /// </summary>
    Secret = 8,
}
