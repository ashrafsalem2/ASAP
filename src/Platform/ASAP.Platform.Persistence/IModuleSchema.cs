using Microsoft.EntityFrameworkCore;

namespace ASAP.Platform.Persistence;

/// <summary>
/// Implemented by a module that brings tables of its own.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately separate from <see cref="Kernel.Modules.IAsapModule"/>. A module that
/// only adds behaviour -- an extension subscribing to posting events, say -- implements the
/// module interface alone and never references Entity Framework. Only a module that owns
/// storage takes on this one.
/// </para>
/// <para>
/// The platform calls <see cref="Configure"/> during model building, after it has registered its
/// own entities, so a module can relate to platform types. It must not touch another module
/// entities: modules meet through events and kernel contracts, never through a shared table.
/// </para>
/// </remarks>
public interface IModuleSchema
{
    /// <summary>
    /// Registers the module entities on the model.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <remarks>
    /// Company filters and audit stamping are applied afterwards, automatically, to anything
    /// implementing the relevant kernel interfaces. A module does not write those itself, and
    /// cannot opt out of them by forgetting to.
    /// </remarks>
    void Configure(ModelBuilder modelBuilder);

    /// <summary>
    /// Schema name the module tables live under, for example <c>fin</c>. Keeps a hundred tables
    /// from landing in one flat namespace, and makes it obvious in the database which module owns
    /// what. Null puts them in the default schema.
    /// </summary>
    string? SchemaName => null;
}
