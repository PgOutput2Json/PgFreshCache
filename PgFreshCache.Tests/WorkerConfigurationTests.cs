using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PgFreshCache.Tests
{
    /// <summary>
    /// A misconfigured cache must not crash the host: the worker logs an error and stops, without connecting to PostgreSQL.
    /// </summary>
    [TestClass]
    public class WorkerConfigurationTests
    {
        // nothing listens there - a worker that tried to connect would keep retrying instead of stopping
        private const string UnreachablePg = "Host=127.0.0.1;Port=1;Timeout=1";

        [TestMethod]
        public async Task Missing_connection_string_stops_the_worker()
        {
            await AssertWorkerStopsWithErrorAsync(o => { }, "Missing PostgreSQL connection string");
        }

        [TestMethod]
        public async Task Missing_publications_stop_the_worker()
        {
            await AssertWorkerStopsWithErrorAsync(o => o.UseConnectionString(UnreachablePg).UsePublications(), "Missing PostgreSQL publication name");
        }

        [TestMethod]
        public async Task In_memory_database_without_shared_cache_stops_the_worker()
        {
            await AssertWorkerStopsWithErrorAsync(o => o
                .UseConnectionString(UnreachablePg)
                .UseSqlite("Data Source=not_shared;Mode=Memory"), "not configured with shared cache");
        }

        private static async Task AssertWorkerStopsWithErrorAsync(Action<PgFreshCache.Lite.PgFreshCacheOptionsBuilder<ItemsContext>> configure, string expectedError)
        {
            var logs = new LogCollector();
            var services = Services.Create(logs);
            services.AddPgFreshCache<ItemsContext>(Services.NewKey(), configure);

            using var sp = services.BuildServiceProvider();
            var worker = (BackgroundService)sp.GetServices<IHostedService>().Single();

            await worker.StartAsync(CancellationToken.None);

            try
            {
                var stopped = await Task.WhenAny(worker.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(10)));

                Assert.AreSame(worker.ExecuteTask, stopped, "The worker did not stop");
                Assert.IsTrue(worker.ExecuteTask!.IsCompletedSuccessfully, worker.ExecuteTask.Exception?.ToString());
                Assert.IsTrue(logs.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains(expectedError)),
                    $"Expected an error containing '{expectedError}', got:\n{string.Join("\n", logs.Entries)}");
            }
            finally
            {
                await worker.StopAsync(CancellationToken.None);
            }
        }
    }
}
