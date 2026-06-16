using ProductionReadySetup.Caching.Api.Caching;

namespace ProductionReadySetup.Caching.Api.Extensions
{
    public static class HybridCacheExtensions
    {
        /// <summary>
        /// Registers Memory + Hybrid cache implementations alongside existing Redis cache.
        ///
        /// CALL ORDER IN Program.cs:
        ///   builder.Services.AddProductionRedisCache();    ← Track 3, registers Redis + options
        ///   builder.Services.AddProductionHybridCaching(); ← Track 4, registers Memory + Hybrid
        ///
        /// WHY SEPARATE EXTENSION METHODS:
        ///   Separation of concern — Redis setup is independent of hybrid setup.
        ///   A project that only needs Redis calls AddProductionRedisCache() only.
        ///   A project that needs hybrid calls both.
        ///
        /// KEYED SERVICE REGISTRATIONS:
        ///   "memory" → MemoryAppCache  (L1, pod-local)
        ///   "redis"  → RedisAppCache   (L2, distributed)  ← re-registered with key
        ///   "hybrid" → HybridAppCache  (L1 + L2 + stampede protection)
        ///
        /// WHY RE-REGISTER RedisAppCache WITH A KEY:
        ///   Track 3 registered IAppCache → RedisAppCache (unkeyed, default).
        ///   Track 4 adds keyed registrations so HybridAppCache can resolve
        ///   memory and redis by key via [FromKeyedServices].
        ///   Both registrations (keyed + unkeyed) coexist safely.
        ///   Unkeyed IAppCache still resolves RedisAppCache for existing call sites.
        /// </summary>
        public static IServiceCollection AddProductionHybridCaching(this IServiceCollection services)
        {
            // NOTE: Since, we are mainting Memory & Redis Options in a single CacheCacheOptions 
            // so we don't need to CONFIGURE & Validate it separetly. And I am not doing here as
            // it is already done on RedisCacheExtensions

            // ── 1. IMemoryCache (required by MemoryAppCache) ──────────────────────
            // AddMemoryCache is idempotent — safe to call multiple times.
            services.AddMemoryCache(options => 
            {
                // CompactionPercentage: when memory limit is hit, remove this
                // fraction of entries. 25% = compact aggressively but not fully.
                options.CompactionPercentage = 0.25;

                // ExpirationScanFrequency: how often IMemoryCache scans for
                // expired entries to evict. Default is 1 minute — acceptable.
                options.ExpirationScanFrequency = TimeSpan.FromMinutes(1);
            });


            // ── 2. Keyed: "memory" → MemoryAppCache ──────────────────────────────
            services.AddKeyedSingleton<IAppCache, MemoryAppCache>(CacheProviderKeys.Memory);

            // ── 3. Keyed: "redis" → RedisAppCache ────────────────────────────────
            // Re-register RedisAppCache under the "redis" key so HybridAppCache
            // can resolve it via [FromKeyedServices(CacheProviderKeys.Redis)].
            services.AddKeyedSingleton<IAppCache, RedisAppCache>(CacheProviderKeys.Redis);

            // ── 4. Keyed: "hybrid" → HybridAppCache ──────────────────────────────
            // HybridAppCache depends on keyed "memory" and "redis" —
            // resolved automatically via [FromKeyedServices] on its constructor.
            services.AddKeyedSingleton<IAppCache, HybridAppCache>(
                CacheProviderKeys.Hybrid);

            return services;
        }
    }
}
