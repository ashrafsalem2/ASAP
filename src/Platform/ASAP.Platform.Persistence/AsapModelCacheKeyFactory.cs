using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ASAP.Platform.Persistence;

/// <summary>
/// Tells Entity Framework that two contexts with different modules installed are different models.
/// </summary>
/// <remarks>
/// <para>
/// The model is built from whichever module schemas the context was handed, and by default EF
/// caches it per context type without knowing that. The first context created in a process then
/// decides which entity types exist for every context after it, whatever they were given.
/// </para>
/// <para>
/// In a running host that is invisible, because every context gets the same modules. It shows up
/// the moment two differ, which is exactly what a modular monolith is supposed to allow: a host
/// running Finance and Inventory should not be handed a model built for one running Finance alone,
/// and a test covering two modules should not depend on which test ran first. The failure is
/// "cannot create a DbSet for X because this type is not included in the model", raised somewhere
/// that has nothing to do with the cause.
/// </para>
/// </remarks>
public sealed class AsapModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        var schemas = context is AsapDbContext asap ? asap.ModuleSchemaSignature : string.Empty;

        return (context.GetType(), schemas, designTime);
    }
}
