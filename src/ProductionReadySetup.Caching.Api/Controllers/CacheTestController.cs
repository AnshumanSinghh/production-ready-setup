using Microsoft.AspNetCore.Mvc;
using ProductionReadySetup.Caching.Api.Caching;

namespace ProductionReadySetup.Caching.Api.Controllers;

/// <summary>
/// Thin controller to demonstrate and manually test IAppCache behavior.
///
/// PRODUCTION NOTE: In a real system, controllers never call IAppCache directly.
/// Cache logic lives in the service/application layer.
/// This controller exists purely to validate the Track 3 setup end-to-end.
/// </summary>
[ApiController]
[Route("api/cache-test")]
public sealed class CacheTestController(
    IAppCache cache,
    CacheKeyBuilder keyBuilder,
    ILogger<CacheTestController> logger) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var key = keyBuilder.Build("test", id);

        // Cache-aside via GetOrSetAsync — controller stays thin,
        // cache logic is fully encapsulated in IAppCache.
        var result = await cache.GetOrSetAsync(
            key,
            factory: async token =>
            {
                // Simulate source-of-truth fetch (DB call, API call, etc.)
                logger.LogInformation(
                    "Factory invoked — fetching from source of truth for id: {Id}", id);

                await Task.Delay(50, token); // Simulate latency
                return new { Id = id, Value = $"data-for-{id}", FetchedAt = DateTime.UtcNow };
            },
            ttl: TimeSpan.FromMinutes(2),
            ct: ct);

        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Invalidate(string id, CancellationToken ct)
    {
        var key = keyBuilder.Build("test", id);
        await cache.RemoveAsync(key, ct);
        return NoContent();
    }
}