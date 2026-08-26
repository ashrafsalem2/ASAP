using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ASAP.Platform.Persistence;

/// <summary>
/// Builds a context for the EF Core command-line tools, which need one without a running host.
/// </summary>
/// <remarks>
/// <para>
/// Used only by <c>dotnet ef</c> when adding migrations or producing a script. It never runs in
/// the application, which builds its context from the container with the real tenant, user and
/// clock behind it.
/// </para>
/// <para>
/// The context it builds carries no module schemas, so migrations generated here cover the
/// platform tables alone. That is intentional: each module owns its own migrations, in its own
/// assembly, so a customer who has not bought Payroll does not carry its tables.
/// </para>
/// </remarks>
public sealed class AsapDbContextFactory : IDesignTimeDbContextFactory<AsapDbContext>
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
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__AsapMigrations", "asap"))
            .Options;

        return new AsapDbContext(
            options,
            new DesignTimeTenantContext(),
            new DesignTimeUserContext(),
            new SystemClock(),
            moduleSchemas: []);
    }

    /// <summary>
    /// A tenant context for schema generation. Reports a cross-tenant operation so the query
    /// filters do not narrow anything the tools inspect.
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
/// The ordinary clock, reading the machine time.
/// </summary>
/// <param name="timeZoneId">
/// IANA time zone that decides what "today" means. Defaults to Riyadh, which is where the first
/// deployment runs; a tenant in another zone overrides it through its own setup.
/// </param>
public sealed class SystemClock(string timeZoneId = "Asia/Riyadh") : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    public DateOnly Today
    {
        get
        {
            // A posting date is a calendar date, not an instant. At 02:00 in Riyadh it is still
            // yesterday in UTC, and defaulting a posting date from UTC would put the entry in the
            // wrong day -- and at a month boundary, in the wrong period.
            var zone = ResolveTimeZone(timeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // A misconfigured zone must not take the system down; UTC is a defensible fallback
            // and the misconfiguration will show up as an off-by-hours posting date.
            return TimeZoneInfo.Utc;
        }
    }
}
