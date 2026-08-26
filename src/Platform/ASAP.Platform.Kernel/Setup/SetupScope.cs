namespace ASAP.Platform.Kernel.Setup;

/// <summary>
/// How widely one setup value applies, and therefore how a lookup resolves it.
/// </summary>
/// <remarks>
/// Values resolve from the narrowest scope outwards: user, then branch, then company, then
/// tenant, then the declared default. That is what lets head office set a policy once for the
/// whole company while a single branch overrides the part that genuinely differs, without
/// anyone maintaining a copy of every setting per branch.
/// </remarks>
public enum SetupScope
{
    /// <summary>Applies across the whole subscription. Licensing and platform-wide policy.</summary>
    Tenant = 0,

    /// <summary>
    /// Applies to one legal entity. Where most accounting policy lives: base currency,
    /// costing method, posting windows.
    /// </summary>
    Company = 1,

    /// <summary>
    /// Applies to one branch. Where a shop legitimately differs from head office: its till
    /// float, its receipt printer, its discount ceiling.
    /// </summary>
    Branch = 2,

    /// <summary>Applies to one person. Preferences only, never policy.</summary>
    User = 3,
}
