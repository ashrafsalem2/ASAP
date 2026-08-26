namespace ASAP.Platform.Kernel.Messaging;

/// <summary>
/// Points a message at the exact thing that caused it, so the client can highlight the guilty
/// field and deep-link to the offending record instead of showing a message with no anchor.
/// </summary>
/// <param name="Field">
/// Dotted path of the input at fault, for example <c>Lines[3].DebitAmount</c>. Null when the
/// message is about the document as a whole rather than one field.
/// </param>
/// <param name="EntityType">
/// Logical name of the related record type, for example <c>Finance.JournalBatch</c>.
/// Null when nothing is linkable.
/// </param>
/// <param name="EntityId">Key of the related record.</param>
/// <param name="DisplayNo">
/// The number a human would recognise the record by, for example <c>GJ-2026-00042</c>.
/// Carried separately because a GUID means nothing to the reader.
/// </param>
public readonly record struct MessageTarget(
    string? Field = null,
    string? EntityType = null,
    Guid? EntityId = null,
    string? DisplayNo = null)
{
    /// <summary>Targets one input on the current document.</summary>
    public static MessageTarget OnField(string field) => new(Field: field);

    /// <summary>Targets another record, which the client can offer to open.</summary>
    public static MessageTarget OnRecord(string entityType, Guid entityId, string? displayNo = null)
        => new(EntityType: entityType, EntityId: entityId, DisplayNo: displayNo);

    /// <summary>True when this target points at nothing in particular.</summary>
    public bool IsEmpty => Field is null && EntityType is null;
}
