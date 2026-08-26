using ASAP.Platform.Kernel.Entities;

namespace ASAP.Platform.Core.Dimensions;

/// <summary>
/// An axis a transaction can be analysed along: department, project, cost centre, salesperson.
/// </summary>
/// <remarks>
/// <para>
/// Dimensions live in the platform rather than in Finance because every module posts through
/// them. A sales invoice, a stock transfer, a payroll run and a purchase receipt all carry the
/// same dimension set, which is what makes "profit by department" a single query rather than a
/// reconciliation exercise across four modules.
/// </para>
/// <para>
/// This is Business Central idea, and it earns its place: adding a new axis of analysis is a
/// configuration change an accountant makes on a Tuesday, not a schema change and a release.
/// </para>
/// </remarks>
public sealed class Dimension : CompanyEntity
{
    /// <summary>Short stable code, for example <c>DEPARTMENT</c>.</summary>
    public required string Code { get; set; }

    /// <summary>Name shown on screen, for example "Department".</summary>
    public required string Name { get; set; }

    /// <summary>Name in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>What this axis is for, shown when setting up posting rules.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Position among the shortcut dimensions, 1 to 8, or null for an ordinary one.
    /// </summary>
    /// <remarks>
    /// A shortcut dimension is copied directly onto every ledger entry as well as into the
    /// dimension set. That denormalisation is deliberate: filtering a million general ledger
    /// entries by department should be an index seek on the entry table, not a join through the
    /// set entries. Two axes usually earn it; eight is the ceiling, and using all eight defeats
    /// the point.
    /// </remarks>
    public int? ShortcutIndex { get; set; }

    /// <summary>
    /// Whether every transaction must carry a value for this dimension. Enforced at posting,
    /// with a message naming the dimension rather than a generic validation failure.
    /// </summary>
    public bool IsMandatory { get; set; }

    /// <summary>
    /// Whether the dimension may still be used. Blocking retires an axis without disturbing the
    /// history posted against it, which stays readable and reportable.
    /// </summary>
    public bool IsBlocked { get; set; }

    /// <summary>The values this dimension may take.</summary>
    public ICollection<DimensionValue> Values { get; set; } = [];
}

/// <summary>
/// What a dimension value is for. Most are ordinary values; the rest exist so a report can show
/// subtotals without anyone hand-building the arithmetic.
/// </summary>
public enum DimensionValueKind
{
    /// <summary>An ordinary value that transactions post to.</summary>
    Standard = 0,

    /// <summary>A caption in the value list. Nothing posts to it.</summary>
    Heading = 1,

    /// <summary>A subtotal over a range of values. Nothing posts to it.</summary>
    Total = 2,

    /// <summary>Marks where a total range opens.</summary>
    BeginTotal = 3,

    /// <summary>Marks where a total range closes.</summary>
    EndTotal = 4,
}

/// <summary>One permitted value of a dimension, such as the Sales department.</summary>
public sealed class DimensionValue : CompanyEntity
{
    /// <summary>The dimension this value belongs to.</summary>
    public Guid DimensionId { get; set; }

    /// <summary>Navigation to the dimension.</summary>
    public Dimension? Dimension { get; set; }

    /// <summary>Short stable code, for example <c>SALES</c>.</summary>
    public required string Code { get; set; }

    /// <summary>Name shown on screen.</summary>
    public required string Name { get; set; }

    /// <summary>Name in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>What this value is for.</summary>
    public DimensionValueKind Kind { get; set; } = DimensionValueKind.Standard;

    /// <summary>
    /// For a <see cref="DimensionValueKind.Total"/>, the range it sums, for example
    /// <c>1000..1999</c>.
    /// </summary>
    public string? TotalRange { get; set; }

    /// <summary>
    /// Indent level in the value list, so a hierarchy of departments reads as one.
    /// </summary>
    public int Indentation { get; set; }

    /// <summary>
    /// Whether the value may still be posted to. Blocking retires a department without touching
    /// what was posted to it while it was live.
    /// </summary>
    public bool IsBlocked { get; set; }

    /// <summary>Whether transactions may post to this value.</summary>
    public bool IsPostable => Kind is DimensionValueKind.Standard && !IsBlocked;
}
