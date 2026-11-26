using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PgFreshCache.Lite;
using Testcontainers.PostgreSql;

[assembly: DoNotParallelize]

namespace PgFreshCache.IntegrationTests
{
    [Table("products")]
    public class Product
    {
        [Column("id")] public int Id { get; set; }
        [Column("name")] public string Name { get; set; } = "";
        [Column("price")] public decimal Price { get; set; }
        [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
        [Column("local_at")] public DateTime LocalAt { get; set; }
        [Column("active")] public bool Active { get; set; }
        [Column("note")] public string? Note { get; set; }
    }

    [Table("categories")]
    public class Category
    {
        [Column("id")] public int Id { get; set; }
        [Column("name")] public string Name { get; set; } = "";
    }

    /// <summary>
    /// A table of another schema than public: SQLite has no schemas, so in the cache it is the table "orders".
    /// </summary>
    [Table("orders", Schema = "sales")]
    public class Order
    {
        [Column("id")] public int Id { get; set; }
        [Column("customer")] public string Customer { get; set; } = "";
    }

    public class CacheContext(DbContextOptions<CacheContext> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Order> Orders => Set<Order>();
    }

    /// <summary>
    /// One PostgreSQL container for the whole run; every test gets its own database in it.
    /// </summary>
    [TestClass]
    public static class Containers
    {
        private static PostgreSqlContainer? _postgres;

        /// <summary>
        /// The PostgreSQL image to test against, e.g. PG_IMAGE=postgres:14.
        /// </summary>
        public static string PgImage => Environment.GetEnvironmentVariable("PG_IMAGE") is { Length: > 0 } image ? image : "postgres:18";

        public static string PgConnectionString => _postgres!.GetConnectionString();

        [AssemblyInitialize]
        public static async Task StartAsync(TestContext _)
        {
            _postgres = new PostgreSqlBuilder(PgImage)
                .WithCommand("-c", "wal_level=logical", "-c", "max_replication_slots=50", "-c", "max_wal_senders=50")
                .Build();

            await _postgres.StartAsync();
        }

        [AssemblyCleanup]
        public static async Task StopAsync()
        {
            if (_postgres != null) await _postgres.DisposeAsync();
        }
    }

    /// <summary>
    /// A fresh database with the products and categories tables, and sales.orders.
    /// </summary>
    internal sealed class TestDatabase
    {
        public string ConnectionString { get; }

        private TestDatabase(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var name = $"db_{Guid.NewGuid():N}";

            await using (var cn = new NpgsqlConnection(Containers.PgConnectionString))
            {
                await cn.OpenAsync();
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", cn);
                await cmd.ExecuteNonQueryAsync();
            }

            var db = new TestDatabase(new NpgsqlConnectionStringBuilder(Containers.PgConnectionString) { Database = name }.ConnectionString);

            await db.ExecuteAsync(@"
                CREATE TABLE products (
                    id int PRIMARY KEY,
                    name text NOT NULL,
                    price numeric(10,2) NOT NULL,
                    created_at timestamptz NOT NULL,
                    local_at timestamp NOT NULL,
                    active boolean NOT NULL,
                    note text NULL);

                CREATE TABLE categories (id int PRIMARY KEY, name text NOT NULL);

                CREATE SCHEMA sales;
                CREATE TABLE sales.orders (id int PRIMARY KEY, customer text NOT NULL);");

            return db;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var cn = new NpgsqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, cn);
            await cmd.ExecuteNonQueryAsync();
        }

        public Task InsertProductAsync(int id, string name, decimal price = 1m)
        {
            return ExecuteAsync($"INSERT INTO products VALUES ({id}, '{name}', {price.ToString(System.Globalization.CultureInfo.InvariantCulture)}, now(), now(), true, NULL)");
        }
    }

    /// <summary>
    /// An application with one or more caches: the DI container with PgFreshCache registered and its workers running.
    /// </summary>
    internal sealed class CacheApp : IAsyncDisposable
    {
        private readonly ServiceProvider _sp;
        private readonly List<IHostedService> _workers;

        private CacheApp(ServiceProvider sp)
        {
            _sp = sp;
            _workers = sp.GetServices<IHostedService>().ToList();
        }

        public static async Task<CacheApp> StartAsync(Action<IServiceCollection> configure)
        {
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));

            configure(services);

            var app = new CacheApp(services.BuildServiceProvider());

            foreach (var worker in app._workers) await worker.StartAsync(CancellationToken.None);

            return app;
        }

        public static Task<CacheApp> StartAsync(string key, Action<PgFreshCacheOptionsBuilder<CacheContext>> configure)
        {
            return StartAsync(services => services.AddPgFreshCache(key, configure));
        }

        public async Task<T> QueryAsync<T>(string key, Func<CacheContext, Task<T>> query)
        {
            using var scope = _sp.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredKeyedService<CacheContext>(key));
        }

        /// <summary>
        /// Polls the cache until the query result satisfies the condition. The cache tables do not exist
        /// until the worker has created them, so errors are retried too.
        /// </summary>
        public async Task<T> WaitForAsync<T>(string key, Func<CacheContext, Task<T>> query, Func<T, bool> condition, string what, int timeoutSeconds = 60)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            Exception? lastError = null;
            T? last = default;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    last = await QueryAsync(key, query);
                    if (condition(last)) return last;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                await Task.Delay(200);
            }

            var message = $"Timed out waiting for {what}. Last result: {Describe(last)}";

            throw lastError == null ? new AssertFailedException(message) : new AssertFailedException(message, lastError);
        }

        private static string Describe(object? value)
        {
            return value is System.Collections.IEnumerable list and not string
                ? $"[{string.Join(", ", list.Cast<object>())}]"
                : value?.ToString() ?? "null";
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var worker in _workers) await worker.StopAsync(CancellationToken.None);

            await _sp.DisposeAsync();
        }
    }
}
