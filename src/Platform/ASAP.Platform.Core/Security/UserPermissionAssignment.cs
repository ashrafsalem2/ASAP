using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Tenancy;

namespace ASAP.Platform.Core.Security;

/// <summary>
/// Grants one permission set to one user, in one company, optionally at one branch.
/// </summary>
/// <remarks>
/// <para>
/// This is the row that answers "what may this person do here". Its shape is what lets the same
/// login be an accountant in the trading company, read-only in the property company, and a
/// cashier at exactly one shop:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="CompanyId"/> null means every company in the tenant.</description></item>
///   <item><description><see cref="BranchId"/> null means every branch in that company.</description></item>
/// </list>
/// <para>
/// Assignments only ever add. There is no deny row, because deny rules interact in ways nobody
/// can reason about at three in the afternoon with a queue at the till. If someone should not be
/// able to do something, do not grant it.
/// </para>
/// </remarks>
public sealed class UserPermissionAssignment : AuditableEntity, ITenantScoped
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>The user being granted.</summary>
    public Guid UserId { get; set; }

    /// <summary>Navigation to the user.</summary>
    public User? User { get; set; }

    /// <summary>The set being granted.</summary>
    public Guid PermissionSetId { get; set; }

    /// <summary>Navigation to the set.</summary>
    public PermissionSet? PermissionSet { get; set; }

    /// <summary>Company the grant applies in, or null for every company in the tenant.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>Branch the grant is narrowed to, or null for every branch in the company.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// When the grant starts, or null for immediately. Used to prepare a promotion ahead of time.
    /// </summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>
    /// When the grant stops, or null for open-ended. Used for temporary cover: someone standing
    /// in for a colleague on leave gets access that expires on its own rather than being
    /// remembered about later.
    /// </summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Whether the grant is in force on a given date.</summary>
    /// <param name="on">The date to test.</param>
    public bool IsEffectiveOn(DateOnly on)
        => (EffectiveFrom is null || on >= EffectiveFrom)
        && (EffectiveTo is null || on <= EffectiveTo);
}
