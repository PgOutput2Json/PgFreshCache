using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: DoNotParallelize]

namespace PgFreshCache.Tests
{
    [Table("items")]
    public class Item
    {
        [Column("id")] public int Id { get; set; }
        [Column("name")] public string Name { get; set; } = "";
    }

    public class ItemsContext(DbContextOptions<ItemsContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    /// <summary>
    /// A constructor with more than the options, so the factory cannot take its fast path.
    /// </summary>
    public class ExtraParameterContext(DbContextOptions<ExtraParameterContext> options, ILogger<ExtraParameterContext> logger) : DbContext(options)
    {
        public ILogger Logger { get; } = logger;
    }

    public class NonGenericOptionsContext(DbContextOptions options) : DbContext(options)
    {
    }

    internal static class Services
    {
        /// <summary>
        /// A unique key per test, so every test gets its own in-memory database (its name is derived from the key).
        /// </summary>
        public static string NewKey() => $"test_{Guid.NewGuid():N}";

        public static ServiceCollection Create(LogCollector? logs = null)
        {
            var services = new ServiceCollection();

            services.AddLogging(b =>
            {
                b.SetMinimumLevel(LogLevel.Debug);
                if (logs != null) b.AddProvider(logs);
            });

            return services;
        }
    }

    internal sealed class LogCollector : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCollector owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner.Entries.Enqueue((logLevel, formatter(state, exception)));
            }
        }
    }
}
