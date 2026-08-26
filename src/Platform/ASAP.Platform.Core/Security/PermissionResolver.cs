using ASAP.Platform.Kernel.Security;

namespace ASAP.Platform.Core.Security;

/// <summary>
/// Works out the full set of permission keys a user holds in one company and branch.
/// </summary>
/// <remarks>
/// <para>
/// Three things widen a grant, and this is where all three are applied:
/// </para>
/// <list type="number">
///   <item><description>
///     <b>Scope.</b> An assignment with no company applies to every company; one with no branch
///     applies to every branch.
///   </description></item>
///   <item><description>
///     <b>Inclusion.</b> A set that includes another grants everything that other set grants,
///     to any depth.
///   </description></item>
///   <item><description>
///     <b>Implication.</b> A permission declared as implying another grants that one too, so
///     granting <c>Finance.Journal.Post</c> also grants <c>Finance.Journal.Read</c>. Posting
///     something you cannot see is meaningless, and making administrators grant both by hand
///     only invites the mistake of granting one.
///   </description></item>
/// </list>
/// <para>
/// The result is a flat set of keys, which is what makes checking a permission at request time a
/// hash lookup rather than a graph walk.
/// </para>
/// </remarks>
/// <param name="declaredPermissions">
/// Every permission declared by every loaded module. Supplies the implication graph.
/// </param>
public sealed class PermissionResolver(IEnumerable<PermissionDescriptor> declaredPermissions)
{
    private readonly Dictionary<string, PermissionDescriptor> _declared =
        declaredPermissions.ToDictionary(static p => p.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves what a user may do.
    /// </summary>
    /// <param name="assignments">Every assignment the user holds, across all companies.</param>
    /// <param name="setsById">The permission sets those assignments point at, with entries and inclusions loaded.</param>
    /// <param name="companyId">The company being worked in.</param>
    /// <param name="branchId">The branch being worked at, or null for head office.</param>
    /// <param name="asOf">The date to test time-limited assignments against.</param>
    /// <returns>Every permission key the user holds, flattened.</returns>
    public IReadOnlySet<string> Resolve(
        IEnumerable<UserPermissionAssignment> assignments,
        IReadOnlyDictionary<Guid, PermissionSet> setsById,
        Guid companyId,
        Guid? branchId,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(setsById);

        var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assignment in assignments.Where(a => Applies(a, companyId, branchId, asOf)))
        {
            CollectFromSet(assignment.PermissionSetId, setsById, granted, visiting: []);
        }

        return ExpandImplications(granted);
    }

    /// <summary>
    /// Whether an assignment covers a given company, branch and date.
    /// </summary>
    /// <remarks>
    /// A null company or branch on the assignment widens rather than narrows: it means "all of
    /// them", which is how a group-wide accountant is granted access once instead of once per company.
    /// </remarks>
    private static bool Applies(
        UserPermissionAssignment assignment,
        Guid companyId,
        Guid? branchId,
        DateOnly asOf)
    {
        if (assignment.CompanyId is { } assignedCompany && assignedCompany != companyId)
        {
            return false;
        }

        // A branch-limited assignment grants nothing at head office. Someone allowed to sell at
        // one shop is not thereby allowed to act across the company.
        if (assignment.BranchId is { } assignedBranch && assignedBranch != branchId)
        {
            return false;
        }

        return assignment.IsEffectiveOn(asOf);
    }

    /// <summary>
    /// Adds every key a set grants, following its inclusions to any depth.
    /// </summary>
    /// <param name="setId">The set to collect from.</param>
    /// <param name="setsById">All loaded sets.</param>
    /// <param name="granted">Accumulates the keys found.</param>
    /// <param name="visiting">
    /// Sets already on the current path. Guards against a cycle: an administrator can construct
    /// one through the UI, and it must degrade to "grant everything reachable once" rather than
    /// hanging the request.
    /// </param>
    private static void CollectFromSet(
        Guid setId,
        IReadOnlyDictionary<Guid, PermissionSet> setsById,
        HashSet<string> granted,
        HashSet<Guid> visiting)
    {
        if (!visiting.Add(setId))
        {
            return;
        }

        // A set can be absent because it was deleted while an assignment still points at it.
        // Granting nothing is the safe reading.
        if (!setsById.TryGetValue(setId, out var set))
        {
            return;
        }

        foreach (var entry in set.Entries)
        {
            granted.Add(entry.PermissionKey);
        }

        foreach (var inclusion in set.Includes)
        {
            CollectFromSet(inclusion.IncludedPermissionSetId, setsById, granted, visiting);
        }
    }

    /// <summary>
    /// Adds every permission implied by one already granted, following the chain transitively.
    /// </summary>
    private IReadOnlySet<string> ExpandImplications(HashSet<string> granted)
    {
        var pending = new Queue<string>(granted);

        while (pending.Count > 0)
        {
            var key = pending.Dequeue();

            if (!_declared.TryGetValue(key, out var descriptor))
            {
                // Granted but undeclared: the module that owns it is not loaded. Keep the key,
                // since the module may come back, but there is no implication graph to follow.
                continue;
            }

            foreach (var implied in descriptor.Implies)
            {
                if (granted.Add(implied))
                {
                    pending.Enqueue(implied);
                }
            }
        }

        return granted;
    }
}
