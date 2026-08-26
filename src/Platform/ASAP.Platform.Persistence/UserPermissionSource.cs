using ASAP.Platform.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Platform.Persistence;

/// <summary>
/// Reads permission assignments out of the database and resolves them.
/// </summary>
/// <param name="context">The unit of work.</param>
/// <param name="resolver">Expands inclusions and implications.</param>
public sealed class UserPermissionSource(AsapDbContext context, PermissionResolver resolver)
    : IUserPermissionSource
{
    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> ResolveAsync(
        Guid userId,
        Guid companyId,
        Guid? branchId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        // Assignments span companies, and the tenant filter alone does not narrow them to the
        // one being worked in -- that is the resolver's job, since a null company on an
        // assignment means "every company" rather than "no company".
        var assignments = await context.UserPermissionAssignments
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (assignments.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var setIds = assignments.Select(static a => a.PermissionSetId).Distinct().ToList();

        // Loaded in one pass with their entries and inclusions, because the resolver walks the
        // inclusion graph in memory and a lazy load per hop would be a query storm on every request.
        var sets = await context.PermissionSets
            .AsNoTracking()
            .Include(s => s.Entries)
            .Include(s => s.Includes)
            .Where(s => setIds.Contains(s.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // An included set may itself not be assigned directly, so keep pulling until the graph
        // is closed. Bounded by the number of sets in the tenant, and in practice one extra pass.
        var loaded = sets.ToDictionary(static s => s.Id);
        var pending = sets
            .SelectMany(static s => s.Includes)
            .Select(static i => i.IncludedPermissionSetId)
            .Where(id => !loaded.ContainsKey(id))
            .Distinct()
            .ToList();

        while (pending.Count > 0)
        {
            var more = await context.PermissionSets
                .AsNoTracking()
                .Include(s => s.Entries)
                .Include(s => s.Includes)
                .Where(s => pending.Contains(s.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (more.Count == 0)
            {
                break;
            }

            foreach (var set in more)
            {
                loaded[set.Id] = set;
            }

            pending = more
                .SelectMany(static s => s.Includes)
                .Select(static i => i.IncludedPermissionSetId)
                .Where(id => !loaded.ContainsKey(id))
                .Distinct()
                .ToList();
        }

        return resolver.Resolve(assignments, loaded, companyId, branchId, asOf);
    }
}
