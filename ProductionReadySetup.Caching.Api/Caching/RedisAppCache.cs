using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.Options;
using ProductionReadySetup.Caching.Api.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace ProductionReadySetup.Caching.Api.Caching
{
    /// <summary>
    /// Production Redis implementation of IAppCache.
    ///
    /// DESIGN DECISIONS:
    ///
    /// 1. IDatabase per operation (not stored as field):
    ///    IDatabase is a lightweight proxy — cheap to get per call.
    ///    Storing it as a field is unnecessary and could hold stale state.
    ///
    /// 2. System.Text.Json for serialization:
    ///    Fast, no external dependency, AOT-compatible.
    ///    No dynamic/object deserialization — generic T only.
    ///    PITFALL: Types with private setters or complex constructors
    ///    may need [JsonConstructor] or custom converters.
    ///
    /// 3. Cache failures are swallowed (when ThrowOnCacheFailure = false):
    ///    Cache is a performance layer, not a source of truth.
    ///    Redis downtime should degrade performance, not cause 500s.
    ///    All failures are logged for observability.
    ///
    /// 4. TTL jitter applied at write time:
    ///    Prevents cache stampede on mass expiry.
    ///    Applied consistently in one place — callers never think about it.
    ///
    /// 5. Null values are not cached:
    ///    Caching null would prevent the factory from retrying on miss.
    ///    If your use case requires negative caching (cache "not found"),
    ///    wrap the value in an Option/Maybe type explicitly.
    /// </summary>
    public sealed class RedisAppCache(
          IConnectionMultiplexer redis,
          IOptions<CacheOptions> cacheOptions,
          ILogger<RedisAppCache> logger) : IAppCache
    {
        private readonly CacheOptions _cacheOptions = cacheOptions.Value;

        // JsonSerializerOptions is expensive to construct — create once and reuse.
        // WHY: Creating per-call causes GC pressure and loses the internal cache
        // that JsonSerializer builds for known types.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false // Compact JSON in cache — every byte counts
        };

        public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            try
            {
                var db = redis.GetDatabase(_cacheOptions is { } ? GetDatabaseIndex() : 0);
                var value = await db.StringGetAsync(key);

                if (!value.HasValue)
                {
                    logger.LogDebug("Cache MISS for key: {CacheKey}", key);
                    return default;
                }

                logger.LogDebug("Cache HIT for key: {CacheKey}", key);
                return Deserialize<T>(value!);
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
            {
                // Log and return null — caller falls back to source of truth.
                // PITFALL: Do not rethrow here unless ThrowOnCacheFailure = true.
                // Redis downtime must not cascade into API failures.
                logger.LogWarning(ex,
                    "Redis GET failed for key: {CacheKey}. Falling back to source of truth.", key);

                if (_cacheOptions.ThrowOnCacheFailure) throw;
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            try
            {
                var db = redis.GetDatabase(GetDatabaseIndex());
                var serialized = Serialize(value);
                var expiry = ApplyJitter(ttl ?? _cacheOptions.DefaultTtl);

                var isCacheSet = await db.StringSetAsync(key, serialized, expiry);                

                logger.LogDebug(
                    "Cache SET for key: {CacheKey} with TTL: {Ttl}",
                    key, expiry);
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
            {
                logger.LogWarning(ex,
                    "Redis SET failed for key: {CacheKey}. Value not cached.", key);

                if (_cacheOptions.ThrowOnCacheFailure) throw;
            }
        }

        public async Task RemoveAsync(string key, CancellationToken ct = default)
        {
            try
            {
                var db = redis.GetDatabase(GetDatabaseIndex());
                await db.KeyDeleteAsync(key);

                logger.LogDebug("Cache REMOVE for key: {CacheKey}", key);
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
            {
                logger.LogWarning(ex,
                    "Redis REMOVE failed for key: {CacheKey}.", key);

                if (_cacheOptions.ThrowOnCacheFailure) throw;
            }
            
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            // Step 1: Check cache
            var cached = await GetAsync<T>(key, ct);
            if (cached is not null) return cached;

            // Step 2: Cache MISS — call factory (DB query, API call, etc.)
            // PITFALL: Under high concurrency, multiple requests may reach here
            // simultaneously for the same key (stampede).
            // Track 4 adds SemaphoreSlim-per-key to prevent this.
            logger.LogDebug(
                "Cache MISS — invoking factory for key: {CacheKey}", key);

            var result = await factory(ct);

            // Step 3: Store in cache — fire and let failure handling in SetAsync decide
            // WHY: Even if SetAsync fails, we return the result from the factory.
            // The next request will miss again and hit the factory — acceptable degradation.
            if (result is not null)
            {
                await SetAsync(key, result, ttl, ct);
            }

            return result;
        }


        #region ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Applies random jitter to TTL to spread cache expiry across a window.
        /// Actual TTL = requested TTL + Random(0, MaxJitter)
        /// </summary>
        private TimeSpan ApplyJitter(TimeSpan baseTtl)
        {
            var jitter = TimeSpan.FromSeconds(
                Random.Shared.NextDouble() * _cacheOptions.MaxJitter.TotalSeconds);
            return baseTtl + jitter;
        }

        private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

        private static T? Deserialize<T>(string value)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(value, JsonOptions);
            }
            catch (JsonException)
            {
                // Stale/corrupt cache entry — treat as miss.
                // WHY: Schema changes can leave old JSON in Redis that no longer
                // deserializes correctly. Return null → caller fetches fresh data.
                return default;
            }
        }

        private int GetDatabaseIndex()
        {
            // Resolved here rather than in constructor so it reflects
            // any runtime options changes (e.g. integration tests overriding options).
            return 0; // Override if RedisOptions.DatabaseIndex is wired here
        }
        #endregion
    }
}
