using Microsoft.Data.Sqlite;

using PgFreshCache.Lite;
using PgOutput2Json.Sqlite;

namespace PgFreshCache.Tests
{
    /// <summary>
    /// The options the worker gives the SQLite output of PgOutput2Json.
    /// </summary>
    [TestClass]
    public class SqliteOptionsTests
    {
        private static SqlitePublisherOptions Configure(Action<PgFreshCacheOptionsBuilder<ItemsContext>>? configure = null)
        {
            var builder = new PgFreshCacheOptionsBuilder<ItemsContext>(Services.NewKey());
            configure?.Invoke(builder);

            var options = new SqlitePublisherOptions { ConnectionStringBuilder = new SqliteConnectionStringBuilder() };
            PgFreshCacheWorker<ItemsContext>.ConfigureSqlite(options, builder.Options, new SqliteConnectionStringBuilder(builder.Options.SqliteConnectionString));

            return options;
        }

        [TestMethod]
        public void The_subscription_name_is_the_replication_slot()
        {
            Assert.AreEqual("cache_slot", Configure(o => o.UseReplicationSlot("cache_slot", useTemporarySlot: true)).SubscriptionName);
        }

        [TestMethod]
        public void The_default_slot_gives_a_subscription_name()
        {
            var options = Configure();

            Assert.IsFalse(string.IsNullOrWhiteSpace(options.SubscriptionName));
        }

        [TestMethod]
        public void Tables_are_named_without_their_schema()
        {
            // the names EF Core's SQLite provider gives the tables of the model: it ignores schemas
            var options = Configure();

            Assert.AreEqual("orders", options.GetTableName("public", "orders"));
            Assert.AreEqual("orders", options.GetTableName("sales", "orders"));
        }
    }
}
