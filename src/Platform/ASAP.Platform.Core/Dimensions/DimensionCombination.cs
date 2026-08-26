using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ASAP.Platform.Core.Dimensions;

/// <summary>
/// A specific combination of dimension values, such as "Department = Sales, Project = Riyadh
/// Tower", reduced to something that can be looked up in one hit.
/// </summary>
/// <remarks>
/// <para>
/// A single sales invoice can produce a dozen ledger entries, and every one of them carries the
/// same dimensions. Storing those values row by row would multiply the dimension rows by the
/// entry count, and a year of trading would be spent writing the same combination over and over.
/// </para>
/// <para>
/// Instead ASAP stores each distinct combination once and points entries at it, which is how
/// Business Central handles the same problem. This type is the key to that store: it puts the
/// pairs in a canonical order and hashes them, so two combinations built in different orders by
/// different modules resolve to the same stored set.
/// </para>
/// </remarks>
public readonly struct DimensionCombination : IEquatable<DimensionCombination>
{
    private readonly ImmutableArray<DimensionPair> _pairs;

    private DimensionCombination(ImmutableArray<DimensionPair> pairs)
    {
        _pairs = pairs;
    }

    /// <summary>A combination with no dimensions on it.</summary>
    public static DimensionCombination Empty { get; } = new([]);

    /// <summary>
    /// The pairs, ordered by dimension so the sequence is canonical.
    /// </summary>
    public ImmutableArray<DimensionPair> Pairs => _pairs.IsDefault ? [] : _pairs;

    /// <summary>True when no dimensions are set.</summary>
    public bool IsEmpty => Pairs.Length == 0;

    /// <summary>How many dimensions carry a value.</summary>
    public int Count => Pairs.Length;

    /// <summary>
    /// Builds a combination from a set of pairs.
    /// </summary>
    /// <param name="pairs">
    /// The dimension and value pairs. Order does not matter; they are sorted. A dimension
    /// appearing twice keeps its last value, which lets a caller layer a document default under
    /// a line override without filtering first.
    /// </param>
    public static DimensionCombination From(IEnumerable<DimensionPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        var deduplicated = new Dictionary<Guid, Guid>();

        foreach (var pair in pairs)
        {
            deduplicated[pair.DimensionId] = pair.DimensionValueId;
        }

        return new DimensionCombination(
        [
            .. deduplicated
                .Select(static kvp => new DimensionPair(kvp.Key, kvp.Value))
                .OrderBy(static p => p.DimensionId),
        ]);
    }

    /// <summary>
    /// Layers another combination over this one, with the other winning where both set the same
    /// dimension.
    /// </summary>
    /// <param name="overrides">The combination taking precedence.</param>
    /// <remarks>
    /// This is how dimensions actually flow through a document. The customer supplies defaults,
    /// the document header may override them, and an individual line may override again. Each
    /// step is one call, and the last word belongs to the most specific level.
    /// </remarks>
    public DimensionCombination OverrideWith(DimensionCombination overrides)
        => overrides.IsEmpty ? this : From([.. Pairs, .. overrides.Pairs]);

    /// <summary>Reads the value set for a dimension, if any.</summary>
    /// <param name="dimensionId">The dimension to look up.</param>
    public Guid? ValueOf(Guid dimensionId)
    {
        foreach (var pair in Pairs)
        {
            if (pair.DimensionId == dimensionId)
            {
                return pair.DimensionValueId;
            }
        }

        return null;
    }

    /// <summary>
    /// A stable 32-byte fingerprint of the combination, used as the unique key of the stored
    /// dimension set.
    /// </summary>
    /// <remarks>
    /// SHA-256 over the canonical text rather than the text itself, because a combination of
    /// eight dimensions runs to several hundred characters and would make a poor index key. The
    /// hash is chosen for its collision resistance, not for secrecy; nothing here is a secret.
    /// </remarks>
    public byte[] ComputeFingerprint()
        => SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalString()));

    /// <summary>
    /// The canonical text form, for example <c>a1b2..:c3d4..|e5f6..:g7h8..</c>. Stored alongside
    /// the fingerprint so a support engineer can read a dimension set without decoding a hash.
    /// </summary>
    public string ToCanonicalString()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var pair in Pairs)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            builder.Append(pair.DimensionId.ToString("N", CultureInfo.InvariantCulture))
                   .Append(':')
                   .Append(pair.DimensionValueId.ToString("N", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public bool Equals(DimensionCombination other) => Pairs.SequenceEqual(other.Pairs);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DimensionCombination other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);

        foreach (var pair in Pairs)
        {
            hash.Add(pair);
        }

        return hash.ToHashCode();
    }

    /// <summary>Compares two combinations by their pairs.</summary>
    public static bool operator ==(DimensionCombination left, DimensionCombination right) => left.Equals(right);

    /// <summary>Compares two combinations by their pairs.</summary>
    public static bool operator !=(DimensionCombination left, DimensionCombination right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => IsEmpty ? "(no dimensions)" : ToCanonicalString();
}

/// <summary>One dimension set to one of its values.</summary>
/// <param name="DimensionId">The dimension.</param>
/// <param name="DimensionValueId">The value it is set to.</param>
public readonly record struct DimensionPair(Guid DimensionId, Guid DimensionValueId);
