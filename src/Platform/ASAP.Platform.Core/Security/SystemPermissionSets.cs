using ASAP.Platform.Kernel.Security;

namespace ASAP.Platform.Core.Security;

/// <summary>
/// The permission sets ASAP ships with, defined as rules over whatever the loaded modules declare
/// rather than as fixed lists of keys.
/// </summary>
/// <remarks>
/// <para>
/// This is the difference between a set that stays correct and one that rots. A hand-written list
/// of keys is right on the day it is written and wrong the moment a module adds a permission, and
/// the wrongness shows up as an administrator quietly missing an ability nobody thought to add.
/// </para>
/// <para>
/// Expressed as rules, the same definitions serve two callers: the seeder building sets for a new
/// installation, and the synchroniser refreshing them after a module is installed. Both produce
/// the same answer because both ask the same question.
/// </para>
/// </remarks>
public static class SystemPermissionSets
{
    /// <summary>One shipped set: its code, its names, and what it grants.</summary>
    /// <param name="Code">Short stable code.</param>
    /// <param name="Name">Name shown when assigning it.</param>
    /// <param name="NameArabic">Arabic name.</param>
    /// <param name="Description">What the set is for.</param>
    /// <param name="Includes">Which declared permissions it grants.</param>
    public sealed record Definition(
        string Code,
        string Name,
        string NameArabic,
        string Description,
        Func<PermissionDescriptor, bool> Includes);

    /// <summary>Every set ASAP ships.</summary>
    public static IReadOnlyList<Definition> All { get; } =
    [
        new(
            "ADMIN",
            "Administrator",
            "مسؤول النظام",
            "Full access to every module and every setting.",
            static _ => true),

        new(
            "VIEWER",
            "Read only",
            "اطلاع فقط",
            "Can see everything, and change nothing.",
            static p => p.Action is PermissionAction.Read),

        new(
            "SETUP",
            "Setup manager",
            "مسؤول الإعدادات",
            "Maintains dimensions, number series and system setup, without touching users or permissions.",
            static p => p.Resource is "Dimension" or "NumberSeries" or "Setup"
                        && p.Action is not PermissionAction.Override),

        new(
            "ACCOUNTANT",
            "Accountant",
            "محاسب",
            "Prepares and posts journals, maintains the chart of accounts, runs financial reports.",
            static p => p.Module is "Finance"
                        && p.Action is not PermissionAction.Override
                        && p.Resource is not "Period"),

        new(
            "BOOKKEEPER",
            "Bookkeeper",
            "مسك دفاتر",

            // Prepares journals but cannot post them. The separation exists because the person who
            // keys a journal should not usually be the person who commits it to the ledger.
            "Prepares journals and views the ledger, but cannot post.",
            static p => p.Module is "Finance"
                        && p.Action is PermissionAction.Read or PermissionAction.Create),
    ];

    /// <summary>
    /// Works out which permission keys a shipped set grants, given what the modules declare.
    /// </summary>
    /// <param name="definition">The set to resolve.</param>
    /// <param name="declared">Every permission declared by every loaded module.</param>
    /// <returns>The keys, ordered so two runs produce the same list.</returns>
    public static IReadOnlyList<string> Resolve(
        Definition definition,
        IEnumerable<PermissionDescriptor> declared)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(declared);

        return
        [
            .. declared
                .Where(definition.Includes)
                .Select(static p => p.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal),
        ];
    }
}
