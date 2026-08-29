using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Hr.Entitlements;

/// <summary>Which running provision a row tracks.</summary>
public enum ProvisionType
{
    /// <summary>What the company would owe every current employee if it let them all go today.</summary>
    /// <remarks>
    /// Reserved. Nothing writes a row of this type today: the payroll run charges the
    /// end-of-service provision as it is earned, per branch, so there is no movement left for a
    /// provision run to post. Kept for the reconciliation that will compare payroll's running
    /// total against the entitlement formula.
    /// </remarks>
    EndOfService = 0,

    /// <summary>What unused leave is worth to everybody who has some.</summary>
    Leave = 1,
}

/// <summary>
/// The last amount HR asked the general ledger to carry for one provision.
/// </summary>
/// <remarks>
/// <para>
/// A provision does not move the way a settlement does. Paying somebody their end-of-service
/// award is one transaction with a start and an end; the provision behind it has no such moment
/// — it is a running total that changes a little every time somebody's service grows by a day or
/// their wage rises, and the ledger only ever receives a movement, not a balance. This is where
/// the last movement is kept, so the next run posts the difference rather than the whole figure
/// over again.
/// </para>
/// <para>
/// One row per company per <see cref="ProvisionType"/>, updated in place. The history that
/// matters is the general ledger entries themselves; this exists only to answer "what did we
/// tell the ledger last time", which is the one thing the ledger itself cannot say back without
/// walking every posting HR has ever made.
/// </para>
/// </remarks>
public sealed class EntitlementProvision : CompanyEntity
{
    /// <summary>Which provision this is.</summary>
    public ProvisionType Type { get; set; }

    /// <summary>What was last asked to be carried in the ledger for it.</summary>
    public decimal PostedAmount { get; set; }

    /// <summary>The day the figure behind <see cref="PostedAmount"/> was computed as of.</summary>
    public DateOnly AsOf { get; set; }

    /// <summary>The transaction the posting belonged to, when one was made.</summary>
    public long? LastTransactionNo { get; set; }
}
