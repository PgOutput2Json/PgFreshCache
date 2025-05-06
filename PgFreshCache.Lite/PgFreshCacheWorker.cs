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
        private readonly PgFreshCacheAccessor<TDbContext> _accessor;
        private readonly IDbContextFactory<TDbContext> _dbContextFactory;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<PgFreshCacheWorker<TDbContext>> _logger;

        public PgFreshCacheWorker(PgFreshCacheOptions options,
                                  PgFreshCacheAccessor<TDbContext> accessor,
                                  IDbContextFactory<TDbContext> dbContextFactory,
                                  ILoggerFactory loggerFactory,
                                  ILogger<PgFreshCacheWorker<TDbContext>> logger)
        {
            _options = options;
            _accessor = accessor;
            _dbContextFactory = dbContextFactory;
            _loggerFactory = loggerFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            SqliteConnection? memoryConnection = null; // keep the connection open while the app is running

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cnStringBuilder = new SqliteConnectionStringBuilder(_options.SqliteConnectionString);

                    if (IsMemoryConection(cnStringBuilder))
                    {
                        if (cnStringBuilder.Cache != SqliteCacheMode.Shared)
                        {
                            _logger.LogError("In-Memory Sqlite database is not confgured with shared cache. Aborting PgFreshCache start");
                            break;
                        }

                        memoryConnection = await EnsureMemoryConnection(memoryConnection, cnStringBuilder, stoppingToken).ConfigureAwait(false);
                    }

                    await EnsureDatabaseCreated(stoppingToken).ConfigureAwait(false);

                    var builder = PgOutput2JsonBuilder.Create()
                        .WithLoggerFactory(_loggerFactory)
                        .WithPgConnectionString(_options.PostgresConnectionString)
                        .WithPgPublications([.. _options.PublicationNames])
                        .WithPgReplicationSlot(_options.ReplicationSlotName, _options.UseTemporaryReplicationSlot)
                        .WithBatchSize(10_000)
                        .WithInitialDataCopy(true)
                        .WithIdleFlushTime(1)
                        .UseSqlite(options =>
                        {
                            options.ConnectionStringBuilder = cnStringBuilder;
                            options.UseWal = _options.UseWal;
                            options.WalCheckpointTryCount = _options.WalCheckpointTryCount;
                            options.WalCheckpointType = _options.WalCheckpointType;
                        });

                    using var pgOutput2Json = builder.Build();

                    _accessor.SetPgOutput2Json(pgOutput2Json);

                    await pgOutput2Json.Start(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Failed to start PgFreshCache worker. Retrying in 10 seconds...");

                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
                    }
                }
            }

            memoryConnection?.Dispose();
        }

        private async Task EnsureDatabaseCreated(CancellationToken stoppingToken)
        {
            using var db = _dbContextFactory.CreateDbContext();

            using var connection = new SqliteConnection(_options.SqliteConnectionString);
            
            await connection.OpenAsync(stoppingToken).ConfigureAwait(false);

            db.Database.SetDbConnection(connection);

            await db.Database.EnsureCreatedAsync(stoppingToken).ConfigureAwait(false);
        }

        private static async Task<SqliteConnection?> EnsureMemoryConnection(SqliteConnection? memoryConnection, SqliteConnectionStringBuilder cnStringBuilder, CancellationToken stoppingToken)
        {
            memoryConnection ??= new SqliteConnection(cnStringBuilder.ConnectionString);

            if (memoryConnection.State != System.Data.ConnectionState.Open)
            {
                await memoryConnection.OpenAsync(stoppingToken).ConfigureAwait(false);
            }

            return memoryConnection;
        }

        private static bool IsMemoryConection(SqliteConnectionStringBuilder cnStringBuilder)
        {
            return cnStringBuilder.Mode == SqliteOpenMode.Memory || (cnStringBuilder.DataSource != null && cnStringBuilder.DataSource.Contains(":memory:"));
        }
    }
}
