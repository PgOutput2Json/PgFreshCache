using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PgOutput2Json.Sqlite;

namespace PgFreshCache.Lite
{
    internal sealed class PgFreshCacheOptions
    {
        public required string PostgresConnectionString { get; set; }

        public required string SqliteConnectionString { get; set; }
        public required List<string> PublicationNames { get; set; }

        public required string ReplicationSlotName { get; set; }
        public required bool UseTemporaryReplicationSlot { get; set; }

        public required bool UseWal { get; set; }
        public required int WalCheckpointTryCount { get; set; }
        public required WalCheckpointType WalCheckpointType { get; set; }

        internal static bool IsMemoryConnection(SqliteConnectionStringBuilder cnStringBuilder)
        {
            return cnStringBuilder.Mode == SqliteOpenMode.Memory || (cnStringBuilder.DataSource != null && cnStringBuilder.DataSource.Contains(":memory:"));
        }
    }

    public sealed class PgFreshCacheOptionsBuilder<TDbContext> where TDbContext : DbContext
    {
        internal readonly PgFreshCacheOptions Options;
        internal readonly DbContextOptionsBuilder<TDbContext> SqliteOptionsBuilder;

        public PgFreshCacheOptionsBuilder(string cacheKey)
        {
            Options = new PgFreshCacheOptions
            {
                PostgresConnectionString = string.Empty,
                SqliteConnectionString = $"Data Source=PgFreshCache_{cacheKey};Mode=Memory;Cache=Shared;Foreign Keys=False",
                ReplicationSlotName = $"pgfreshcache_{Guid.NewGuid():N}", // slot names may only contain lower case letters, numbers and underscores
                PublicationNames = ["cache_publication"],
                UseTemporaryReplicationSlot = true,

                UseWal = true,
                WalCheckpointTryCount = 10,
                WalCheckpointType = WalCheckpointType.Automatic,
            };

            SqliteOptionsBuilder = new DbContextOptionsBuilder<TDbContext>()
                .UseSqlite(Options.SqliteConnectionString)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution)
                .AddInterceptors(new PgFreshCacheConnectionInterceptor());
        }

        public PgFreshCacheOptionsBuilder<TDbContext> UseConnectionString(string? postgresConnectionString)
        {
            Options.PostgresConnectionString = postgresConnectionString ?? throw new ArgumentNullException(nameof(postgresConnectionString));
            return this;
        }

        public PgFreshCacheOptionsBuilder<TDbContext> UsePublications(params string[] publicationNames)
        {
            Options.PublicationNames = [..publicationNames];
            return this;
        }

        public PgFreshCacheOptionsBuilder<TDbContext> UseReplicationSlot(string replicationSlotName, bool useTemporarySlot = false)
        {
            Options.ReplicationSlotName = replicationSlotName;
            Options.UseTemporaryReplicationSlot = useTemporarySlot;
            return this;
        }

        public PgFreshCacheOptionsBuilder<TDbContext> UseRollbackJournal()
        {
            Options.UseWal = false;
            return this;
        }

        public PgFreshCacheOptionsBuilder<TDbContext> UseWal(WalCheckpointType walCheckpointType)
        {
            Options.UseWal = true;
            Options.WalCheckpointType = walCheckpointType;
            return this;
        }

        public PgFreshCacheOptionsBuilder<TDbContext> UseSqlite(string sqliteConnectionString, Action<SqliteDbContextOptionsBuilder>? sqliteOptionsAction = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sqliteConnectionString);

            // the worker writes to this connection string, the DbContext reads from the one below - they must be the same
            Options.SqliteConnectionString = sqliteConnectionString;
            SqliteOptionsBuilder.UseSqlite(sqliteConnectionString, sqliteOptionsAction);
            return this;
        }

        internal void Validate()
        {
            // The table copy state of a permanent slot is kept in PostgreSQL, so after a restart the tables are
            // considered copied and only new changes are streamed - an in-memory database would stay (partly) empty.
            if (!Options.UseTemporaryReplicationSlot
                && PgFreshCacheOptions.IsMemoryConnection(new SqliteConnectionStringBuilder(Options.SqliteConnectionString)))
            {
                throw new InvalidOperationException(
                    $"Permanent replication slot '{Options.ReplicationSlotName}' cannot be used with an in-memory Sqlite database. " +
                    "Use a temporary replication slot, or a file-based Sqlite database (UseSqlite).");
            }
        }
    }
}
