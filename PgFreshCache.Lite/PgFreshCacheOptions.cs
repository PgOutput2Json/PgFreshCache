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
                ReplicationSlotName = $"PgFreshCache_{Guid.NewGuid()}".Replace("-", ""),
                PublicationNames = ["cache_publication"],
                UseTemporaryReplicationSlot = true,

                UseWal = true,
                WalCheckpointTryCount = 10,
                WalCheckpointType = WalCheckpointType.Full,
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

        public PgFreshCacheOptionsBuilder<TDbContext> WithReplicationSlot(string replicationSlotName, bool useTemporarySlot = false)
        {
            Options.ReplicationSlotName = replicationSlotName;
            Options.UseTemporaryReplicationSlot = useTemporarySlot;
            return this;
        }

        public PgFreshCacheOptionsBuilder<TDbContext> UseSqlite(string sqliteConnectionString, Action<SqliteDbContextOptionsBuilder>? sqliteOptionsAction)
        {
            SqliteOptionsBuilder.UseSqlite(sqliteConnectionString, sqliteOptionsAction);
            return this;
        }
    }
}
