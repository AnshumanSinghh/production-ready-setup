using Microsoft.AspNetCore.Mvc;
using ProductionReadySetup.Caching.Api.Caching;

namespace ProductionReadySetup.Caching.Api.Controllers;

/// <summary>
/// Demonstrates all three IAppCache providers via keyed service injection.
///
/// PRODUCTION NOTE: Controllers never call IAppCache directly in real systems.
/// Cache logic belongs in the service/application layer.
/// This controller exists purely to validate Track 3 + Track 4 end-to-end.
///
/// KEYED INJECTION:
///   [FromKeyedServices("hybrid")] → resolves HybridAppCache
///   [FromKeyedServices("redis")]  → resolves RedisAppCache
///   [FromKeyedServices("memory")] → resolves MemoryAppCache
/// </summary>
[ApiController]
[Route("api/cache-test")]
public sealed class CacheTestController(
    [FromKeyedServices(CacheProviderKeys.Hybrid)] IAppCache hybridCache,
    [FromKeyedServices(CacheProviderKeys.Redis)] IAppCache redisCache,
    [FromKeyedServices(CacheProviderKeys.Memory)] IAppCache memoryCache,
    CacheKeyBuilder keyBuilder,
    ILogger<CacheTestController> logger) : ControllerBase
{
    // ── Hybrid cache endpoints ────────────────────────────────────────────────

    [HttpGet("hubrid/{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var key = keyBuilder.Build("hybrid-test", id);

        // Cache-aside via GetOrSetAsync — controller stays thin,
        // cache logic is fully encapsulated in IAppCache.
        var result = await hybridCache.GetOrSetAsync(
            key,
            factory: async token =>
            {
                // Simulate source-of-truth fetch (DB call, API call, etc.)
                logger.LogInformation(
                    "Hybrid factory invoked for id: {Id} — fetching from source of truth", id);
                await Task.Delay(50, token); // stimulate latency
                return new { Id = id, Source = "database", Layer = "hybrid", FetchedAt = DateTime.UtcNow };
            },
            ttl: TimeSpan.FromMinutes(5),
            ct: ct);

        return Ok(result);
    }

    [HttpDelete("hybrid/{id}")]
    public async Task<IActionResult> InvalidateHybrid(string id, CancellationToken ct)
    {
        var key = keyBuilder.Build("hybrid-test", id);
        await hybridCache.RemoveAsync(key, ct);
        return NoContent();
    }

    // ── Redis-only endpoints ──────────────────────────────────────────────────

    [HttpGet("redis/{id}")]
    public async Task<IActionResult> GetRedis(string id, CancellationToken ct)
    {
        var key = keyBuilder.Build("redis-test", id);

        var result = await redisCache.GetOrSetAsync(
            key,
            factory: async token =>
            {
                logger.LogInformation(
                    "Redis factory invoked for id: {Id}", id);
                await Task.Delay(50, token);
                return new { Id = id, Source = "database", Layer = "redis", FetchedAt = DateTime.UtcNow };
            },
            ttl: TimeSpan.FromMinutes(5),
            ct: ct);

        return Ok(result);
    }

    // ── Memory-only endpoints ─────────────────────────────────────────────────

    [HttpGet("memory/{id}")]
    public async Task<IActionResult> GetMemory(string id, CancellationToken ct)
    {
        var key = keyBuilder.Build("memory-test", id);

        var result = await memoryCache.GetOrSetAsync(
            key,
            factory: async token =>
            {
                logger.LogInformation(
                    "Memory factory invoked for id: {Id}", id);
                await Task.Delay(50, token);
                return new { Id = id, Source = "database", Layer = "memory", FetchedAt = DateTime.UtcNow };
            },
            ct: ct);

        return Ok(result);
    }

    [HttpDelete("memory/{id}")]
    public async Task<IActionResult> Invalidate(string id, CancellationToken ct)
    {
        var key = keyBuilder.Build("test", id);
        await memoryCache.RemoveAsync(key, ct);
        return NoContent();
    }
}