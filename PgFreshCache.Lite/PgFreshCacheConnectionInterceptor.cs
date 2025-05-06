using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace PgFreshCache.Lite
{
    internal sealed class PgFreshCacheConnectionInterceptor :  DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA query_only = true";

            cmd.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA query_only = true";

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
