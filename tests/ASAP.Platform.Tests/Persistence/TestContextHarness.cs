using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Platform.Tests.Persistence;

/// <summary>
/// A tenant context whose values can be moved between calls, so one test can act first as one
/// company and then as another against the same database.
/// </summary>
internal sealed class MutableTenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }

    public Guid? CompanyId { get; set; }

    public Guid? BranchId { get; set; }

    public bool IsCrossTenantOperation { get; set; }

    public Guid RequireTenantId()
        => TenantId ?? throw new InvalidOperationException("No tenant on the current request.");

    public Guid RequireCompanyId()
        => CompanyId ?? throw new InvalidOperationException("No company selected.");
}

/// <summary>A user context with fixed values.</summary>
internal sealed class StubUserContext : IUserContext
{
    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public string? DisplayName { get; set; }

    public string? Culture { get; set; }

    public bool IsSuperUser { get; set; }

    public IReadOnlySet<string> Permissions { get; set; } = new HashSet<string>();

    public bool Has(string permissionKey) => IsSuperUser || Permissions.Contains(permissionKey);

    public Guid RequireUserId()
        => UserId ?? throw new InvalidOperationException("No user on the current request.");
}

/// <summary>A clock frozen at a known instant, so audit stamps are assertable.</summary>
internal sealed class FrozenClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;

    public DateOnly Today => DateOnly.FromDateTime(UtcNow);
}

/// <summary>
/// Builds contexts over one shared in-memory database, so several contexts can be opened against
/// the same data while acting as different tenants.
/// </summary>
/// <remarks>
/// The in-memory provider is the right tool here specifically because these tests are about
/// query filters and change tracking, both of which sit above the provider. Nothing here asserts
/// on SQL, indexes or column types; those belong to a migration test against real SQL Server.
/// </remarks>
internal sealed class TestContextHarness : IDisposable
{
    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly List<AsapDbContext> _opened = [];

    public TestContextHarness()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-tests-{Guid.CreateVersion7()}")
            // The filters read the tenant off the context on every query. Warning about it would
            // be right for a value captured once; here it is exactly the intended behaviour.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .EnableSensitiveDataLogging()
            .Options;
    }

    public MutableTenantContext Tenancy { get; } = new();

    public StubUserContext User { get; } = new();

    public FrozenClock Clock { get; } = new(new DateTime(2026, 8, 26, 9, 30, 0, DateTimeKind.Utc));

    /// <summary>
    /// Opens a context sharing the harness database and ambient tenant.
    /// </summary>
    /// <remarks>
    /// Every context shares one <see cref="MutableTenantContext"/> instance, which is what makes
    /// the model-caching question testable: EF builds and caches the model once, so if the
    /// filters had baked in the first tenant, a later context acting as a different one would
    /// still return the first tenant rows.
    /// </remarks>
    public AsapDbContext NewContext() => NewContext(null);

    /// <summary>
    /// Opens a context that also captures sync changes for whatever the registry publishes.
    /// </summary>
    /// <param name="syncRegistry">What branches hold a copy of, or null to capture nothing.</param>
    public AsapDbContext NewContext(ASAP.Platform.Core.Sync.SyncRegistry? syncRegistry)
    {
        var context = new AsapDbContext(_options, Tenancy, User, Clock, [], syncRegistry);
        _opened.Add(context);
        return context;
    }

    /// <summary>Runs an action as a cross-tenant operation, the way the seeder does.</summary>
    public void AsSystem(Action<AsapDbContext> action)
    {
        var wasCrossTenant = Tenancy.IsCrossTenantOperation;
        Tenancy.IsCrossTenantOperation = true;

        try
        {
            using var context = new AsapDbContext(_options, Tenancy, User, Clock, []);
            action(context);
        }
        finally
        {
            Tenancy.IsCrossTenantOperation = wasCrossTenant;
        }
    }

    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }
}
