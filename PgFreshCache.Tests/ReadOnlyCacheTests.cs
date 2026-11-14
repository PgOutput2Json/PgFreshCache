using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PgFreshCache.Tests
{
    /// <summary>
    /// The keyed context reads the cache that the worker writes, through the same shared in-memory database.
    /// It must not be able to write to it.
    /// </summary>
    [TestClass]
    public sealed class ReadOnlyCacheTests : IDisposable
    {
        private readonly string _key = Services.NewKey();
        private readonly ServiceProvider _sp;
        private readonly SqliteConnection _writer; // plays the worker, and keeps the in-memory database alive

        public ReadOnlyCacheTests()
        {
            var services = Services.Create();
            services.AddPgFreshCache<ItemsContext>(_key);

            _sp = services.BuildServiceProvider();

            using var scope = _sp.CreateScope();
            var cs = scope.ServiceProvider.GetRequiredKeyedService<ItemsContext>(_key).Database.GetConnectionString();

            _writer = new SqliteConnection(cs);
            _writer.Open();

            Execute("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT NOT NULL); INSERT INTO items VALUES (1, 'one');");
        }

        public void Dispose()
        {
            _writer.Dispose();
            _sp.Dispose();
        }

        private void Execute(string sql)
        {
            using var cmd = _writer.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private ItemsContext CreateCache(IServiceScope scope) => scope.ServiceProvider.GetRequiredKeyedService<ItemsContext>(_key);

        [TestMethod]
        public async Task Reads_what_the_worker_writes()
        {
            using var scope = _sp.CreateScope();
            var cache = CreateCache(scope);

            Assert.AreEqual("one", (await cache.Items.SingleAsync()).Name);

            Execute("INSERT INTO items VALUES (2, 'two')");

            Assert.AreEqual(2, cache.Items.Count());
        }

        [TestMethod]
        public void SaveChanges_is_rejected()
        {
            using var scope = _sp.CreateScope();
            var cache = CreateCache(scope);

            cache.Items.Add(new Item { Id = 3, Name = "three" });

            var ex = Assert.ThrowsExactly<DbUpdateException>(() => cache.SaveChanges());
            AssertReadOnly(ex.InnerException);
        }

        [TestMethod]
        public async Task SaveChangesAsync_is_rejected()
        {
            using var scope = _sp.CreateScope();
            var cache = CreateCache(scope);

            cache.Items.Add(new Item { Id = 3, Name = "three" });

            var ex = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => cache.SaveChangesAsync());
            AssertReadOnly(ex.InnerException);
        }

        [TestMethod]
        public void Raw_sql_writes_are_rejected()
        {
            using var scope = _sp.CreateScope();
            var cache = CreateCache(scope);

            var ex = Assert.ThrowsExactly<SqliteException>(() => cache.Database.ExecuteSqlRaw("DELETE FROM items"));
            AssertReadOnly(ex);
        }

        [TestMethod]
        public async Task Raw_sql_writes_are_rejected_async()
        {
            using var scope = _sp.CreateScope();
            var cache = CreateCache(scope);

            var ex = await Assert.ThrowsExactlyAsync<SqliteException>(() => cache.Database.ExecuteSqlRawAsync("DROP TABLE items"));
            AssertReadOnly(ex);

            Assert.AreEqual(1, await cache.Items.CountAsync());
        }

        private static void AssertReadOnly(Exception? ex)
        {
            Assert.IsInstanceOfType<SqliteException>(ex);
            Assert.AreEqual(8, ((SqliteException)ex).SqliteErrorCode, ex.Message); // SQLITE_READONLY
        }
    }
}
