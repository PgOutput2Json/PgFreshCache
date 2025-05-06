using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;
using System.Reflection;

namespace PgFreshCache.Lite
{
    internal sealed class PgFreshCacheDbContextFactory<TDbContext> : IDbContextFactory<TDbContext>
        where TDbContext : DbContext
    {
        private static readonly Func<IServiceProvider, DbContextOptions<TDbContext>, TDbContext> _factory;

        static PgFreshCacheDbContextFactory()
        {
            _factory = CreateActivator();
        }

        private readonly IServiceProvider _serviceProvider;
        private readonly DbContextOptions<TDbContext> _options;

        public PgFreshCacheDbContextFactory(IServiceProvider serviceProvider, DbContextOptions<TDbContext> options)
        {
            _serviceProvider = serviceProvider;
            _options = options;
        }

        public TDbContext CreateDbContext()
        {
            return _factory(_serviceProvider, _options);
        }

        private static Func<IServiceProvider, DbContextOptions<TDbContext>, TDbContext> CreateActivator()
        {
            var constructors
                = typeof(TDbContext).GetTypeInfo().DeclaredConstructors
                    .Where(c => c is { IsStatic: false, IsPublic: true } && c.GetParameters().Length != 0)
                    .ToArray();

            if (constructors.Length == 1)
            {
                var parameters = constructors[0].GetParameters();

                if (parameters.Length == 1)
                {
                    var isGeneric = parameters[0].ParameterType == typeof(DbContextOptions<TDbContext>);
                    if (isGeneric
                        || parameters[0].ParameterType == typeof(DbContextOptions))
                    {
                        var optionsParam = Expression.Parameter(typeof(DbContextOptions<TDbContext>), "options");
                        var providerParam = Expression.Parameter(typeof(IServiceProvider), "provider");

                        return Expression.Lambda<Func<IServiceProvider, DbContextOptions<TDbContext>, TDbContext>>(
                                Expression.New(
                                    constructors[0],
                                    isGeneric
                                        ? optionsParam
                                        : Expression.Convert(optionsParam, typeof(DbContextOptions))),
                                providerParam, optionsParam)
                            .Compile();
                    }
                }
            }

            var factory = ActivatorUtilities.CreateFactory(typeof(TDbContext), Type.EmptyTypes);

            return (p, _) => (TDbContext)factory(p, null);
        }
    }
}
