using ASAP.Platform.Core.Events;
using ASAP.Platform.Kernel.Cqrs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ASAP.Platform.Persistence;

/// <summary>Registers the ASAP persistence layer.</summary>
public static class AsapPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database context and everything that depends on it.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="connectionString">Connection to the ASAP database.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddAsapPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AsapDbContext>((provider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable("__AsapMigrations", "asap");

                // A branch talking to head office over a consumer link drops connections that a
                // datacentre never would. Retrying transient failures is the difference between
                // a till that hiccups and a till that stops.
                sql.EnableRetryOnFailure(maxRetryCount: 5, TimeSpan.FromSeconds(10), errorNumbersToAdd: null);

                sql.CommandTimeout(60);
            });
        });

        services.TryAddScoped<IOutboxWriter, OutboxWriter>();
        services.TryAddScoped<ASAP.Platform.Kernel.Setup.ISetupService, SetupService>();
        services.TryAddScoped<ASAP.Platform.Core.Security.IUserPermissionSource, UserPermissionSource>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}
