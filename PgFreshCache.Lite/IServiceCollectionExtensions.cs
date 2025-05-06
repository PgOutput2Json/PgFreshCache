using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PgFreshCache.Lite;
using PgOutput2Json.Sqlite;

namespace Microsoft.Extensions.DependencyInjection;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddPgFreshCache<TDbContext>(this IServiceCollection services,
                                                                 string cacheKey,
                                                                 string postgresConnectionString,
                                                                 string publicationName,
                                                                 Action<PgFreshCacheOptions>? cacheOptionsAction = null,
                                                                 Action<DbContextOptionsBuilder>? sqliteOptionsAction = null)
        where TDbContext : DbContext
    {
        var cacheOptions = new PgFreshCacheOptions
        {
            PostgresConnectionString = postgresConnectionString,
            SqliteConnectionString = $"Data Source=PgFreshCache_{cacheKey};Mode=Memory;Cache=Shared;Foreign Keys=False",
            ReplicationSlotName = $"PgFreshCache_{Guid.NewGuid()}".Replace("-", ""),
            PublicationNames = [publicationName],
            UseTemporaryReplicationSlot = true,

            UseWal = true,
            WalCheckpointTryCount = 10,
            WalCheckpointType = WalCheckpointType.Full,
        };

        cacheOptionsAction?.Invoke(cacheOptions);

        var sqliteOptionsBuilder = new DbContextOptionsBuilder<TDbContext>()
            .UseSqlite(cacheOptions.SqliteConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution)
            .AddInterceptors(new PgFreshCacheConnectionInterceptor());

        sqliteOptionsAction?.Invoke(sqliteOptionsBuilder);

        // register services 
        services.AddKeyedSingleton(cacheKey, cacheOptions);

        services.AddKeyedSingleton<PgFreshCacheAccessor<TDbContext>>(cacheKey);

        services.AddKeyedSingleton<IDbContextFactory<TDbContext>>(cacheKey, (svc, key) => 
            new PgFreshCacheDbContextFactory<TDbContext>(svc, sqliteOptionsBuilder.Options));

        // created with factory, so it does not confuse the regular DbContextOptions<TDbContext> with cache DbContext options
        services.AddKeyedScoped(cacheKey, (svc, key) => 
            svc.GetRequiredKeyedService<IDbContextFactory<TDbContext>>(key).CreateDbContext());

        // If we want multiple instances running in parallel (for different keys), we should call AddSingleton<IHostedService>().
        // https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.servicecollectionhostedserviceextensions.addhostedservice
        services.AddSingleton<IHostedService, PgFreshCacheWorker<TDbContext>>(svc =>
        {
            return new PgFreshCacheWorker<TDbContext>(
                svc.GetRequiredKeyedService<PgFreshCacheOptions>(cacheKey),
                svc.GetRequiredKeyedService<PgFreshCacheAccessor<TDbContext>>(cacheKey),
                svc.GetRequiredKeyedService<IDbContextFactory<TDbContext>>(cacheKey), 
                svc.GetRequiredService<ILoggerFactory>(),
                svc.GetRequiredService<ILogger<PgFreshCacheWorker<TDbContext>>>()
            );
        });

        return services;
    }
}
