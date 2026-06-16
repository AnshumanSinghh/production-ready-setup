using Microsoft.Extensions.Options;
using ProductionReadySetup.Caching.Api.Options;
using System.Collections.Concurrent;

namespace ProductionReadySetup.Caching.Api.Caching
{
    /// <summary>
    /// L1 (Memory) + L2 (Redis) hybrid cache implementation with stampede protection.
    ///
    /// FLOW:
    ///   GET:
    ///     1. Check L1 (memory)   → HIT: return immediately
    ///     2. Check L2 (Redis)    → HIT: populate L1, return
    ///     3. Acquire per-key lock (stampede protection)
    ///     4. Re-check L1 + L2 inside lock (another request may have populated while waiting)
    ///     5. Call factory        → populate L2, populate L1, return
    ///
    ///   SET:
    ///     Write to L2 (Redis) then L1 (memory).
    ///
    ///   REMOVE:
    ///     Remove from both L1 and L2.
    ///
    /// STAMPEDE PROTECTION:
    ///   SemaphoreSlim per cache key — only ONE factory call per key per pod.
    ///   LIMITATION: Pod-local only. Two pods can still call factory simultaneously.
    ///   True distributed protection requires Redis SETNX locking — out of scope here.
    ///   SemaphoreSlim covers the 99% case for single-pod or moderate multi-pod load.
    ///
    /// LOCK LIFECYCLE:
    ///   Locks are stored in a ConcurrentDictionary and never removed.
    ///   WHY: Removing locks after use introduces a race condition where one thread
    ///   removes the lock while another is about to acquire it.
    ///   Memory cost: one SemaphoreSlim (~200 bytes) per unique cache key ever seen.
    ///   For bounded key spaces (orders:123, users:456) this is negligible.
    ///   PITFALL: Unbounded key spaces (cache key includes timestamp, random ID)
    ///   will cause memory growth. Use hybrid cache only with stable, bounded key sets.
    /// </summary>
    public sealed class HybridAppCache(
        [FromKeyedServices(CacheProviderKeys.Memory)] IAppCache memoryCache,
        [FromKeyedServices(CacheProviderKeys.Redis)] IAppCache redisCache,
        IOptions<CacheOptions> cacheOptions,
        ILogger<HybridAppCache> logger) : IAppCache
    {
        private readonly CacheOptions _cacheOptions = cacheOptions.Value;

        // Per-key semaphores for stampede protection.
        // ConcurrentDictionary ensures thread-safe creation of new locks.
        // GetOrAdd is atomic — two threads requesting the same key always
        // get the same SemaphoreSlim instance.
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

        public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            // Step 1: L1 — fastest path, no network
            var memResult = await memoryCache.GetAsync<T?>(key, ct);
            if (memResult is not null)
            {
                logger.LogDebug("Hybrid cache L1 HIT for key: {CacheKey}", key);
                return memResult;
            }

            // Step 2: L2 — Redis, shared across pods
            var redisResult = await redisCache.GetAsync<T?>(key, ct);
            if (redisResult is not null)
            {
                logger.LogDebug(
                    "Hybrid cache L2 HIT for key: {CacheKey} — backfilling L1", key);

                // Backfill L1 so next request is served from memory
                await memoryCache.SetAsync(key, redisResult, null, ct);
                return memResult;
            }

            logger.LogDebug("Hybrid cache MISS (L1 + L2) for key: {CacheKey}", key);
            return default;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            // Write L2 first — Redis is the shared source.
            // L1 is populated on next read, not eagerly here.
            // WHY: Writing L1 here on multi-pod setup means other pods
            // still have stale L1 — Redis is the consistent layer.
            await redisCache.SetAsync<T>(key, value, ttl, ct);
            await memoryCache.SetAsync<T>(key, value, ttl, ct);

            logger.LogDebug("Hybrid cache SET for key: {CacheKey}", key);
        }

        public async Task RemoveAsync(string key, CancellationToken ct = default)
        {
            // Remove from both layers — order matters.
            // Remove L2 first so other pods stop serving from Redis immediately.
            // Then remove L1 on this pod.
            await redisCache.RemoveAsync(key, ct);
            await memoryCache.RemoveAsync(key, ct);

            logger.LogDebug("Hybrid cache REMOVE for key: {CacheKey}", key);
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            // ── Fast path (no lock) ───────────────────────────────────────────────
            // Check L1 + L2 before acquiring the lock.
            // WHY: Lock acquisition has overhead. Most requests will HIT here
            // and never need the lock. Only true misses proceed to lock.
            var cached = await GetAsync<T>(key, ct);
            if (cached is not null)
                return cached;

            // ── Slow path (with lock) ─────────────────────────────────────────────
            // Acquire per-key semaphore to prevent stampede.
            var keyLock = _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await keyLock.WaitAsync(ct);

            try
            {
                // ── Double-check inside lock ──────────────────────────────────────
                // WHY: Another request may have populated the cache while we
                // waited for the lock. Without this check, we call factory
                // unnecessarily even though data is now available.
                // This is the classic double-checked locking pattern.
                var doubleChecked = await GetAsync<T>(key, ct);
                if (doubleChecked is not null)
                {
                    logger.LogDebug(
                        "Hybrid cache double-check HIT for key: {CacheKey} — factory skipped", key);
                    return doubleChecked;
                }

                // ── Factory call ──────────────────────────────────────────────────
                // Only ONE request per key reaches here at a time (per pod).
                logger.LogDebug(
                    "Hybrid cache invoking factory for key: {CacheKey}", key);

                var result = await factory(ct);

                if (result is not null)
                {
                    // Populate L2 then L1
                    await redisCache.SetAsync(key, result, ttl, ct);
                    await memoryCache.SetAsync(key, result, ttl, ct);
                }

                return result;
            }
            finally
            {
                // Always release — even if factory throws.
                // Without finally, a factory exception leaves the lock permanently
                // acquired → all subsequent requests for this key hang forever.
                keyLock.Release();
            }
        }

    }
}
