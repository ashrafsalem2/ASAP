namespace ASAP.Platform.Kernel.Entities;

/// <summary>Marker for anything the persistence layer treats as a stored entity.</summary>
public interface IEntity
{
    /// <summary>The primary key, boxed so infrastructure can read it without knowing the key type.</summary>
    object GetKey();
}

/// <summary>An entity with a strongly typed primary key.</summary>
/// <typeparam name="TKey">The key type.</typeparam>
public interface IEntity<out TKey> : IEntity
    where TKey : notnull
{
    /// <summary>The primary key.</summary>
    TKey Id { get; }
}
