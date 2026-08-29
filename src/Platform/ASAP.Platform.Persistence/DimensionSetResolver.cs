using ASAP.Platform.Core.Dimensions;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Platform.Persistence;

/// <summary>What a set of dimension codes resolved to.</summary>
/// <param name="Combination">The values, ready for the posting engine to check.</param>
/// <param name="Found">Everything wrong with what was asked for.</param>
/// <remarks>
/// No stored set, deliberately. Resolving a document's codes and resolving one line's are two
/// steps that get merged before anything is stored, and a resolve that stored as it went would
/// leave a row for every intermediate combination nothing ever points at. Ask for the set once,
/// at the end, with <see cref="DimensionSetResolver.SetForAsync"/>.
/// </remarks>
public readonly record struct ResolvedDimensions(
    DimensionCombination Combination,
    IReadOnlyList<AsapMessage> Found)
{
    /// <summary>Whether anything named was unusable.</summary>
    public bool Failed => Found.Any(static m => m.IsFailure);
}

/// <summary>
/// Turns the dimension codes a document names into a stored set.
/// </summary>
/// <remarks>
/// <para>
/// Documents name dimensions the way a person does — <c>DEPARTMENT</c> is <c>SALES</c> — because
/// that is what an integration sends and what somebody types. Everything below the posting engine
/// works in identifiers, and this is the one place the two meet.
/// </para>
/// <para>
/// Sets are shared. Two entries posted with the same department and project point at the same
/// row, which is why a company running four dimensions accumulates a few thousand sets over its
/// life rather than several rows per ledger entry. Finding an existing set is a single seek on
/// the fingerprint.
/// </para>
/// <para>
/// It lives in the platform rather than in Finance because every module posts through dimensions.
/// Payroll charges a department, a stock transfer carries a project; if this were Finance's, each
/// of them would either reference Finance or grow its own copy.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenantContext">Supplies the company a set belongs to.</param>
/// <param name="clock">Stamps when a set was first seen.</param>
public sealed class DimensionSetResolver(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenantContext,
    IClock clock)
{
    /// <summary>
    /// Resolves dimension codes to a stored set, creating it the first time it is seen.
    /// </summary>
    /// <param name="wanted">Dimension code to value code. Null or empty resolves to nothing.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The combination, and every reason a code could not be used.</returns>
    /// <remarks>
    /// Checks only. Nothing is added and nothing is saved, so a document that is then refused
    /// leaves no trace — which matters, because a dimension set nothing points at is a row that
    /// will puzzle somebody later.
    /// </remarks>
    public async Task<ResolvedDimensions> ResolveAsync(
        IReadOnlyDictionary<string, string>? wanted,
        CancellationToken cancellationToken = default)
    {
        if (wanted is null || wanted.Count == 0)
        {
            return new ResolvedDimensions(DimensionCombination.Empty, []);
        }

        var codes = wanted.Keys.Select(static c => c.Trim().ToUpperInvariant()).ToList();

        var dimensions = await context.Set<Dimension>()
            .AsNoTracking()
            .Include(d => d.Values)
            .Where(d => codes.Contains(d.Code))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byCode = dimensions.ToDictionary(static d => d.Code, StringComparer.OrdinalIgnoreCase);
        var found = new List<AsapMessage>();
        var pairs = new List<DimensionPair>();

        foreach (var (dimensionCode, valueCode) in wanted)
        {
            var trimmedDimension = dimensionCode.Trim();
            var trimmedValue = valueCode?.Trim() ?? string.Empty;

            // An empty value is somebody clearing the field, not an error. It simply means this
            // document carries no value for that axis.
            if (trimmedValue.Length == 0)
            {
                continue;
            }

            if (!byCode.TryGetValue(trimmedDimension, out var dimension))
            {
                found.Add(messages.Render(
                    PlatformMessages.DimensionNotFound,
                    Args(("Dimension", trimmedDimension))));

                continue;
            }

            if (dimension.IsBlocked)
            {
                found.Add(messages.Render(
                    PlatformMessages.DimensionBlocked,
                    Args(("Dimension", dimension.Name))));

                continue;
            }

            var value = dimension.Values.FirstOrDefault(
                v => string.Equals(v.Code, trimmedValue, StringComparison.OrdinalIgnoreCase));

            if (value is null)
            {
                found.Add(messages.Render(
                    PlatformMessages.DimensionValueNotFound,
                    Args(("Dimension", dimension.Name), ("Value", trimmedValue))));

                continue;
            }

            // A heading or a total is a thing to report under, not a thing to post to. Letting one
            // through would put entries beneath a subtotal that also sums them.
            if (!value.IsPostable)
            {
                found.Add(messages.Render(
                    PlatformMessages.DimensionValueBlocked,
                    Args(("Dimension", dimension.Name), ("Value", value.Code))));

                continue;
            }

            pairs.Add(new DimensionPair(dimension.Id, value.Id));
        }

        return found.Exists(static m => m.IsFailure)
            ? new ResolvedDimensions(DimensionCombination.Empty, found)
            : new ResolvedDimensions(DimensionCombination.From(pairs), found);
    }

    /// <summary>
    /// Which dimensions are the shortcuts, by position.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The dimension at position one and at position two, either of which may be null.</returns>
    /// <remarks>
    /// A shortcut's value is copied straight onto every ledger entry as well as into the set, so
    /// grouping a million entries by department is an index seek rather than a join. That only
    /// works if the posting engine is told which dimension holds which position, and this is what
    /// tells it.
    /// </remarks>
    public async Task<(Guid? First, Guid? Second)> ShortcutsAsync(
        CancellationToken cancellationToken = default)
    {
        var shortcuts = await context.Set<Dimension>()
            .AsNoTracking()
            .Where(d => d.ShortcutIndex == 1 || d.ShortcutIndex == 2)
            .Select(d => new { d.Id, d.ShortcutIndex })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (
            shortcuts.FirstOrDefault(s => s.ShortcutIndex == 1)?.Id,
            shortcuts.FirstOrDefault(s => s.ShortcutIndex == 2)?.Id);
    }

    /// <summary>
    /// Finds the stored set for a combination that has already been resolved, or adds it.
    /// </summary>
    /// <param name="combination">The values.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The set, or null when the combination is empty.</returns>
    /// <remarks>
    /// For a caller that merged two combinations of its own — a document's analysis and one
    /// line's override — and needs the result stored. The values have already been checked by
    /// the time they got here, so this only finds or creates.
    /// </remarks>
    public async Task<Guid?> SetForAsync(
        DimensionCombination combination,
        CancellationToken cancellationToken = default)
        => combination.IsEmpty
            ? null
            : await SetIdAsync(combination, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Finds the stored set for a combination, or adds it.
    /// </summary>
    /// <remarks>
    /// Looked up by fingerprint, which is a single seek on a unique index. The canonical text is
    /// stored beside it so somebody supporting a company can read what a set contains without
    /// joining out to its entries — the sort of thing that turns a half-hour into a minute.
    /// </remarks>
    private async Task<Guid> SetIdAsync(
        DimensionCombination combination,
        CancellationToken cancellationToken)
    {
        var fingerprint = combination.ComputeFingerprint();

        // What this unit of work has already added, before what the database holds. A document
        // whose lines share a combination asks for it once per line, and none of those additions
        // has been saved yet -- so a lookup that went only to the database would add the set
        // again, and the second insert would be refused by the unique index on the fingerprint.
        var pending = context.Set<DimensionSet>()
            .Local
            .FirstOrDefault(s => s.Fingerprint.SequenceEqual(fingerprint));

        if (pending is not null)
        {
            return pending.Id;
        }

        var existing = await context.Set<DimensionSet>()
            .FirstOrDefaultAsync(s => s.Fingerprint == fingerprint, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Id;
        }

        var set = new DimensionSet
        {
            TenantId = tenantContext.TenantId ?? Guid.Empty,
            CompanyId = tenantContext.RequireCompanyId(),
            Fingerprint = fingerprint,
            Signature = combination.ToCanonicalString(),
            CreatedAtUtc = clock.UtcNow,
        };

        context.Set<DimensionSet>().Add(set);

        foreach (var pair in combination.Pairs)
        {
            // Added through the set rather than the collection. Keys are handed out by the
            // constructor, and EF reads an already-set key on a child as "this row exists".
            context.Set<DimensionSetEntry>().Add(new DimensionSetEntry
            {
                DimensionSetId = set.Id,
                DimensionId = pair.DimensionId,
                DimensionValueId = pair.DimensionValueId,
            });
        }

        return set.Id;
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}
