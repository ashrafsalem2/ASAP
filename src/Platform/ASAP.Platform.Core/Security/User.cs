using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Security;

/// <summary>
/// Someone who signs in to ASAP.
/// </summary>
/// <remarks>
/// A user belongs to a tenant, not to a company. That is deliberate: one accountant commonly
/// works across several companies in a group, and forcing a separate login per company would
/// mean re-entering credentials all day and would scatter the audit trail across accounts that
/// are really the same person. What varies per company is what they are allowed to do, and that
/// lives on <see cref="UserPermissionAssignment"/>.
/// </remarks>
public sealed class User : AuditableEntity, ITenantScoped, ISoftDeletable, IConcurrencyAware
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>Login name, unique within the tenant and compared without regard to case.</summary>
    public required string UserName { get; set; }

    /// <summary>Name shown on screen and stamped on documents.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Email address, used for notifications and password reset.</summary>
    public string? Email { get; set; }

    /// <summary>Mobile number, used for notifications and two-factor codes.</summary>
    public string? Phone { get; set; }

    /// <summary>BCrypt hash of the password. Never the password itself, and never reversible.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>Preferred UI language, for example <c>ar</c>. Falls back to the tenant default.</summary>
    public string? Culture { get; set; }

    /// <summary>
    /// The installation owner, who passes every permission check. Held by one or two accounts,
    /// and everything such an account does is written to the audit log regardless of settings.
    /// </summary>
    public bool IsSuperUser { get; set; }

    /// <summary>Whether the account may sign in.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The employee record this login belongs to, once HR is present. Held as a bare key so the
    /// platform does not depend on the HR module.
    /// </summary>
    public Guid? EmployeeId { get; set; }

    /// <summary>Company the user lands in after signing in.</summary>
    public Guid? DefaultCompanyId { get; set; }

    /// <summary>Branch the user works at by default. Null for head-office staff.</summary>
    public Guid? DefaultBranchId { get; set; }

    /// <summary>When the user last signed in successfully, in UTC.</summary>
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>
    /// Consecutive failed sign-in attempts. Reset on success, and used to lock the account after
    /// the configured threshold.
    /// </summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// When the account stops being locked out, in UTC. Null when it is not locked. Held as a
    /// time rather than a flag so the lock expires on its own without a job to clear it.
    /// </summary>
    public DateTime? LockedUntilUtc { get; set; }

    /// <summary>What this user may do, per company and branch.</summary>
    public ICollection<UserPermissionAssignment> Assignments { get; set; } = [];

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; set; }

    /// <inheritdoc />
    public byte[]? RowVersion { get; set; }
}
