using Microsoft.Extensions.Options;
using ProductionReadySetup.Caching.Api.Options;

namespace ProductionReadySetup.Caching.Api.Caching
{
    /// <summary>
    /// Builds structured, consistent, prefixed cache keys.
    ///
    /// WHY: Unstructured keys ("orders123", "user_abc") are a maintenance hazard.
    ///   - No namespace isolation between services
    ///   - No environment separation
    ///   - Impossible to pattern-scan safely
    ///   - Impossible to bulk-invalidate a resource type
    ///
    /// KEY FORMAT:
    ///   "{appPrefix}:{environment}:{resource}:{identifier}"
    ///   Example: "caching-api:production:orders:123"
    ///
    /// RULES:
    ///   - Always lowercase for consistency
    ///   - Colon (:) as separator — Redis convention
    ///   - No spaces, no special characters in segments
    ///
    /// PITFALL: Do not use KEYS command to scan by pattern in production.
    ///   KEYS blocks Redis while scanning. Use SCAN with MATCH instead.
    ///   We expose BuildPattern() for SCAN-based usage only.
    /// </summary>
    public sealed class CacheKeyBuilder(IOptions<CacheOptions> options)
    {
        private readonly CacheOptions _options = options.Value;

        /// <summary>
        /// Builds a fully qualified cache key for a specific resource entry.
        /// Example: Build("orders", "123") → "caching-api:production:orders:123"
        /// </summary>
        public string Build(string resource, string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            return $"{_options.AppPrefix}:{_options.Environment}:{resource}:{identifier}"
                    .ToLowerInvariant();
        }

        /// <summary>
        /// Builds a cache key for a collection/list resource.
        /// Example: BuildCollection("orders") → "caching-api:production:orders:__collection"
        ///
        /// WHY __collection suffix: Distinguishes a cached list from a cached item.
        /// Avoids collision between "orders:all" (ambiguous) and "orders:123" (item).
        /// </summary>
        public string BuildCollection(string resource) 
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);

            return $"{_options.AppPrefix}:{_options.Environment}:{resource}__collections"
                    .ToLowerInvariant();
        }

        /// <summary>
        /// Builds a SCAN-safe pattern for a resource type.
        /// Use this with SCAN MATCH — never with KEYS.
        /// Example: BuildPattern("orders") → "caching-api:production:orders:*"
        ///
        /// PITFALL: This pattern is for operational tooling and bulk invalidation only.
        /// Do not use in hot request paths.
        /// </summary>|
        public string BuildPattern(string resource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);

            return $"{_options.AppPrefix}:{_options.Environment}:{resource}*"
                    .ToLowerInvariant();
        }
    }
}
