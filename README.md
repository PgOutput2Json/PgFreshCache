## PgFreshCache - Overview

PgFreshCache provides a read-only `DbContext` backed by a local SQLite database, kept in sync with PostgreSQL via logical replication. Data can be queried using standard EF Core queries.

- On startup, the library recreates the in-memory SQLite database based on the configured `DbContext` model.
- It then copies existing data for the tables specified in the PostgreSQL publication, using `COPY TO` for efficient bulk transfer.
- Once the initial copy is complete, it starts listening for changes via logical replication to keep the local cache up to date.

**Note:**  
If using an in-memory SQLite database with a temporary replication slot, avoid including large tables in the publication — they can consume a lot of memory and significantly slow down the initial data load. 
Alternatively, the library allows providing your own SQLite connection string, enabling use of a file-based database alongside a permanent replication slot for more stable, long-running setups.

## ⚠️ Development Status

**Bleeding edge** — the library is under active development, and **not tested**.

This library relies on **[PgOutput2Json](https://github.com/PgOutput2Json/PgOutput2Json)** to receive row changes as compact JSON messages (`WriteMode.Compact`).

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
