using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProductionReadySetup.Caching.Api.Options;

namespace ProductionReadySetup.Caching.Api.Caching
{
    /// <summary>
    /// L1 in-process memory cache implementation of IAppCache.
    ///
    /// CHARACTERISTICS:
    ///   - Nanosecond access — no network, no serialization
    ///   - Pod-local — not shared across instances
    ///   - Lost on restart or pod recycle
    ///   - Bounded by process memory — set size limits in production
    ///
    /// WHEN TO USE DIRECTLY (via CacheProviderKeys.Memory):
    ///   - Rate limiting counters (pod-local is fine)
    ///   - Lookup tables loaded once at startup
    ///   - Data that is intentionally instance-specific
    ///   - Tests — no Redis infrastructure needed
    ///
    /// SERIALIZATION:
    ///   IMemoryCache stores objects by reference — no serialization needed.
    ///   Objects are stored as-is in memory.
    ///   PITFALL: Mutating a cached object mutates the cache entry too.
    ///   Always return clones or immutable types from the factory if mutation is possible.
    ///
    /// SIZE LIMITS:
    ///   IMemoryCache has no default size limit — it can grow unbounded.
    ///   In production, configure MemoryCacheOptions.SizeLimit and set
    ///   MemoryCacheEntryOptions.Size on each entry.
    ///   For this track we keep it simple — add size limits when you know
    ///   the expected entry count for your domain.
    /// </summary>
    public sealed class MemoryAppCache(
        IMemoryCache memoryCache,
        IOptions<CacheOptions> cacheOptions,
        ILogger<MemoryAppCache> logger) : IAppCache
    {
        private readonly CacheOptions _cacheOptions = cacheOptions.Value;
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            if (memoryCache.TryGetValue(key, out T? cached))
            {
                logger.LogInformation("Memory cache HIT - for key: {CacheyKey}", key);
                return Task.FromResult<T?>(cached);
            }

            logger.LogDebug("Memory cache MISS for key: {CacheKey}", key);
            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            // Use MemoryTtl from options — L1 TTL is always shorter than L2.
            // Caller-supplied TTL is intentionally ignored here.
            // WHY: L1 TTL is a system-level concern, not a per-call decision.
            // Allowing callers to set L1 TTL directly risks L1 outliving L2.
            var expiry = _cacheOptions.MemoryTtl;

            var entryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry,

                // PostEvictionCallback is useful for debugging cache churn in production.
                // Remove in high-throughput scenarios — callback adds overhead per eviction.
                PostEvictionCallbacks =
                {
                    new PostEvictionCallbackRegistration
                    {
                        EvictionCallback = (evictedKey, _value, reason, _state) =>
                            logger.LogDebug(
                                "Memory cache entry evicted. Key: {CacheKey}, Reason: {Reason}",
                            evictedKey, reason)
                    }
                }
            };

            memoryCache.Set(key, value, entryOptions);
            logger.LogDebug(
                "Memory cache SET for key: {CacheKey} with TTL: {Ttl}", key, expiry);

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            memoryCache.Remove(key);
            logger.LogInformation(
                "Memory cache REMOVE for {CacheKey}: ", key);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrSetAsync<T>(string key, 
            Func<CancellationToken, Task<T>> factory, 
            TimeSpan? ttl = null, 
            CancellationToken ct = default)
        {
            var cached = await GetAsync<T>(key, ct);
            if (cached is not null)
                return cached;

            var result = await factory(ct);

            if (result is not null)
                await SetAsync<T>(key, result, ttl, ct);

            return result;
        }
    }
}
