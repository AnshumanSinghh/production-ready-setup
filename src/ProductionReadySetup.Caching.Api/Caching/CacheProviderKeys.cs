namespace ProductionReadySetup.Caching.Api.Caching
{
    /// <summary>
    /// Constants for keyed service resolution of IAppCache implementations.
    ///
    /// WHY: Magic strings for keyed service keys scattered across
    /// controllers and services are a maintenance hazard.
    /// One typo = wrong implementation resolved at runtime, no compile error.
    ///
    /// USAGE:
    ///   Registration:  services.AddKeyedSingleton<IAppCache, HybridAppCache>(CacheProviderKeys.Hybrid);
    ///   Resolution:    [FromKeyedServices(CacheProviderKeys.Hybrid)] IAppCache cache
    ///
    /// WHEN TO USE WHICH:
    ///   Memory  → ultra-fast, pod-local, non-shared data (rate limiting counters,
    ///             request-scoped lookups, data that differs per instance)
    ///   Redis   → shared state across pods, data that must be consistent
    ///             (inventory counts, feature flags, distributed locks)
    ///   Hybrid  → default choice for read-heavy, shared, expensive-to-fetch data
    ///             (product catalog, user profiles, configuration, reference data)
    /// </summary>
    public static class CacheProviderKeys
    {
        public const string Memory = "memory";
        public const string Redis = "redis";
        public const string Hybrid = "hybrid";
    }
}
