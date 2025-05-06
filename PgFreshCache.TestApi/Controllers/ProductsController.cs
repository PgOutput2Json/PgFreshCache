using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PgFreshCache.TestApi.Model;

namespace PgFreshCache.TestApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly StoreDbContext _dbContext;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController([FromKeyedServices("cache")] StoreDbContext dbContext,
                                  ILogger<ProductsController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        [HttpGet(Name = "products")]
        public async Task<IEnumerable<Product>> Get(CancellationToken token)
        {
            return await _dbContext.Products
                .Include(p => p.Stocks)
                .OrderBy(p => p.Id)
                .ToListAsync(token);
        }
    }
}
