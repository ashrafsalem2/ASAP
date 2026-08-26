using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Dimensions;
using ASAP.Platform.Core.Events;
using ASAP.Platform.Core.Numbering;
using ASAP.Platform.Core.Security;
using ASAP.Platform.Core.Setup;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Kernel.Entities;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ASAP.Platform.Persistence;

/// <summary>
/// The one database context for the whole of ASAP: the platform and every loaded module.
/// </summary>
/// <remarks>
/// <para>
/// One context rather than one per module, because an ERP transaction crosses module boundaries
/// constantly. Posting a sales invoice writes a customer ledger entry, an item ledger entry, a
/// value entry and a general ledger entry, and either all four commit or none do. Splitting the
/// context per module would turn that into a distributed transaction to solve a problem nobody
/// has.
/// </para>
/// <para>
/// Modules stay isolated by convention rather than by connection: each registers its entities
/// through <see cref="IModuleSchema"/>, keeps them in its own schema, and never reads another
/// module tables.
/// </para>
/// </remarks>
/// <param name="options">Provider and connection configuration.</param>
/// <param name="tenantContext">Supplies the tenant and company the current request acts in.</param>
/// <param name="userContext">Supplies the user that audit stamping records.</param>
/// <param name="clock">Supplies the time that audit stamping records.</param>
/// <param name="moduleSchemas">Every loaded module that owns tables.</param>
public sealed class AsapDbContext(
    DbContextOptions<AsapDbContext> options,
    ITenantContext tenantContext,
    IUserContext userContext,
    IClock clock,
    IEnumerable<IModuleSchema> moduleSchemas) : DbContext(options)
{
    private readonly IEnumerable<IModuleSchema> _moduleSchemas = moduleSchemas;

    /// <summary>
    /// Tenant the query filters narrow to. Read as a property on each query rather than captured
    /// once, so one cached query plan serves every tenant.
    /// </summary>
    public Guid? CurrentTenantId => tenantContext.TenantId;

    /// <summary>Company the query filters narrow to.</summary>
    public Guid? CurrentCompanyId => tenantContext.CompanyId;

    /// <summary>
    /// Whether the filters are currently relaxed. True only for migrations, the seeder and
    /// deliberate cross-company consolidation, all of which run inside the platform.
    /// </summary>
    public bool IsCrossTenantOperation => tenantContext.IsCrossTenantOperation;

    /// <summary>ASAP subscribers.</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Legal entities.</summary>
    public DbSet<Company> Companies => Set<Company>();

    /// <summary>Shops, warehouses and offices.</summary>
    public DbSet<Branch> Branches => Set<Branch>();

    /// <summary>People who sign in.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Named bundles of permissions.</summary>
    public DbSet<PermissionSet> PermissionSets => Set<PermissionSet>();

    /// <summary>Permission keys inside a set.</summary>
    public DbSet<PermissionSetEntry> PermissionSetEntries => Set<PermissionSetEntry>();

    /// <summary>Sets included wholesale by other sets.</summary>
    public DbSet<PermissionSetInclusion> PermissionSetInclusions => Set<PermissionSetInclusion>();

    /// <summary>Who holds which set, where.</summary>
    public DbSet<UserPermissionAssignment> UserPermissionAssignments => Set<UserPermissionAssignment>();

    /// <summary>Live sign-in sessions, one row per rotation.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Settings that have been given a value.</summary>
    public DbSet<SetupValue> SetupValues => Set<SetupValue>();

    /// <summary>Sources of document numbers.</summary>
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();

    /// <summary>Dated ranges within a number series.</summary>
    public DbSet<NumberSeriesLine> NumberSeriesLines => Set<NumberSeriesLine>();

    /// <summary>Axes of analysis.</summary>
    public DbSet<Dimension> Dimensions => Set<Dimension>();

    /// <summary>Permitted values of a dimension.</summary>
    public DbSet<DimensionValue> DimensionValues => Set<DimensionValue>();

    /// <summary>Stored combinations of dimension values.</summary>
    public DbSet<DimensionSet> DimensionSets => Set<DimensionSet>();

    /// <summary>Values inside a stored combination.</summary>
    public DbSet<DimensionSetEntry> DimensionSetEntries => Set<DimensionSetEntry>();

    /// <summary>The append-only record of what was done.</summary>
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    /// <summary>Per-company transaction numbering.</summary>
    public DbSet<Core.Numbering.TransactionCounter> TransactionCounters => Set<Core.Numbering.TransactionCounter>();

    /// <summary>Integration events waiting to be delivered.</summary>
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("asap");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AsapDbContext).Assembly);

        // Modules register after the platform, so a module can relate to platform types such as
        // Branch or DimensionSet. They cannot relate to each other, by design.
        foreach (var schema in _moduleSchemas)
        {
            schema.Configure(modelBuilder);
        }

        modelBuilder.ApplyDecimalPrecision();

        // Last, so nothing registered above escapes the company filter -- including entities
        // brought by a third-party extension.
        modelBuilder.ApplyTenancyFilters(this);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Fills in tenancy and audit fields, and turns a delete into a soft delete where the entity
    /// supports one.
    /// </summary>
    /// <remarks>
    /// Done by overriding save rather than by an interceptor deliberately. An interceptor has to
    /// be registered, and a registration can be forgotten in one composition path and not
    /// another; the result would be rows written without a company, which the query filters would
    /// then hide from everyone. Overriding cannot be forgotten.
    /// </remarks>
    private void StampChanges()
    {
        var now = clock.UtcNow;
        var userId = userContext.UserId;

        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    StampTenancy(entry);
                    StampCreated(entry, now, userId);
                    break;

                case EntityState.Modified:
                    StampModified(entry, now, userId);
                    break;

                case EntityState.Deleted:
                    ConvertToSoftDelete(entry, now, userId);
                    break;
            }
        }
    }

    /// <summary>
    /// Stamps the active tenant and company on a new row.
    /// </summary>
    /// <remarks>
    /// Only fills a value that is still empty. A cross-company operation such as the seeder sets
    /// these explicitly, and must not have its work overwritten by whatever the ambient context
    /// happens to hold.
    /// </remarks>
    private void StampTenancy(EntityEntry entry)
    {
        if (entry.Entity is ITenantScoped tenantScoped && tenantScoped.TenantId == Guid.Empty)
        {
            tenantScoped.TenantId = CurrentTenantId ?? Guid.Empty;
        }

        if (entry.Entity is ICompanyScoped companyScoped && companyScoped.CompanyId == Guid.Empty)
        {
            companyScoped.CompanyId = CurrentCompanyId ?? Guid.Empty;
        }

        if (entry.Entity is IBranchScoped branchScoped && branchScoped.BranchId is null)
        {
            branchScoped.BranchId = tenantContext.BranchId;
        }
    }

    private static void StampCreated(EntityEntry entry, DateTime now, Guid? userId)
    {
        if (entry.Entity is not IAuditable auditable)
        {
            return;
        }

        auditable.CreatedAtUtc = now;
        auditable.CreatedBy ??= userId;
    }

    private static void StampModified(EntityEntry entry, DateTime now, Guid? userId)
    {
        if (entry.Entity is not IAuditable auditable)
        {
            return;
        }

        auditable.ModifiedAtUtc = now;
        auditable.ModifiedBy = userId;

        // A row created in this same save should not also claim to have been modified.
        entry.Property(nameof(IAuditable.CreatedAtUtc)).IsModified = false;
        entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
    }

    /// <summary>
    /// Turns a requested delete into a soft delete, where the entity allows one.
    /// </summary>
    /// <remarks>
    /// Master data is hidden rather than removed because posted history keeps pointing at it: an
    /// item withdrawn from sale must still resolve on last year invoices. Ledger entries do not
    /// implement <see cref="ISoftDeletable"/> at all, so a delete against one falls through to a
    /// real delete and is caught by the database, which has no cascade path to permit it.
    /// </remarks>
    private static void ConvertToSoftDelete(EntityEntry entry, DateTime now, Guid? userId)
    {
        if (entry.Entity is not ISoftDeletable deletable)
        {
            return;
        }

        entry.State = EntityState.Modified;
        deletable.IsDeleted = true;
        deletable.DeletedAtUtc = now;
        deletable.DeletedBy = userId;
    }
}
