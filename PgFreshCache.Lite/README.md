### PgFreshCache

PgFreshCache provides a read-only `DbContext` backed by a local SQLite database, kept in sync with PostgreSQL via logical replication. 
Data can be queried using standard EF Core queries.

### 🔧 1. Add the cache `DbContext` to your service container

In `Program.cs`:

```csharp
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
