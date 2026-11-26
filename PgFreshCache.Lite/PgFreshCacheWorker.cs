using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PgOutput2Json;
using PgOutput2Json.Sqlite;

namespace PgFreshCache.Lite
{
    internal sealed class PgFreshCacheWorker<TDbContext> : BackgroundService
        where TDbContext: DbContext
    {
        private readonly PgFreshCacheOptions _options;
        private readonly IDbContextFactory<TDbContext> _dbContextFactory;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<PgFreshCacheWorker<TDbContext>> _logger;

        public PgFreshCacheWorker(PgFreshCacheOptions options,
                                  IDbContextFactory<TDbContext> dbContextFactory,
                                  ILoggerFactory loggerFactory,
                                  ILogger<PgFreshCacheWorker<TDbContext>> logger)
        {
            _options = options;
            _dbContextFactory = dbContextFactory;
            _loggerFactory = loggerFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(_options.PostgresConnectionString))
            {
                _logger.LogError("Missing PostgreSQL connection string for cache replication. Aborting PgFreshCache start");
                return;
            }

            if (_options.PublicationNames.Count == 0)
            {
                _logger.LogError("Missing PostgreSQL publication name for cache replication. Aborting PgFreshCache start");
                return;
            }

            SqliteConnection? memoryConnection = null; // keep the connection open while the app is running

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // the connections that write (EnsureCreated, PgOutput2Json, and the one that keeps an in-memory database) are
                    // not pooled: the cache DbContext makes its connections read-only (PRAGMA query_only), and a pooled one,
                    // opened by a query before the worker started, would be reused here and fail every write
                    var cnStringBuilder = new SqliteConnectionStringBuilder(_options.SqliteConnectionString) { Pooling = false };

                    if (PgFreshCacheOptions.IsMemoryConnection(cnStringBuilder))
                    {
                        if (cnStringBuilder.Cache != SqliteCacheMode.Shared)
                        {
                            _logger.LogError("In-Memory Sqlite database is not configured with shared cache. Aborting PgFreshCache start");
                            break;
                        }

                        memoryConnection = await EnsureMemoryConnectionAsync(memoryConnection, cnStringBuilder, stoppingToken).ConfigureAwait(false);
                    }

                    await EnsureDatabaseCreatedAsync(cnStringBuilder, stoppingToken).ConfigureAwait(false);

                    var builder = PgOutput2JsonBuilder.Create()
                        .WithLoggerFactory(_loggerFactory)
                        .WithPgConnectionString(_options.PostgresConnectionString)
                        .WithPgPublications([.. _options.PublicationNames])
                        .WithPgReplicationSlot(_options.ReplicationSlotName, _options.UseTemporaryReplicationSlot)
                        .WithInitialDataCopy(true)
                        .UseSqlite(options => ConfigureSqlite(options, _options, cnStringBuilder));

                    using var pgOutput2Json = builder.Build();

                    await pgOutput2Json.StartAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Failed to start PgFreshCache worker. Retrying in 10 seconds...");

                        // don't throw on shutdown, so the loop exits normally and the memory connection gets disposed
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    }
                }
            }

            memoryConnection?.Dispose();
        }

        internal static void ConfigureSqlite(SqlitePublisherOptions options, PgFreshCacheOptions cacheOptions, SqliteConnectionStringBuilder cnStringBuilder)
        {
            options.ConnectionStringBuilder = cnStringBuilder;
            options.UseWal = cacheOptions.UseWal;
            options.WalCheckpointTryCount = cacheOptions.WalCheckpointTryCount;
            options.WalCheckpointType = cacheOptions.WalCheckpointType;

            // the store keeps its position under this name: a cache has one source and one slot
            options.SubscriptionName = cacheOptions.ReplicationSlotName;

            // the tables are created from the DbContext model (EnsureCreated), and SQLite has no schemas: the EF provider
            // ignores the schema of an entity, so its table is the table name alone, and the rows must go there.
            // Two published tables with the same name in different schemas are an error of PgOutput2Json
            options.GetTableName = (schemaName, tableName) => tableName;
        }

        private async Task EnsureDatabaseCreatedAsync(SqliteConnectionStringBuilder cnStringBuilder, CancellationToken stoppingToken)
        {
            using var db = await _dbContextFactory.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);

            using var connection = new SqliteConnection(cnStringBuilder.ConnectionString);
            
            await connection.OpenAsync(stoppingToken).ConfigureAwait(false);

            db.Database.SetDbConnection(connection);

            await db.Database.EnsureCreatedAsync(stoppingToken).ConfigureAwait(false);
        }

        private static async Task<SqliteConnection?> EnsureMemoryConnectionAsync(SqliteConnection? memoryConnection, SqliteConnectionStringBuilder cnStringBuilder, CancellationToken stoppingToken)
        {
            memoryConnection ??= new SqliteConnection(cnStringBuilder.ConnectionString);

            if (memoryConnection.State != System.Data.ConnectionState.Open)
            {
                await memoryConnection.OpenAsync(stoppingToken).ConfigureAwait(false);
            }

            return memoryConnection;
        }
    }
}
