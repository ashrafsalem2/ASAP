using ASAP.Platform.Kernel.Messaging;

namespace ASAP.Platform.Kernel.Modules;

/// <summary>What sort of thing a navigation entry leads to.</summary>
public enum NavigationKind
{
    /// <summary>A heading that groups other entries and leads nowhere itself.</summary>
    Group = 0,

    /// <summary>A list or card screen.</summary>
    Page = 1,

    /// <summary>A report.</summary>
    Report = 2,

    /// <summary>A setup screen.</summary>
    Setup = 3,

    /// <summary>A job or batch routine the user runs on demand.</summary>
    Task = 4,
}

/// <summary>
/// One entry in the ASAP menu, declared by the module that owns it.
/// </summary>
/// <remarks>
/// The menu is assembled from these at runtime and filtered by what the signed-in user may
/// actually see, so nobody is shown a screen that will refuse them on arrival. An extension
/// contributes entries the same way, and can nest them under a core group by naming that group
/// as its parent.
/// </remarks>
public sealed record NavigationItem
{
    /// <summary>
    /// Stable identifier, shaped <c>Module.Area.Item</c>, for example
    /// <c>Finance.Journals.GeneralJournal</c>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Module that owns the entry.</summary>
    public required string Module { get; init; }

    /// <summary>What the menu shows.</summary>
    public required LocalizedText DisplayName { get; init; }

    /// <summary>What sort of thing this leads to.</summary>
    public NavigationKind Kind { get; init; } = NavigationKind.Page;

    /// <summary>
    /// Identifier of the entry this one nests under, or null for a top-level entry. Naming a
    /// core group here is how an extension gets its screens to appear beside the built-in ones.
    /// </summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// Angular route the entry navigates to, for example <c>/finance/journals</c>. Null for a
    /// <see cref="NavigationKind.Group"/>.
    /// </summary>
    public string? Route { get; init; }

    /// <summary>Icon name from the ASAP icon set.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Permission the user must hold for the entry to appear. Null makes it visible to anyone
    /// who has the module at all.
    /// </summary>
    public string? RequiresPermission { get; init; }

    /// <summary>Sort order within the parent, lowest first.</summary>
    public int Order { get; init; }

    /// <inheritdoc />
    public override string ToString() => Id;
}
