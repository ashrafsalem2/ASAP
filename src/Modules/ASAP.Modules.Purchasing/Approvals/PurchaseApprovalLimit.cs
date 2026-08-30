using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Purchasing.Approvals;

/// <summary>
/// How much one person may approve a purchase order for.
/// </summary>
/// <remarks>
/// <para>
/// Per person rather than per role, and that is not laziness about roles. An approval limit is a
/// statement about an individual's authority that somebody signed for, and it is the thing an
/// auditor asks to see. A role carries it only until somebody is added to the role for an
/// unrelated reason and quietly gains the authority to sign for a hundred thousand.
/// </para>
/// <para>
/// Somebody with no limit at all can approve nothing. That is the safe default: a system where
/// unknown means unlimited is a system where the answer to "who can approve this" is "whoever has
/// not been set up yet".
/// </para>
/// </remarks>
public sealed class PurchaseApprovalLimit : CompanyEntity
{
    /// <summary>The person the limit belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>Their user name, copied so a list can be read without joining.</summary>
    public required string UserName { get; set; }

    /// <summary>What they are called, for a message that has to name them.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The most they may approve, on one order.
    /// </summary>
    /// <remarks>
    /// Per order, not per day or per vendor. A daily total would mean the same order is approvable
    /// or not depending on what else happened that morning, which is not a rule anybody can plan
    /// around.
    /// </remarks>
    public decimal MaximumAmount { get; set; }

    /// <summary>Whether the limit is still in force.</summary>
    /// <remarks>
    /// Withdrawn rather than deleted, because an order approved last year was approved by somebody
    /// whose authority has to remain visible on the record.
    /// </remarks>
    public bool IsActive { get; set; } = true;
}
