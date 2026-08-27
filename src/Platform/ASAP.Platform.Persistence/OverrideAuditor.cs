using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;

namespace ASAP.Platform.Persistence;

/// <summary>
/// Records every protection somebody pushed past.
/// </summary>
/// <remarks>
/// <para>
/// An overridable block downgrades to a warning when the caller holds the permission, and the text
/// the user sees says the override has been recorded against their name. Something has to make
/// that true, and it has to be the same something everywhere: the promise is made by the platform,
/// in one shared sentence, so a module that forgets to keep it turns that sentence into a lie
/// without changing a word of it.
/// </para>
/// <para>
/// This exists because two modules had already written their own copy and a third was about to.
/// Finance recorded its overrides; Inventory did not, and the comment claiming it did went
/// unchallenged for a fortnight. One implementation is the only version of this that stays true.
/// </para>
/// <para>
/// Nothing here saves. The rows join whatever transaction the caller is already in, so the trail
/// and the entries it describes commit together or not at all.
/// </para>
/// <para>
/// A message is recorded once however many layers see it. Sales asks Inventory to move stock and
/// passes the messages it gets back to its own <see cref="Record"/> call, which wrote the same
/// override twice and made one negative-stock decision look like two. The row that survives is
/// the innermost one, which names the layer that actually raised the block.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="tenantContext">Supplies the company and branch.</param>
/// <param name="userContext">Names who pushed past it.</param>
/// <param name="clock">Supplies the time.</param>
public sealed class OverrideAuditor(
    AsapDbContext context,
    ITenantContext tenantContext,
    IUserContext userContext,
    IClock clock)
{
    // By reference, not by value: two identical refusals on two different lines are two overrides
    // and both belong in the trail. What must not be counted twice is one message seen twice on
    // its way up.
    private readonly HashSet<AsapMessage> recorded =
        new(ReferenceEqualityComparer.Instance as IEqualityComparer<AsapMessage>);

    /// <summary>
    /// Writes an audit row for every message that was a refusal until the caller overrode it.
    /// </summary>
    /// <param name="messages">Everything the operation raised. Anything else is ignored.</param>
    /// <param name="entityType">What was being posted, for example <c>Finance.GlEntry</c>.</param>
    /// <param name="displayNo">The document number a person would recognise it by.</param>
    /// <param name="reason">Why the caller says they pushed past it.</param>
    /// <returns>How many overrides this call recorded, not counting any already in the trail.</returns>
    public int Record(
        IEnumerable<AsapMessage> messages,
        string entityType,
        string? displayNo,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var written = 0;

        foreach (var message in messages)
        {
            // Only messages actually downgraded. Severity plus an override permission is also the
            // shape of a message that was never more than a warning, and logging those as
            // overrides would bury the real ones.
            if (!message.WasOverridden)
            {
                continue;
            }

            // Already written by a layer below. Recording it again would report one decision as
            // two, and "how many times did we sell below cost last month" is a question people
            // answer by counting these rows.
            if (!this.recorded.Add(message))
            {
                continue;
            }

            context.AuditLog.Add(new AuditLogEntry
            {
                TenantId = tenantContext.TenantId ?? Guid.Empty,
                CompanyId = tenantContext.CompanyId,
                BranchId = tenantContext.BranchId,
                UserId = userContext.UserId,
                UserName = userContext.UserName,
                OccurredAtUtc = clock.UtcNow,
                Action = AuditAction.Override,
                EntityType = entityType,
                DisplayNo = displayNo,
                OverriddenMessageCode = message.Code.Value,
                OverrideReason = reason,

                // The detail carries the figures behind the refusal, which is what makes the row
                // worth reading a year later. "Over the credit limit" says nothing; "owed 48,000
                // and this took them to 52,000 against a limit of 50,000" says everything.
                Changes = message.Detail,
            });

            written++;
        }

        return written;
    }
}
