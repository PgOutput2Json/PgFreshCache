using PgOutput2Json.Sqlite;

namespace PgFreshCache.Lite
{
    public sealed class PgFreshCacheOptions
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
}
