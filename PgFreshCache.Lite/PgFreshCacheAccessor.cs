﻿using PgOutput2Json;

namespace PgFreshCache.Lite
{
    public sealed class PgFreshCacheAccessor<TDbContext>
    {
        private IPgOutput2Json? _pg2j;

        public PgFreshCacheAccessor()
        {
        }

        public async Task<bool> WhenLsnReachesAsync(string expectedLsn, TimeSpan timeout, CancellationToken cancellationToken)
        {
            return _pg2j != null && await _pg2j.WhenLsnReachesAsync(expectedLsn, timeout, cancellationToken).ConfigureAwait(false);
        }

        internal void SetPgOutput2Json(IPgOutput2Json pg2j)
        {
            _pg2j = pg2j;
        }
    }
}
