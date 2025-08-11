using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PgFreshCache.Lite;

namespace Microsoft.Extensions.DependencyInjection;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddPgFreshCache<TDbContext>(this IServiceCollection services,
                                                                 string cacheKey,
                                                                 Action<PgFreshCacheOptionsBuilder<TDbContext>>? cacheOptionsAction = null)
        where TDbContext : DbContext
    {
        var optionsBuilder = new PgFreshCacheOptionsBuilder<TDbContext>(cacheKey);

        cacheOptionsAction?.Invoke(optionsBuilder);

        // register services 
        services.AddKeyedSingleton(cacheKey, optionsBuilder.Options);

        services.AddKeyedSingleton<IDbContextFactory<TDbContext>>(cacheKey, (svc, key) => 
            new PgFreshCacheDbContextFactory<TDbContext>(svc, optionsBuilder.SqliteOptionsBuilder.Options));

        // created with factory, so it does not confuse the regular DbContextOptions<TDbContext> with cache DbContext options
        services.AddKeyedScoped(cacheKey, (svc, key) => 
            svc.GetRequiredKeyedService<IDbContextFactory<TDbContext>>(key).CreateDbContext());

        // If we want multiple instances running in parallel (for different keys), we should call AddSingleton<IHostedService>().
        // https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.servicecollectionhostedserviceextensions.addhostedservice
        services.AddSingleton<IHostedService, PgFreshCacheWorker<TDbContext>>(svc =>
        {
            return new PgFreshCacheWorker<TDbContext>(
                svc.GetRequiredKeyedService<PgFreshCacheOptions>(cacheKey),
                svc.GetRequiredKeyedService<IDbContextFactory<TDbContext>>(cacheKey), 
                svc.GetRequiredService<ILoggerFactory>(),
                svc.GetRequiredService<ILogger<PgFreshCacheWorker<TDbContext>>>()
            );
        });

        return services;
    }
}
