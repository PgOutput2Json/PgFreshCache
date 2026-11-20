using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace PgFreshCache.IntegrationTests
{
    [TestClass]
    public class CacheTests
    {
        private static string NewKey() => $"it_{Guid.NewGuid():N}";

        private static async Task<List<string>> ProductsAsync(CacheContext db)
        {
            var products = await db.Products.OrderBy(p => p.Id).ToListAsync();

            // formatted, so 1 and 1.0 (equal decimals) compare equal
            return products.Select(p => $"{p.Id}:{p.Name}:{p.Price.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}").ToList();
        }

        [TestMethod]
        public async Task Copies_existing_rows_and_streams_changes()
        {
            var db = await TestDatabase.CreateAsync();
            await db.InsertProductAsync(1, "one", 1.5m);
            await db.InsertProductAsync(2, "two", 2.5m);
            await db.ExecuteAsync("CREATE PUBLICATION cache_publication FOR TABLE products");

            var key = NewKey();
            await using var app = await CacheApp.StartAsync(key, o => o.UseConnectionString(db.ConnectionString));

            await app.WaitForAsync(key, ProductsAsync, r => r.SequenceEqual(["1:one:1.5", "2:two:2.5"]), "the initial copy");

            await db.InsertProductAsync(3, "three", 3.5m);
            await db.ExecuteAsync("UPDATE products SET name = 'ONE', price = 9.99 WHERE id = 1");
            await db.ExecuteAsync("DELETE FROM products WHERE id = 2");

            await app.WaitForAsync(key, ProductsAsync, r => r.SequenceEqual(["1:ONE:9.99", "3:three:3.5"]), "the streamed changes");
        }

        [TestMethod]
        public async Task Column_types_read_back_through_ef()
        {
            var db = await TestDatabase.CreateAsync();
            await db.ExecuteAsync(@"
                INSERT INTO products VALUES
                    (1, 'with offset', 1234567.89, '2026-01-02 03:04:05.123456+02', '2026-01-02 03:04:05.123456', true, 'a note'),
                    (2, 'utc', 0.01, '2026-06-30 23:59:59+00', '2026-06-30 23:59:59', false, NULL);
                CREATE PUBLICATION cache_publication FOR TABLE products;");

            var key = NewKey();
            await using var app = await CacheApp.StartAsync(key, o => o.UseConnectionString(db.ConnectionString));

            var rows = await app.WaitForAsync(key, c => c.Products.OrderBy(p => p.Id).ToListAsync(), r => r.Count == 2, "the initial copy");

            Assert.AreEqual(1234567.89m, rows[0].Price);
            Assert.AreEqual(0.01m, rows[1].Price);
            Assert.AreEqual(new DateTimeOffset(2026, 1, 2, 1, 4, 5, TimeSpan.Zero).AddTicks(1234560), rows[0].CreatedAt);
            Assert.AreEqual(new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero), rows[1].CreatedAt);
            // a timestamp without time zone is the same wall time on every machine, of no particular zone
            Assert.AreEqual(new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1234560), rows[0].LocalAt);
            Assert.AreEqual(new DateTime(2026, 6, 30, 23, 59, 59), rows[1].LocalAt);
            Assert.AreEqual(DateTimeKind.Unspecified, rows[0].LocalAt.Kind);
            Assert.AreEqual(DateTimeKind.Unspecified, rows[1].LocalAt.Kind);
            Assert.IsTrue(rows[0].Active);
            Assert.IsFalse(rows[1].Active);
            Assert.AreEqual("a note", rows[0].Note);
            Assert.IsNull(rows[1].Note);

            // the values must also work in queries that EF translates to SQL
            Assert.AreEqual(1, await app.QueryAsync(key, c => c.Products.CountAsync(p => p.LocalAt > new DateTime(2026, 3, 1))));
            Assert.AreEqual(1, await app.QueryAsync(key, c => c.Products.CountAsync(p => p.Active)));
            Assert.AreEqual(1, await app.QueryAsync(key, c => c.Products.CountAsync(p => p.Note == null)));
        }

        [TestMethod]
        public async Task Only_the_tables_of_the_configured_publications_are_cached()
        {
            var db = await TestDatabase.CreateAsync();
            await db.InsertProductAsync(1, "one");
            await db.ExecuteAsync(@"
                INSERT INTO categories VALUES (1, 'cat');
                CREATE PUBLICATION products_pub FOR TABLE products;
                CREATE PUBLICATION categories_pub FOR TABLE categories;");

            var productsKey = NewKey();
            var categoriesKey = NewKey();

            await using var app = await CacheApp.StartAsync(services =>
            {
                services.AddPgFreshCache<CacheContext>(productsKey, o => o.UseConnectionString(db.ConnectionString).UsePublications("products_pub"));
                services.AddPgFreshCache<CacheContext>(categoriesKey, o => o.UseConnectionString(db.ConnectionString).UsePublications("categories_pub"));
            });

            await app.WaitForAsync(productsKey, c => c.Products.CountAsync(), n => n == 1, "products in the products cache");
            await app.WaitForAsync(categoriesKey, c => c.Categories.CountAsync(), n => n == 1, "categories in the categories cache");

            await db.InsertProductAsync(2, "two");
            await db.ExecuteAsync("INSERT INTO categories VALUES (2, 'cat2')");

            await app.WaitForAsync(productsKey, c => c.Products.CountAsync(), n => n == 2, "the new product");
            await app.WaitForAsync(categoriesKey, c => c.Categories.CountAsync(), n => n == 2, "the new category");

            Assert.AreEqual(0, await app.QueryAsync(productsKey, c => c.Categories.CountAsync()));
            Assert.AreEqual(0, await app.QueryAsync(categoriesKey, c => c.Products.CountAsync()));
        }

        [TestMethod]
        public async Task Multiple_publications_feed_one_cache()
        {
            var db = await TestDatabase.CreateAsync();
            await db.InsertProductAsync(1, "one");
            await db.ExecuteAsync(@"
                INSERT INTO categories VALUES (1, 'cat');
                CREATE PUBLICATION products_pub FOR TABLE products;
                CREATE PUBLICATION categories_pub FOR TABLE categories;");

            var key = NewKey();
            await using var app = await CacheApp.StartAsync(key, o => o.UseConnectionString(db.ConnectionString).UsePublications("products_pub", "categories_pub"));

            await app.WaitForAsync(key, c => c.Products.CountAsync(), n => n == 1, "the products");
            await app.WaitForAsync(key, c => c.Categories.CountAsync(), n => n == 1, "the categories");
        }

        [TestMethod]
        public async Task Restarted_in_memory_cache_is_copied_again()
        {
            var db = await TestDatabase.CreateAsync();
            await db.InsertProductAsync(1, "one");
            await db.ExecuteAsync("CREATE PUBLICATION cache_publication FOR TABLE products");

            var key = NewKey();

            await using (var app = await CacheApp.StartAsync(key, o => o.UseConnectionString(db.ConnectionString)))
            {
                await app.WaitForAsync(key, ProductsAsync, r => r.SequenceEqual(["1:one:1"]), "the first copy");
            }

            await db.InsertProductAsync(2, "two");

            await using (var app = await CacheApp.StartAsync(key, o => o.UseConnectionString(db.ConnectionString)))
            {
                await app.WaitForAsync(key, ProductsAsync, r => r.SequenceEqual(["1:one:1", "2:two:1"]), "the copy after the restart");
            }
        }

        [TestMethod]
        [DataRow(true, DisplayName = "WAL")]
        [DataRow(false, DisplayName = "rollback journal")]
        public async Task File_cache_with_permanent_slot_catches_up_after_restart(bool useWal)
        {
            var db = await TestDatabase.CreateAsync();
            await db.InsertProductAsync(1, "one");
            await db.ExecuteAsync("CREATE PUBLICATION cache_publication FOR TABLE products");
            // a slot cannot be created in a transaction that has written, so not in the same batch
            var slot = $"slot_{Guid.NewGuid():N}"; // slot names are per cluster, not per database
            await db.ExecuteAsync($"SELECT pg_create_logical_replication_slot('{slot}', 'pgoutput')");

            var key = NewKey();
            var file = Path.Combine(Path.GetTempPath(), $"pgfreshcache_{Guid.NewGuid():N}.db");

            void Configure(PgFreshCache.Lite.PgFreshCacheOptionsBuilder<CacheContext> o)
            {
                o.UseConnectionString(db.ConnectionString)
                    .UseReplicationSlot(slot)
                    .UseSqlite($"Data Source={file}");

                if (!useWal) o.UseRollbackJournal();
            }

            try
            {
                await using (var app = await CacheApp.StartAsync(key, Configure))
                {
                    await app.WaitForAsync(key, ProductsAsync, r => r.SequenceEqual(["1:one:1"]), "the initial copy");

                    await db.InsertProductAsync(2, "two");

                    await app.WaitForAsync(key, ProductsAsync, r => r.SequenceEqual(["1:one:1", "2:two:1"]), "the streamed insert");
                }

                // changes while the application is down
                await db.InsertProductAsync(3, "three");
                await db.ExecuteAsync("UPDATE products SET name = 'ONE' WHERE id = 1; DELETE FROM products WHERE id = 2;");

                await using (var app = await CacheApp.StartAsync(key, Configure))
                {
                    await app.WaitForAsync(key, ProductsAsync, r => r.SequenceEqual(["1:ONE:1", "3:three:1"]), "the changes made while stopped");
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();

                foreach (var f in new[] { file, file + "-wal", file + "-shm", file + "-journal" })
                {
                    if (File.Exists(f)) File.Delete(f);
                }
            }
        }
    }
}
