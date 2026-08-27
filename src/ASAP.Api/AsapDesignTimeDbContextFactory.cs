using ASAP.Modules.Finance;
using ASAP.Platform.Core.Time;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ASAP.Api;

/// <summary>
/// Builds a context for the EF Core command-line tools, which need one without a running host.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the API host rather than in the persistence layer for a reason worth stating.
/// Migrations must cover every table ASAP ships with, which means the model has to be built with
/// every module's schema registered -- and only the host knows what those are. Persistence cannot
/// reference Finance without inverting the dependency the whole architecture rests on.
/// </para>
/// <para>
/// So migrations are generated against the host and written into the persistence assembly:
/// </para>
/// <code>
/// dotnet ef migrations add Name --project src/Platform/ASAP.Platform.Persistence --startup-project src/ASAP.Api
/// </code>
/// <para>
/// A third-party extension that brings its own tables is not covered by this, and cannot be: its
/// assembly is not present when migrations are generated. That is a known gap, and the plan for
/// it is a per-extension schema step run at install time rather than a shared migration history.
/// </para>
/// </remarks>
public sealed class AsapDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AsapDbContext>
{
    /// <summary>
    /// Connection used at design time only. Points at LocalDB, holds no credentials, and is
    /// overridden by the <c>ASAP_DESIGN_CONNECTION</c> environment variable where a developer
    /// keeps their database somewhere else.
    /// </summary>
    private const string DesignTimeConnection =
        "Server=(localdb)\\MSSQLLocalDB;Database=AsapErp;Trusted_Connection=True;TrustServerCertificate=True";

    /// <inheritdoc />
    public AsapDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ASAP_DESIGN_CONNECTION")
                         ?? DesignTimeConnection;

        var options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseSqlServer(connection, sql =>
            {
                sql.MigrationsHistoryTable("__AsapMigrations", "asap");
                sql.MigrationsAssembly(typeof(AsapDbContext).Assembly.FullName);
            })
            .Options;

        return new AsapDbContext(
            options,
            new DesignTimeTenantContext(),
            new DesignTimeUserContext(),
            new SystemClock(),
            AsapModules.Schemas);
    }

    /// <summary>
    /// A tenant context for schema generation. Reports a cross-tenant operation so the query
    /// filters narrow nothing the tools inspect.
    /// </summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;

        public Guid? CompanyId => null;

        public Guid? BranchId => null;

        public bool IsCrossTenantOperation => true;

        public Guid RequireTenantId()
            => throw new NotSupportedException("There is no tenant at design time.");

        public Guid RequireCompanyId()
            => throw new NotSupportedException("There is no company at design time.");
    }

    /// <summary>An empty user context. Nothing at design time writes rows that need stamping.</summary>
    private sealed class DesignTimeUserContext : IUserContext
    {
        public Guid? UserId => null;

        public string? UserName => null;

        public string? DisplayName => null;

        public string? Culture => null;

        public bool IsSuperUser => false;

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();

        public bool Has(string permissionKey) => false;

        public Guid RequireUserId()
            => throw new NotSupportedException("There is no user at design time.");
    }
}

/// <summary>
/// The modules this build of ASAP ships with.
/// </summary>
/// <remarks>
/// One list, read by both the running host and the migration tooling, so a module added here
/// cannot be present at runtime and missing from the schema.
/// </remarks>
public static class AsapModules
{
    /// <summary>Every built-in module, in any order. The catalogue sorts them by dependency.</summary>
    public static IReadOnlyList<Platform.Kernel.Modules.IAsapModule> BuiltIn { get; } =
    [
        new Platform.Core.Modules.PlatformModule(),
        new FinanceModule(),
        new Modules.Inventory.InventoryModule(),
        new Modules.Purchasing.PurchasingModule(),
    ];

    /// <summary>Every built-in module that owns tables.</summary>
    public static IReadOnlyList<IModuleSchema> Schemas { get; } =
    [
        new FinanceSchema(),
        new Modules.Inventory.InventorySchema(),
        new Modules.Purchasing.PurchasingSchema(),
    ];
}
