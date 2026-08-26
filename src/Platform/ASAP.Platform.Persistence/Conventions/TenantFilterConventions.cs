using System.Reflection;
using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Platform.Persistence.Conventions;

/// <summary>
/// Applies the company isolation and soft-delete filters to every entity that qualifies.
/// </summary>
/// <remarks>
/// <para>
/// This is the security boundary of ASAP multi-company model, and the reason it is applied here
/// rather than left to each module is that a boundary a developer has to remember is a boundary
/// that eventually leaks. A module author writes <c>context.Items.Where(...)</c> and gets their
/// own company data because the model says so, not because they wrote the predicate correctly.
/// </para>
/// <para>
/// Filters are attached by scanning the finished model, so an entity added by a third-party
/// extension is covered on exactly the same terms as a built-in one. There is no opt-out short
/// of not implementing the interface.
/// </para>
/// </remarks>
public static class TenantFilterConventions
{
    private static readonly MethodInfo CompanyAndSoftDelete =
        Method(nameof(ApplyCompanyScopedSoftDeletable));

    private static readonly MethodInfo CompanyOnly =
        Method(nameof(ApplyCompanyScoped));

    private static readonly MethodInfo TenantAndSoftDelete =
        Method(nameof(ApplyTenantScopedSoftDeletable));

    private static readonly MethodInfo TenantOnly =
        Method(nameof(ApplyTenantScoped));

    private static readonly MethodInfo SoftDeleteOnly =
        Method(nameof(ApplySoftDeletable));

    /// <summary>
    /// Attaches a query filter to every entity implementing a tenancy or soft-delete interface.
    /// </summary>
    /// <param name="modelBuilder">The finished model.</param>
    /// <param name="context">The context whose ambient tenant the filters read.</param>
    /// <remarks>
    /// Must be called last, after the platform and every module have registered their entities,
    /// so nothing registered later escapes the scan.
    /// </remarks>
    public static void ApplyTenancyFilters(this ModelBuilder modelBuilder, AsapDbContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // An owned type is queried only through its owner, which already carries the filter.
            // Attaching one here would be rejected by EF as well as being redundant.
            if (entityType.IsOwned())
            {
                continue;
            }

            var isCompanyScoped = typeof(ICompanyScoped).IsAssignableFrom(clrType);
            var isTenantScoped = typeof(ITenantScoped).IsAssignableFrom(clrType);
            var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);

            var applier = (isCompanyScoped, isTenantScoped, isSoftDeletable) switch
            {
                (true, _, true) => CompanyAndSoftDelete,
                (true, _, false) => CompanyOnly,
                (false, true, true) => TenantAndSoftDelete,
                (false, true, false) => TenantOnly,
                (false, false, true) => SoftDeleteOnly,
                _ => null,
            };

            applier?.MakeGenericMethod(clrType).Invoke(null, [modelBuilder, context]);
        }
    }

    // Each filter reads the ambient values off the context as properties rather than capturing
    // them as constants. EF turns a property access on the context into a query parameter and
    // re-reads it on every execution, which is what lets one cached query plan serve every
    // company. Capturing the value instead would bake the first caller company into the plan.

    private static void ApplyCompanyScopedSoftDeletable<TEntity>(ModelBuilder modelBuilder, AsapDbContext context)
        where TEntity : class, ICompanyScoped, ISoftDeletable
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            context.IsCrossTenantOperation
            || (!e.IsDeleted
                && e.TenantId == context.CurrentTenantId
                && e.CompanyId == context.CurrentCompanyId));

    private static void ApplyCompanyScoped<TEntity>(ModelBuilder modelBuilder, AsapDbContext context)
        where TEntity : class, ICompanyScoped
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            context.IsCrossTenantOperation
            || (e.TenantId == context.CurrentTenantId && e.CompanyId == context.CurrentCompanyId));

    private static void ApplyTenantScopedSoftDeletable<TEntity>(ModelBuilder modelBuilder, AsapDbContext context)
        where TEntity : class, ITenantScoped, ISoftDeletable
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            context.IsCrossTenantOperation
            || (!e.IsDeleted && e.TenantId == context.CurrentTenantId));

    private static void ApplyTenantScoped<TEntity>(ModelBuilder modelBuilder, AsapDbContext context)
        where TEntity : class, ITenantScoped
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            context.IsCrossTenantOperation || e.TenantId == context.CurrentTenantId);

    private static void ApplySoftDeletable<TEntity>(ModelBuilder modelBuilder, AsapDbContext context)
        where TEntity : class, ISoftDeletable
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            context.IsCrossTenantOperation || !e.IsDeleted);

    private static MethodInfo Method(string name)
        => typeof(TenantFilterConventions).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException(
               $"{nameof(TenantFilterConventions)}.{name} is missing. The tenancy filters cannot be "
               + "applied, which would expose one company data to another, so the host must not start.");
}
