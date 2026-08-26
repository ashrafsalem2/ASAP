using System.Diagnostics.CodeAnalysis;

namespace ASAP.Platform.Kernel.Entities;

/// <summary>
/// Base class for every persisted ASAP entity.
/// </summary>
/// <remarks>
/// Keys are UUID v7, which sort by creation time. That keeps SQL Server's clustered index
/// appending at the end of the B-tree instead of fragmenting it the way random v4 GUIDs do —
/// which matters a great deal for ledger tables that grow to millions of rows.
/// </remarks>
public abstract class Entity : IEntity<Guid>, IEquatable<Entity>
{
    /// <summary>Primary key. Generated on construction so the value is known before saving.</summary>
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public object GetKey() => Id;

    /// <inheritdoc />
    public bool Equals(Entity? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Entities of different types never compare equal even if their keys collide.
        return GetType() == other.GetType() && Id != Guid.Empty && Id == other.Id;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <summary>Compares two entities by type and key.</summary>
    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    /// <summary>Compares two entities by type and key.</summary>
    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);

    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public override string ToString() => $"{GetType().Name} {Id}";
}
