namespace ProductionReadySetup.Common.Options
{
    /// <summary>
    /// Strongly-typed options for all observability concerns:
    /// logging, enrichment, and Datadog field mapping.
    ///
    /// WHY: Avoid magic strings scattered across configuration.
    /// All observability knobs live here and are validated at startup.
    ///
    /// BOUND TO: appsettings.json → "Observability" section
    /// </summary>
    public sealed class ObservabilityOptions
    {
        public const string SectionName = "Observability";

        /// <summary>
        /// Logical name of this service.
        /// Maps to: Serilog "Application" property + Datadog "dd.service"
        /// Example: "production-ready-api"
        /// </summary>
        public string ServiceName { get; init; } = "api";

        /// <summary>
        /// Deployment environment.
        /// Maps to: Serilog "Environment" property + Datadog "dd.env"
        /// Should match your Datadog environment tag: production, staging, development
        /// </summary>
        public string Environment { get; init; } = "development";

        /// <summary>
        /// Application version — ideally set from CI/CD pipeline (e.g., git tag or build number).
        /// Maps to: Datadog "dd.version"
        /// Allows you to correlate log spikes with specific deployments.
        /// </summary>
        public string Version { get; init; } = "1.0.0";

        /// <summary>
        /// Minimum log level for the application.
        /// PITFALL: Do not set to Verbose/Debug in production — log volume cost is real.
        /// Recommended: "Information" for prod, "Debug" for local dev.
        /// </summary>
        public string MinimumLevel { get; init; } = "Information";

        /// <summary>
        /// Override minimum level for noisy Microsoft/System namespaces.
        /// WHY: ASP.NET Core and EF Core are very chatty at Information level.
        /// Keep these at Warning unless you are actively debugging framework internals.
        /// </summary>
        public string MicrosoftLevel { get; init; } = "Warning";

        /// <summary>
        /// Whether to write logs to stdout in JSON format.
        /// Always true in containerized/cloud environments.
        /// Set to false only in local dev if you prefer human-readable console output.
        /// </summary>
        public bool WriteToConsoleJson { get; init; } = true;

        /// <summary>
        /// Whether to write logs to a rolling file.
        /// Useful for local dev and non-containerized deployments.
        /// PITFALL: In Kubernetes, writing to file inside the container is pointless
        /// — the agent reads stdout. Disable this in container environments.
        /// </summary>
        public bool WriteToFile { get; init; } = false;

        /// <summary>
        /// Path for rolling file logs. Only relevant when WriteToFile = true.
        /// </summary>
        public string LogFilePath { get; init; } = "logs/app-.log";

        /// <summary>
        /// Controls whether Serilog's request logging middleware is enabled.
        /// This replaces the noisy default ASP.NET Core request logging.
        /// Recommended: always true in production.
        /// </summary>
        public bool EnableRequestLogging { get; init; } = true;

        /// <summary>
        /// HTTP paths to exclude from request logging entirely.
        /// WHY: Health check probes (liveness/readiness) hit every 10 seconds.
        /// Logging them pollutes your log stream with zero signal.
        /// </summary>
        public string[] RequestLoggingExcludedPaths { get; init; } =
        [
            "/health",
            "/health/live",
            "/health/ready",
            "/metrics",
            "/favicon.ico"
        ];

        // Destrcuture properties for MaxDepth, MaxStringLength, and MaxCollectionCount to avoid magic numbers
        public int MaxDestructureDepth { get; init; } = 4;
        public int MaxStringLength { get; init; } = 1000;
        public int MaxCollectionCount { get; init; } = 10;
    }
}
