using Microsoft.Extensions.Options;
using ProductionReadySetup.Caching.Api.Caching;
using ProductionReadySetup.Caching.Api.Options;
using StackExchange.Redis;

namespace ProductionReadySetup.Caching.Api.Extensions
{
    /// <summary>
    /// Extension method to register all Redis caching services into DI.
    ///
    /// USAGE IN Program.cs:
    ///   builder.Services.AddProductionRedisCache(builder.Configuration);
    ///
    /// WHAT THIS REGISTERS:
    ///   - RedisOptions + CacheOptions (validated at startup)
    ///   - IConnectionMultiplexer as Singleton (one connection pool for the app lifetime)
    ///   - IAppCache → RedisAppCache as Singleton
    ///   - CacheKeyBuilder as Singleton
    ///   - Redis health check
    ///
    /// WHY SINGLETON FOR IConnectionMultiplexer:
    ///   StackExchange.Redis is designed for long-lived reuse.
    ///   The multiplexer manages an internal connection pool.
    ///   Creating per-request = connection exhaustion and severe performance degradation.
    ///   PITFALL: Never register IConnectionMultiplexer as Scoped or Transient.
    ///
    /// WHY SINGLETON FOR IAppCache:
    ///   RedisAppCache is stateless — it gets IDatabase per operation.
    ///   Safe and efficient as singleton.
    /// </summary>
    public static class RedisCacheExtensions
    {
        public static IServiceCollection AddProductionRedisCache(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // AddOptions<T>() takes the raw options class — the framework wraps
            // it in IOptions<T> automatically for you. Wrapping it yourself creates
            // a double-wrapped type that can never be resolved correctly from DI.         
            services
                .AddOptions<RedisOptions>()
                .BindConfiguration(RedisOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart(); // Fail fast — bad Redis config crashes at startup

            services
                .AddOptions<CacheOptions>()
                .BindConfiguration(CacheOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart(); // Fail fast — bad Redis config crashes at startup

            // ── 2. Register IConnectionMultiplexer as Singleton ───────────────────
            // WHY factory delegate: ConnectionMultiplexer.Connect() is blocking
            // and must be called after options are resolved from DI.
            // Using a factory ensures it runs at first resolution, not at registration.
            services.AddSingleton<IConnectionMultiplexer>(sp => 
            {
                var redisOptions = sp.GetRequiredService<IOptions<RedisOptions>>().Value;

                var config = new ConfigurationOptions
                {
                    ConnectTimeout = redisOptions.ConnectTimeoutMs,
                    SyncTimeout = redisOptions.SyncTimeoutMs,
                    ConnectRetry = redisOptions.ConnectRetry,
                    Ssl = redisOptions.UseSsl,
                    AbortOnConnectFail = false // WHY: Don't crash on startup if Redis is momentarily unavailable.
                                               // Allows the app to start and serve non-cached requests.
                                               // Health check will report Redis as unhealthy until it recovers.
                };

                config.EndPoints.Add(redisOptions.ConnectionString);

                return ConnectionMultiplexer.Connect(config);
            });

            // ── 3. Register cache services ────────────────────────────────────────
            services.AddSingleton<CacheKeyBuilder>();
            services.AddSingleton<IAppCache, RedisAppCache>();

            // ── 4. Redis health check ─────────────────────────────────────────────
            // WHY: Kubernetes liveness/readiness probes need to know if Redis
            // is reachable. Without this, pods stay "Ready" even when Redis is down.
            // The health check endpoint is configured in Program.cs → MapHealthChecks.
            services
                .AddHealthChecks()
                .AddRedis(
                    sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                    name: "redis",
                    tags: ["cache", "infrastructure"]);

            return services;
        }
    }
}
