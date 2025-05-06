## PgFreshCache - Minimal Usage

PgFreshCache provides a read-only `DbContext` backed by a local SQLite database, kept in sync with PostgreSQL via logical replication. Data can be queried using standard EF Core queries.

## ⚠️ Development Status

**Bleeding edge** — the library is under active development, and **not tested**.

### 🔧 1. Add the cache `DbContext` to your service container

In `Program.cs`:

```csharp
var pgConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new Exception("DefaultConnection not configured");

// Register the regular version of the context
builder.Services.AddDbContext<StoreDbContext>(options =>
{
    options.UseNpgsql(pgConnectionString);
});

// Register the cache version of the context
builder.Services.AddPgFreshCache<StoreDbContext>(
    "cache",                   // Keyed service name
    pgConnectionString,        // PostgreSQL connection string
    "cache_publication"        // Logical replication publication name
);

var app = builder.Build();
```

### 🧪 2. Query data via the cache `DbContext`

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

### 🛠 3. PostgreSQL Setup: Create the publication

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
CREATE ROLE pgfresh_user WITH LOGIN PASSWORD 'your-password';
GRANT REPLICATION TO pgfresh_user;

-- Grant read access
GRANT SELECT ON TABLE products, stocks TO pgfresh_user;
```

In `pg_hba.conf`, add a replication entry:

```text
# TYPE    DATABASE    USER           ADDRESS         METHOD
host      replication pgfresh_user   0.0.0.0/0       scram-sha-256
```

Then reload PostgreSQL:

```sql
SELECT pg_reload_conf();
```
