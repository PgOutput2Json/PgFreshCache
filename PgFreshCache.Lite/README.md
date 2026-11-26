## PgFreshCache

PgFreshCache provides a read-only `DbContext` backed by a local SQLite database, kept in sync with PostgreSQL via logical replication. 
Data can be queried using standard EF Core queries.

- On startup, the library creates the SQLite database (in memory by default) from the configured `DbContext` model, if it does not exist yet.
- It then copies existing data for the tables specified in the PostgreSQL publication, using `COPY TO` for efficient bulk transfer.
- Once the initial copy is complete, it starts listening for changes via logical replication to keep the local cache up to date.

**Note:**  
If using an in-memory SQLite database with a temporary replication slot, avoid including large tables in the publication — they can consume a lot of memory and significantly slow down the initial data load. 
Alternatively, the library allows providing your own SQLite connection string, enabling use of a file-based database alongside a permanent replication slot for more stable, long-running setups (see *File-Based Cache with a Permanent Slot* below).
With a temporary slot (the default), every table is copied again on every start and every reconnect.

## Quick Start (In-Memory).

### 🔧 1. Install the `PgFreshCache.Lite` package:

```text
dotnet add package PgFreshCache.Lite
```

### 🔧 2. Add the cache `DbContext` to your service container

In `Program.cs`:

```csharp
// Register the regular version of the context
builder.Services.AddDbContext<StoreDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Register the cache version of the context, as a keyed service
builder.Services.AddPgFreshCache<StoreDbContext>("cache", options =>
{
    options.UseConnectionString(builder.Configuration.GetConnectionString("DefaultConnection")) 
        .UsePublications("cache_publication");
});
```

### 🧪 3. Query data via the cache `DbContext`

Inject the context using `[FromKeyedServices]`:

```csharp
public class ProductsController : ControllerBase
{
    private readonly StoreDbContext _dbContext;

    public ProductsController(
        [FromKeyedServices("cache")] StoreDbContext dbContext,
        ILogger<ProductsController> logger)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IEnumerable<Product>> Get(CancellationToken token)
    {
        return await _dbContext.Products
            .Include(p => p.Stocks)
            .OrderBy(p => p.Id)
            .ToListAsync(token);
    }
}
```

### 🛠 4. PostgreSQL Setup: Create the publication

Make sure your PostgreSQL database is configured for logical replication.

 Then create the publication:

```sql
-- Enable replication in PostgreSQL config (if needed):
-- wal_level = logical

-- Create publication for the tables you want to cache
CREATE PUBLICATION cache_publication
    FOR TABLE products, stocks;
```

> The publication name (`cache_publication`) must match the one passed to `AddPgFreshCache`.

SQLite has no schemas: a table is cached under its name alone (`sales.orders` is the SQLite table `orders`), the same name the
SQLite provider of EF Core gives an entity mapped to it (it ignores the schema). So a publication cannot have two tables with the
same name in different schemas: the replication logs an error naming both, and retries until one of them is removed from it.

### 💾 File-Based Cache with a Permanent Slot

For large tables, or to avoid a full copy on every start, keep the cache in a file and use a permanent replication slot. After a restart, the cache continues from where it stopped.

Create the slot first (the library does not create permanent slots). Slot names may only contain lower case letters, numbers and underscores:

```sql
SELECT pg_create_logical_replication_slot('pgfresh_cache_slot', 'pgoutput');
```

```csharp
builder.Services.AddPgFreshCache<StoreDbContext>("cache", options =>
{
    options.UseConnectionString(builder.Configuration.GetConnectionString("DefaultConnection"))
        .UsePublications("cache_publication")
        .UseReplicationSlot("pgfresh_cache_slot")
        .UseSqlite("Data Source=cache.db");
});
```

- A permanent slot needs a file-based cache: with the default in-memory cache, `AddPgFreshCache` throws, because after a restart the tables would not be copied again into the empty database.
- The copy state is kept in PostgreSQL (see the permissions below). To copy everything again, stop the application, delete the cache file and the slot's rows: `DELETE FROM pgoutput2json.table_sync_state WHERE slot_name = 'pgfresh_cache_slot';`
- PostgreSQL keeps the WAL for a permanent slot while the application is stopped, so drop a slot that is no longer used: `SELECT pg_drop_replication_slot('pgfresh_cache_slot');`

### 🔐 PostgreSQL Permissions

The user used to connect must:

- Have the `REPLICATION` role
- Have `SELECT` access to all tables in the publication
- Be allowed to connect using a `pg_hba.conf` entry

Example:

```sql
-- Create user with replication rights
CREATE ROLE pgfresh_user WITH REPLICATION LOGIN PASSWORD 'your-password';

-- Or alter existing user, if existing user is to be used
ALTER ROLE existing_user WITH REPLICATION;

-- Grant read access on tables in the publication
GRANT SELECT ON TABLE products, stocks TO pgfresh_user;

-- Or, optionally, all tables in schema
GRANT SELECT ON ALL TABLES IN SCHEMA public TO pgfresh_user;

-- And, optionally, all tables created in the future
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO pgfresh_user;
```

With a **permanent** replication slot, the copy state of each table is kept in the source database, in `pgoutput2json.table_sync_state`.
The schema and the table are created on first use, which needs the `CREATE` privilege on the database. Alternatively, create the schema yourself:

```sql
CREATE SCHEMA pgoutput2json;
GRANT USAGE, CREATE ON SCHEMA pgoutput2json TO pgfresh_user;
```

If the state cannot be stored, the replication logs an error and keeps retrying, and the cache stays empty (the initial copy needs the state). With a temporary slot nothing is stored.

If needed, in `pg_hba.conf`, add an entry:

```text
# TYPE    DATABASE    USER           ADDRESS         METHOD
host      your_db     pgfresh_user   all             scram-sha-256
```
> Note that the database name is specified because `replication` alone is not enough for the initial data copy, which is done through a regular connection.

After changing the `pg_hba.conf` file, reload the PostgreSQL configuration:

```sql
SELECT pg_reload_conf();
```
