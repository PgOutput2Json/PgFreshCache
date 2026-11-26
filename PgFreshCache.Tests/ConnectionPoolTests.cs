using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PgFreshCache.Tests
{
    /// <summary>
    /// The cache context makes its connections read-only (PRAGMA query_only), and SQLite connections are pooled by their
    /// connection string: a connection the cache context used must not become one the worker writes with.
    /// </summary>
    [TestClass]
    public class ConnectionPoolTests
    {
        // nothing listens there: the worker creates the SQLite tables, then fails to connect and retries
        private const string UnreachablePg = "Host=127.0.0.1;Port=1;Timeout=1";

        [TestMethod]
        [DataRow(false, DisplayName = "in memory")]
        [DataRow(true, DisplayName = "file")]
        public async Task The_worker_creates_the_tables_after_the_cache_was_queried(bool useFile)
        {
            var key = Services.NewKey();
            var file = Path.Combine(Path.GetTempPath(), $"pgfreshcache_{Guid.NewGuid():N}.db");

            var logs = new LogCollector();
            var services = Services.Create(logs);
            services.AddPgFreshCache<ItemsContext>(key, o =>
            {
                o.UseConnectionString(UnreachablePg);
                if (useFile) o.UseSqlite($"Data Source={file}");
            });

            try
            {
                using var sp = services.BuildServiceProvider();

                var connectionString = sp.GetRequiredKeyedService<PgFreshCache.Lite.PgFreshCacheOptions>(key).SqliteConnectionString;

                // an application that reads the cache before the worker has started: the query fails (no tables yet),
                // and its read-only connection goes back to the pool
                using (var scope = sp.CreateScope())
                {
                    var cache = scope.ServiceProvider.GetRequiredKeyedService<ItemsContext>(key);

                    await Assert.ThrowsExactlyAsync<SqliteException>(() => cache.Items.CountAsync());
                }

                // in memory, the database lives while a connection is open: this one is not pooled, so it is not the one reused
                using var check = new SqliteConnection(new SqliteConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
                await check.OpenAsync();

                var worker = (BackgroundService)sp.GetServices<IHostedService>().Single();
                await worker.StartAsync(CancellationToken.None);

                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(10);

                    while (!await TableExistsAsync(check, "items") && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(50);
                    }

                    Assert.IsTrue(await TableExistsAsync(check, "items"),
                        $"The worker did not create the tables:\n{string.Join("\n", logs.Entries.Where(e => e.Level >= LogLevel.Warning))}");
                }
                finally
                {
                    await worker.StopAsync(CancellationToken.None);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();

                foreach (var f in new[] { file, file + "-wal", file + "-shm", file + "-journal" })
                {
                    if (File.Exists(f)) File.Delete(f);
                }
            }
        }

        private static async Task<bool> TableExistsAsync(SqliteConnection cn, string table)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
            cmd.Parameters.AddWithValue("$name", table);

            return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
        }
    }
}
