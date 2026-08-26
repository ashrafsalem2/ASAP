using ASAP.Platform.Core.Security;
using ASAP.Platform.Kernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Platform.Persistence;

/// <summary>
/// Brings the shipped permission sets back into line with what the loaded modules declare.
/// </summary>
/// <remarks>
/// <para>
/// Runs at every startup, and exists because of a gap that is easy to miss: a customer buys
/// Inventory, the module is installed, and nothing grants its permissions to the Administrator
/// set. The set was written when Inventory did not exist. Nobody gets an error; the screens
/// simply are not there, and the first person to notice is the customer.
/// </para>
/// <para>
/// It only ever touches sets marked <see cref="PermissionSet.IsSystemDefined"/>. A set an
/// administrator created, or a copy they made of a shipped one, is never modified -- which is
/// what makes copying a shipped set the safe way to customise it.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="declared">Every permission the loaded modules declare.</param>
/// <param name="logger">Reports what changed.</param>
public sealed class SystemPermissionSetSynchroniser(
    AsapDbContext context,
    IReadOnlyCollection<PermissionDescriptor> declared,
    ILogger<SystemPermissionSetSynchroniser> logger)
{
    /// <summary>
    /// Syncs every tenant's shipped sets.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many sets were changed.</returns>
    /// <remarks>
    /// Must be called inside a cross-tenant scope: it works across every tenant at once, which
    /// the company filters would otherwise prevent.
    /// </remarks>
    public async Task<int> SynchroniseAsync(CancellationToken cancellationToken = default)
    {
        var tenantIds = await context.Tenants
            .Select(static t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tenantIds.Count == 0)
        {
            return 0;
        }

        var existing = await context.PermissionSets
            .Include(s => s.Entries)
            .Where(s => s.IsSystemDefined)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var changed = 0;

        foreach (var tenantId in tenantIds)
        {
            foreach (var definition in SystemPermissionSets.All)
            {
                var wanted = SystemPermissionSets.Resolve(definition, declared);

                // A set that resolves to nothing belongs to a module this installation does not
                // have. Creating an empty Accountant set on a tenant without Finance would put a
                // meaningless option on the assignment screen.
                if (wanted.Count == 0)
                {
                    continue;
                }

                var set = existing.Find(s =>
                    s.TenantId == tenantId
                    && string.Equals(s.Code, definition.Code, StringComparison.OrdinalIgnoreCase));

                if (set is null)
                {
                    context.PermissionSets.Add(Create(tenantId, definition, wanted));
                    changed++;
                    continue;
                }

                if (Reconcile(context, set, wanted))
                {
                    changed++;
                }
            }
        }

        if (changed > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Refreshed {Count} shipped permission set(s) to match the {PermissionCount} "
                + "permissions the loaded modules declare.",
                changed,
                declared.Count);
        }

        return changed;
    }

    private static PermissionSet Create(
        Guid tenantId,
        SystemPermissionSets.Definition definition,
        IReadOnlyList<string> keys)
    {
        var set = new PermissionSet
        {
            TenantId = tenantId,
            Code = definition.Code,
            Name = definition.Name,
            NameArabic = definition.NameArabic,
            Description = definition.Description,
            IsSystemDefined = true,
        };

        foreach (var key in keys)
        {
            set.Entries.Add(new PermissionSetEntry { PermissionSetId = set.Id, PermissionKey = key });
        }

        return set;
    }

    /// <summary>
    /// Adds what is missing and removes what no longer exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Removal matters as much as addition. When a module is uninstalled its permissions stop
    /// being declared, and leaving the keys behind would show an administrator grants that
    /// resolve to nothing -- which reads as a system that has lost track of itself.
    /// </para>
    /// <para>
    /// Rows are added and removed through the set directly rather than by mutating the parent's
    /// navigation collection. Both are supposed to work, but the collection route left EF free to
    /// classify a new child as modified rather than added -- and it did, producing
    /// <c>UPDATE ... WHERE Id = ...</c> against rows that had never been inserted, which surfaces
    /// as a concurrency exception blaming another user for a change nobody made. Saying add and
    /// remove outright leaves nothing to infer.
    /// </para>
    /// </remarks>
    private static bool Reconcile(AsapDbContext context, PermissionSet set, IReadOnlyList<string> wanted)
    {
        var target = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
        var held = set.Entries.Select(static e => e.PermissionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = target.Except(held, StringComparer.OrdinalIgnoreCase).ToList();
        var surplus = set.Entries.Where(e => !target.Contains(e.PermissionKey)).ToList();

        foreach (var key in missing)
        {
            context.PermissionSetEntries.Add(new PermissionSetEntry
            {
                PermissionSetId = set.Id,
                PermissionKey = key,
            });
        }

        if (surplus.Count > 0)
        {
            context.PermissionSetEntries.RemoveRange(surplus);
        }

        return missing.Count > 0 || surplus.Count > 0;
    }
}
