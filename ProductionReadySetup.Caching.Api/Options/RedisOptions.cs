namespace ProductionReadySetup.Caching.Api.Options
{
    /// <summary>
    /// Strongly-typed options for Redis connection configuration.
    ///
    /// WHY: No magic strings for connection strings or instance names.
    /// All Redis connection concerns live here and are validated at startup.
    ///
    /// BOUND TO: appsettings.json → "Redis" section
    ///
    /// PITFALL: Never hardcode the connection string in code.
    /// In production, inject it via environment variable or secrets manager
    /// (Azure Key Vault, AWS Secrets Manager, Kubernetes secrets).
    /// </summary>
    public sealed class RedisOptions
    {
        public const string SectionName = "Redis";

        /// <summary>
        /// Redis connection string.
        /// Local:      "localhost:6379"
        /// Production: "your-redis.cache.windows.net:6380,password=...,ssl=True"
        /// PITFALL: Never commit real connection strings to source control.
        /// </summary>
        public string ConnectionString { get; init; } = string.Empty;

        /// <summary>
        /// Logical name for this Redis connection instance.
        /// Used by IConnectionMultiplexer registration and health checks.
        /// Keep it stable — changing it requires updating health check config.
        /// </summary>
        public string InstanceName { get; init;  } = "default";

        /// <summary>
        /// Connection timeout in milliseconds.
        /// WHY 5000: Give Redis enough time to respond under load.
        /// Too low = false timeouts under GC pauses or network jitter.
        /// Too high = slow failure detection.
        /// </summary>
        public int ConnectTimeoutMs { get; init; } = 5000;

        /// <summary>
        /// Sync timeout in milliseconds for Redis operations.
        /// PITFALL: StackExchange.Redis has both connect timeout and sync timeout.
        /// Sync timeout applies to individual commands, not the initial connection.
        /// </summary>
        public int SyncTimeoutMs { get; init; } = 3000;

        /// <summary>
        /// Number of times StackExchange.Redis retries a failed connection.
        /// WHY 3: Handles transient network blips without hammering Redis.
        /// </summary>
        public int ConnectRetry { get; init; } = 3;

        /// <summary>
        /// Whether to use SSL/TLS for the Redis connection.
        /// Always true in production (Azure Cache for Redis, ElastiCache with TLS).
        /// False for local development only.
        /// </summary>
        public bool UseSsl { get; init; } = false;

        /// <summary>
        /// Redis database index. Default is 0.
        /// WHY: Some teams use separate DB indices per environment on shared Redis.
        /// PITFALL: Redis Cluster does not support multiple databases.
        /// Use separate Redis instances per environment in production instead.
        /// </summary>
        public int DatabaseIndex { get; init; } = 0;
    }
}
