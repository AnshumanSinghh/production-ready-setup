namespace ProductionReadySetup.Caching.Api.Caching
{
    /// <summary>
    /// Application cache abstraction.
    ///
    /// WHY AN INTERFACE:
    ///   - Testability: unit tests inject a mock/fake, no Redis needed
    ///   - Swappability: swap Redis for Memcached, NCache, or in-memory
    ///     without changing any call site
    ///   - Multiple implementations: Track 4 adds HybridAppCache alongside
    ///     RedisAppCache — keyed services resolve the right one
    ///
    /// DESIGN DECISIONS:
    ///   - Generic methods only: no object/dynamic typing
    ///     → type safety at compile time, no runtime cast failures
    ///   - TTL is optional: falls back to CacheOptions.DefaultTtl
    ///   - CancellationToken on all async methods: production requirement,
    ///     allows caller to cancel in-flight cache operations on timeout
    ///   - Factory delegate on GetOrSetAsync: cache-aside pattern built in,
    ///     caller never manually checks cache
    /// </summary>
    public interface IAppCache
    {
        /// <summary>
        /// Gets a cached value by key.
        /// Returns null if key does not exist or has expired.
        /// </summary>
        Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

        /// <summary>
        /// Sets a value in cache with optional TTL.
        /// TTL falls back to CacheOptions.DefaultTtl if not provided.
        /// Jitter is applied automatically to the TTL.
        /// </summary>
        Task SetAsync<T>(string key,  T value, TimeSpan? ttl = null, CancellationToken ct = default);

        /// <summary>
        /// Removes a cache entry by key.
        /// No-op if key does not exist — safe to call unconditionally on update/delete.
        /// </summary>
        Task RemoveAsync(string key, CancellationToken ct = default);

        /// <summary>
        /// Cache-aside pattern in one call.
        ///
        /// FLOW:
        ///   1. Check cache for key
        ///   2. HIT  → return cached value (fast path, factory not called)
        ///   3. MISS → call factory (DB query, API call, etc.)
        ///           → store result in cache with TTL + jitter
        ///           → return result
        ///
        /// WHY FACTORY DELEGATE:
        ///   Deferred execution — factory only runs on cache miss.
        ///   Keeps call sites clean: no if/else cache check needed.
        ///
        /// PITFALL: Factory must be idempotent — it may be called
        /// concurrently under high load (stampede not fully prevented
        /// until Track 4 adds SemaphoreSlim per key).
        /// </summary>
        Task<T> GetOrSetAsync<T>(string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default);
    }
}
