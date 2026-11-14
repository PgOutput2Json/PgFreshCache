using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PgFreshCache.Lite;

namespace PgFreshCache.Tests
{
    [TestClass]
    public class RegistrationTests
    {
        [TestMethod]
        public void Keyed_context_uses_the_in_memory_sqlite_cache_by_default()
        {
            var key = Services.NewKey();
            var services = Services.Create();
            services.AddPgFreshCache<ItemsContext>(key);

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var db = scope.ServiceProvider.GetRequiredKeyedService<ItemsContext>(key);
            var cs = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());

            Assert.AreEqual("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
            Assert.AreEqual($"PgFreshCache_{key}", cs.DataSource);
            Assert.AreEqual(SqliteOpenMode.Memory, cs.Mode);
            Assert.AreEqual(SqliteCacheMode.Shared, cs.Cache);
            Assert.AreEqual(QueryTrackingBehavior.NoTrackingWithIdentityResolution, db.ChangeTracker.QueryTrackingBehavior);
        }

        [TestMethod]
        public void Keyed_context_does_not_replace_the_regular_context()
        {
            var key = Services.NewKey();
            var services = Services.Create();
            services.AddDbContext<ItemsContext>(o => o.UseSqlite("Data Source=regular_app.db"));
            services.AddPgFreshCache<ItemsContext>(key);

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var regular = scope.ServiceProvider.GetRequiredService<ItemsContext>();
            var cache = scope.ServiceProvider.GetRequiredKeyedService<ItemsContext>(key);

            Assert.AreEqual("Data Source=regular_app.db", regular.Database.GetConnectionString());
            StringAssert.Contains(cache.Database.GetConnectionString(), $"PgFreshCache_{key}");
        }

        [TestMethod]
        public void Keyed_context_is_scoped()
        {
            var key = Services.NewKey();
            var services = Services.Create();
            services.AddPgFreshCache<ItemsContext>(key);

            using var sp = services.BuildServiceProvider();
            using var scope1 = sp.CreateScope();
            using var scope2 = sp.CreateScope();

            var a = scope1.ServiceProvider.GetRequiredKeyedService<ItemsContext>(key);
            var b = scope1.ServiceProvider.GetRequiredKeyedService<ItemsContext>(key);
            var c = scope2.ServiceProvider.GetRequiredKeyedService<ItemsContext>(key);

            Assert.AreSame(a, b);
            Assert.AreNotSame(a, c);
        }

        [TestMethod]
        public void Context_with_extra_constructor_parameters_gets_the_cache_options()
        {
            var key = Services.NewKey();
            var services = Services.Create();
            // the regular options in DI must not leak into the cache context
            services.AddDbContext<ExtraParameterContext>(o => o.UseSqlite("Data Source=regular_app.db"));
            services.AddPgFreshCache<ExtraParameterContext>(key);

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var cache = scope.ServiceProvider.GetRequiredKeyedService<ExtraParameterContext>(key);

            StringAssert.Contains(cache.Database.GetConnectionString(), $"PgFreshCache_{key}");
            Assert.IsNotNull(cache.Logger);
        }

        [TestMethod]
        public void Context_with_extra_constructor_parameters_works_without_a_regular_context()
        {
            var key = Services.NewKey();
            var services = Services.Create();
            services.AddPgFreshCache<ExtraParameterContext>(key);

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var cache = scope.ServiceProvider.GetRequiredKeyedService<ExtraParameterContext>(key);

            StringAssert.Contains(cache.Database.GetConnectionString(), $"PgFreshCache_{key}");
        }

        [TestMethod]
        public void Context_with_non_generic_options_gets_the_cache_options()
        {
            var key = Services.NewKey();
            var services = Services.Create();
            services.AddPgFreshCache<NonGenericOptionsContext>(key);

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var cache = scope.ServiceProvider.GetRequiredKeyedService<NonGenericOptionsContext>(key);

            StringAssert.Contains(cache.Database.GetConnectionString(), $"PgFreshCache_{key}");
        }

        [TestMethod]
        public void Two_caches_get_separate_databases_and_workers()
        {
            var key1 = Services.NewKey();
            var key2 = Services.NewKey();
            var services = Services.Create();
            services.AddPgFreshCache<ItemsContext>(key1);
            services.AddPgFreshCache<ItemsContext>(key2);

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var cache1 = scope.ServiceProvider.GetRequiredKeyedService<ItemsContext>(key1);
            var cache2 = scope.ServiceProvider.GetRequiredKeyedService<ItemsContext>(key2);

            StringAssert.Contains(cache1.Database.GetConnectionString(), key1);
            StringAssert.Contains(cache2.Database.GetConnectionString(), key2);
            Assert.HasCount(2, sp.GetServices<IHostedService>());
        }

        [TestMethod]
        public void UseSqlite_sets_the_replication_and_the_read_connection_string()
        {
            var key = Services.NewKey();
            var services = Services.Create();
            services.AddPgFreshCache<ItemsContext>(key, o => o.UseSqlite("Data Source=cache_file.db"));

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var options = sp.GetRequiredKeyedService<PgFreshCacheOptions>(key);
            var cache = scope.ServiceProvider.GetRequiredKeyedService<ItemsContext>(key);

            Assert.AreEqual("Data Source=cache_file.db", options.SqliteConnectionString);
            Assert.AreEqual("Data Source=cache_file.db", cache.Database.GetConnectionString());
        }

        [TestMethod]
        public void UseSqlite_runs_the_sqlite_options_action()
        {
            var called = false;
            var services = Services.Create();
            services.AddPgFreshCache<ItemsContext>(Services.NewKey(), o => o.UseSqlite("Data Source=cache_file.db", _ => called = true));

            Assert.IsTrue(called);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("  ")]
        public void UseSqlite_rejects_an_empty_connection_string(string connectionString)
        {
            var services = Services.Create();

            Assert.ThrowsExactly<ArgumentException>(() =>
                services.AddPgFreshCache<ItemsContext>(Services.NewKey(), o => o.UseSqlite(connectionString)));
        }

        [TestMethod]
        public void UseConnectionString_rejects_null()
        {
            var services = Services.Create();

            Assert.ThrowsExactly<ArgumentNullException>(() =>
                services.AddPgFreshCache<ItemsContext>(Services.NewKey(), o => o.UseConnectionString(null)));
        }

        [TestMethod]
        public void Default_options()
        {
            var key = Services.NewKey();
            var services = Services.Create();
            services.AddPgFreshCache<ItemsContext>(key);

            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredKeyedService<PgFreshCacheOptions>(key);

            CollectionAssert.AreEqual(new[] { "cache_publication" }, options.PublicationNames);
            Assert.IsTrue(options.UseTemporaryReplicationSlot);
            Assert.IsTrue(options.UseWal);
        }

        [TestMethod]
        public void Default_slot_name_is_a_valid_unique_postgres_slot_name()
        {
            var key1 = Services.NewKey();
            var key2 = Services.NewKey();
            var services = Services.Create();
            services.AddPgFreshCache<ItemsContext>(key1);
            services.AddPgFreshCache<ItemsContext>(key2);

            using var sp = services.BuildServiceProvider();
            var slot1 = sp.GetRequiredKeyedService<PgFreshCacheOptions>(key1).ReplicationSlotName;
            var slot2 = sp.GetRequiredKeyedService<PgFreshCacheOptions>(key2).ReplicationSlotName;

            // PostgreSQL: lower case letters, numbers and underscores; at most 63 characters
            Assert.MatchesRegex(new Regex("^[a-z0-9_]{1,63}$"), slot1);
            Assert.AreNotEqual(slot1, slot2);
        }

        [TestMethod]
        public void Builder_methods_set_the_options()
        {
            var key = Services.NewKey();
            var services = Services.Create();
            services.AddPgFreshCache<ItemsContext>(key, o => o
                .UseConnectionString("Host=pg")
                .UsePublications("pub_a", "pub_b")
                .UseReplicationSlot("my_slot", useTemporarySlot: true)
                .UseRollbackJournal());

            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredKeyedService<PgFreshCacheOptions>(key);

            Assert.AreEqual("Host=pg", options.PostgresConnectionString);
            CollectionAssert.AreEqual(new[] { "pub_a", "pub_b" }, options.PublicationNames);
            Assert.AreEqual("my_slot", options.ReplicationSlotName);
            Assert.IsTrue(options.UseTemporaryReplicationSlot);
            Assert.IsFalse(options.UseWal);
        }

        [TestMethod]
        [DataRow(null, DisplayName = "default in-memory database")]
        [DataRow("Data Source=:memory:", DisplayName = ":memory: data source")]
        [DataRow("Data Source=cache;Mode=Memory;Cache=Shared", DisplayName = "Mode=Memory")]
        public void Permanent_slot_with_in_memory_sqlite_fails_at_registration(string? sqliteConnectionString)
        {
            var services = Services.Create();

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                services.AddPgFreshCache<ItemsContext>(Services.NewKey(), o =>
                {
                    if (sqliteConnectionString != null) o.UseSqlite(sqliteConnectionString);
                    o.UseReplicationSlot("my_slot");
                }));

            StringAssert.Contains(ex.Message, "my_slot");
        }

        [TestMethod]
        public void Permanent_slot_with_file_sqlite_is_accepted()
        {
            var services = Services.Create();

            services.AddPgFreshCache<ItemsContext>(Services.NewKey(), o => o
                .UseReplicationSlot("my_slot")
                .UseSqlite("Data Source=cache_file.db"));
        }

        [TestMethod]
        public void Temporary_named_slot_with_in_memory_sqlite_is_accepted()
        {
            var services = Services.Create();

            services.AddPgFreshCache<ItemsContext>(Services.NewKey(), o => o.UseReplicationSlot("my_slot", useTemporarySlot: true));
        }
    }
}
