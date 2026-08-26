namespace ASAP.Platform.Kernel.Security;

/// <summary>
/// Who is making the current request and what they are allowed to do in the company they are
/// working in.
/// </summary>
/// <remarks>
/// Permissions are resolved per company, not once per user. The same person can be an
/// accountant in one company and read-only in another, and a cashier can be allowed to give
/// discounts at one branch but not at the next. Everything here is already narrowed to the
/// company and branch on the current <see cref="Tenancy.ITenantContext"/>.
/// </remarks>
public interface IUserContext
{
    /// <summary>The signed-in user, or null on anonymous requests such as login.</summary>
    Guid? UserId { get; }

    /// <summary>Login name, useful for audit trails and log lines.</summary>
    string? UserName { get; }

    /// <summary>Display name, for greeting the user and stamping documents.</summary>
    string? DisplayName { get; }

    /// <summary>
    /// Preferred UI culture, for example <c>ar-SA</c>. Drives which side of every
    /// <see cref="Messaging.LocalizedText"/> the user sees.
    /// </summary>
    string? Culture { get; }

    /// <summary>
    /// True for the installation owner, who passes every permission check. Held by one or two
    /// accounts only, and every action they take is audited.
    /// </summary>
    bool IsSuperUser { get; }

    /// <summary>
    /// Every permission key the user holds in the active company, with implied permissions
    /// already expanded. Compared case-insensitively.
    /// </summary>
    IReadOnlySet<string> Permissions { get; }

    /// <summary>
    /// Whether the user holds a permission in the active company.
    /// </summary>
    /// <param name="permissionKey">A key such as <c>Finance.Journal.Post</c>.</param>
    bool Has(string permissionKey);

    /// <summary>
    /// Whether the user may act on a resource.
    /// </summary>
    /// <param name="module">Owning module, for example <c>Finance</c>.</param>
    /// <param name="resource">Guarded resource, for example <c>Journal</c>.</param>
    /// <param name="action">Verb being attempted.</param>
    bool Can(string module, string resource, PermissionAction action)
        => Has(PermissionDescriptor.BuildKey(module, resource, action));

    /// <summary>
    /// The signed-in user, or a thrown exception on an anonymous request. Use where an
    /// identity is a precondition, so the failure is a clear message rather than a null later on.
    /// </summary>
    Guid RequireUserId();
}
