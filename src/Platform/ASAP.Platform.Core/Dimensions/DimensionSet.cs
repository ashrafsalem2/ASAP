using ASAP.Platform.Kernel.Entities;

namespace ASAP.Platform.Core.Dimensions;

/// <summary>
/// One stored combination of dimension values, shared by every entry posted with it.
/// </summary>
/// <remarks>
/// A ledger entry carries a pointer to one of these rather than its own copy of the dimension
/// values. Two entries posted with the same department and project point at the same set, so a
/// company running four dimensions accumulates a few thousand sets over its lifetime rather than
/// several rows per ledger entry.
/// </remarks>
public sealed class DimensionSet : Entity
{
    /// <summary>Tenant that owns the set.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Company that owns the set. Sets are never shared across companies.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// The 32-byte fingerprint of the combination, uniquely indexed per company. Looking a
    /// combination up before posting is a single seek on this column.
    /// </summary>
    public required byte[] Fingerprint { get; set; }

    /// <summary>
    /// The canonical text the fingerprint was taken over. Kept for support: it lets someone read
    /// what a set actually contains without joining out to the entries.
    /// </summary>
    public required string Signature { get; set; }

    /// <summary>When the set was first created, in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The dimension values in the set.</summary>
    public ICollection<DimensionSetEntry> Entries { get; set; } = [];

    /// <summary>Rebuilds the combination this set represents.</summary>
    public DimensionCombination ToCombination()
        => DimensionCombination.From(
            Entries.Select(static e => new DimensionPair(e.DimensionId, e.DimensionValueId)));
}

/// <summary>One dimension value inside a stored set.</summary>
public sealed class DimensionSetEntry : Entity
{
    /// <summary>The set this belongs to.</summary>
    public Guid DimensionSetId { get; set; }

    /// <summary>Navigation to the set.</summary>
    public DimensionSet? DimensionSet { get; set; }

    /// <summary>The dimension.</summary>
    public Guid DimensionId { get; set; }

    /// <summary>Navigation to the dimension.</summary>
    public Dimension? Dimension { get; set; }

    /// <summary>The value it is set to.</summary>
    public Guid DimensionValueId { get; set; }

    /// <summary>Navigation to the value.</summary>
    public DimensionValue? DimensionValue { get; set; }
}
