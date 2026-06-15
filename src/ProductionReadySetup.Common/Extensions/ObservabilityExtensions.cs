using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProductionReadySetup.Common.Observability;
using ProductionReadySetup.Common.Options;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace ProductionReadySetup.Common.Extensions
{
    public static class ObservabilityExtensions
    {
        public static WebApplicationBuilder AddProductionObservability(
            this WebApplicationBuilder builder) 
        {
            // ── 1. Bind and validate ObservabilityOptions ─────────────────────────────
            builder.Services
                .AddOptions<ObservabilityOptions>()
                .BindConfiguration(ObservabilityOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart(); // Fail fast — bad config crashes at startup, not runtime

            // Register DatadogLogEnricher in DI so it can receive IOptions<ObservabilityOptions>.
            // It will be resolved and handed to Serilog during logger configuration below.
            builder.Services.AddSingleton<DatadogLogEnricher>();

            // ── 2. Build Serilog logger configuration ─────────────────────────────────
            // We use two-stage initialization:
            //   Stage A (bootstrap): A minimal logger active BEFORE the DI container is built.
            //           - Captures startup errors (config failures, missing secrets, etc.)
            //   Stage B (full): The real logger with all enrichers, sinks, and options.
            //           - Activated once the DI container is ready via UseSerilog callback.

            // Stage A: Bootstrap logger — stdout only, minimal config, for startup visibility
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information() 
                .WriteTo.Console()
                .CreateBootstrapLogger();


            // Stage B: Full logger — registered via UseSerilog so it receives the built
            // service provider and can resolve ObservabilityOptions + DatadogLogEnricher.
            builder.Host.UseSerilog((hostContext, services, loggerConfig) =>
            {
                var options = services
                    .GetRequiredService<IOptions<ObservabilityOptions>>()
                    .Value;

                var ddEnricher = services
                    .GetRequiredService<IOptions<DatadogLogEnricher>>()
                    .Value;

                // ── Minimum levels ──────────────────────────────────────────────────
                var minLevel = ParseLogLevel(options.MinimumLevel);
                var msLevel = ParseLogLevel(options.MicrosoftLevel);

                loggerConfig
                .MinimumLevel.Is(minLevel)

                // Suppress noisy Microsoft/System namespaces to avoid log spam.
                // These namespaces log a LOT at Information (every request route match,
                // EF Core queries, etc.) which is noise in production.
                .MinimumLevel.Override("Microsoft", msLevel)
                .MinimumLevel.Override("Microsoft.AspNetCore", msLevel)
                .MinimumLevel.Override("System.Net.Http.HttpClient", msLevel)

                // ── Enrichers ───────────────────────────────────────────────────
                // Enrichers add ambient context to every log line automatically.
                // Order matters for readability — put stable fields first.
                .Enrich.FromLogContext()                      // picks up PushProperty calls (correlationId etc.)
                .Enrich.WithMachineName()                     // useful for multi-instance debugging
                .Enrich.WithEnvironmentName()                 // OS environment name
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.With(ddEnricher)                      // dd.service, dd.env, dd.version, dd.trace_id

                // Static properties that identify this service instance across all logs.
                // These complement the Datadog-specific fields for non-Datadog consumers.
                .Enrich.WithProperty("Application", options.ServiceName)
                .Enrich.WithProperty("Environment", options.Environment)
                .Enrich.WithProperty("Version", options.Version)

                // ── Destructuring (Security) ─────────────────────────────────────
                // Prevent accidental logging of sensitive objects.
                // WithDestructuringTo limits how deep Serilog will inspect an object
                // when you use {@obj} syntax. Keeps log size bounded.
                .Destructure.ToMaximumDepth(options.MaxDestructureDepth)
                .Destructure.ToMaximumStringLength(options.MaxStringLength)
                .Destructure.ToMaximumCollectionCount(options.MaxCollectionCount)


                // ── Sinks ────────────────────────────────────────────────────────
                // Sink = where logs are written.
                // We configure based on ObservabilityOptions to keep prod/dev flexible.
                .WriteTo.Conditional(
                    // JSON to stdout — PRIMARY sink for containerized environments.
                    // Datadog Agent reads stdout and ships it. No API key in app needed.
                    _ => options.WriteToConsoleJson,
                    writeTo => writeTo.Console(new CompactJsonFormatter()))


                .WriteTo.Conditional(
                    // Human-readable console — only useful for local development.
                    // Disable in production to avoid double-writing and format confusion.
                    _ => options.WriteToConsoleJson,
                    writeTo => writeTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} " +
                                        "{Properties:j}{NewLine}{Exception}"))

                .WriteTo.Conditional(
                    // Rolling file — useful for VMs or non-containerized deployments.
                    // PITFALL: Do NOT enable in Kubernetes. Container filesystem is
                    // ephemeral; files vanish on pod restart. Use stdout + agent instead.
                    _ => options.WriteToFile,
                    writeTo => writeTo.File(
                        new CompactJsonFormatter(),
                        options.LogFilePath,
                        rollingInterval: RollingInterval.Day, // new file each day
                        retainedFileCountLimit: 7, // keep 7 days of logs
                        fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB per file
                        rollOnFileSizeLimit: true)); // create a new file when the size limit is reached, instead of overwriting
            });
            return builder;
        }

        /// <summary>
        /// Phase 2: Register observability middleware into the HTTP pipeline.
        ///
        /// ORDERING IS CRITICAL:
        ///   CorrelationIdMiddleware must come FIRST — before any logging middleware —
        ///   so that the correlationId is in LogContext before the first log is written
        ///   for this request. If reversed, early logs lack the correlationId.
        ///
        ///   Serilog request logging comes AFTER routing but captures the full lifecycle
        ///   (status code, elapsed time) by wrapping everything underneath it.
        ///
        ///   Correct order:
        ///     1. CorrelationIdMiddleware  ← sets correlationId in LogContext
        ///     2. Serilog Request Logging  ← enriched with correlationId + route info
        ///     3. Auth, routing, controllers...
        /// </summary>
        public static WebApplication UseProductionObservability(this WebApplication app)
        {
            var options = app.Services
                .GetRequiredService<IOptions<ObservabilityOptions>>()
                .Value;

            // Step 1: Correlation ID — MUST be first.
            app.UseMiddleware<CorrelationIdMiddleware>();

            // Step 2: Serilog request logging middleware.
            // Replaces the default ASP.NET Core request logging (which is noisy and unstructured).
            // Produces ONE log line per request with: path, status, elapsed time, correlationId.
            if (options.EnableRequestLogging)
            {
                app.UseSerilogRequestLogging(requestOptions =>
                {
                    // Custom message template — what the one-per-request log line looks like.
                    // Keep it concise; the JSON payload carries the full structured data.
                    requestOptions.MessageTemplate =
                        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

                    // Enrich the request log with additional properties not captured by default.
                    requestOptions.EnrichDiagnosticContext = EnrichRequestLog;

                    // Filter out health check and metrics endpoints.
                    // WHY: Kubernetes probes hit /health every few seconds.
                    // Logging each probe adds thousands of low-value lines per hour.
                    requestOptions.GetLevel = (httpContext, elapsed, ex) =>
                    {
                        // Exclude configured paths entirely
                        if (IsExcludedPath(httpContext, options.RequestLoggingExcludedPaths))
                            return LogEventLevel.Verbose; // Verbose is below default minimum — effectively suppressed

                        // Exceptions always at Error
                        if (ex is not null)
                            return LogEventLevel.Error;

                        // 5xx → Error, 4xx → Warning, rest → Information
                        return httpContext.Response.StatusCode >= 500
                            ? LogEventLevel.Error
                            : httpContext.Response.StatusCode >= 400
                                ? LogEventLevel.Warning
                                : LogEventLevel.Information;
                    };
                });
            }

            return app;
        }

        // ── Private helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Enriches the Serilog request log with contextual fields.
        /// Called once per request when the response is complete (status code is known).
        ///
        /// SAFE FIELDS ONLY:
        ///   - No Authorization header
        ///   - No request/response body
        ///   - No query strings that might contain tokens
        ///   UserAgent and RemoteIp are borderline PII in GDPR contexts —
        ///   include them only if your compliance team approves.
        /// </summary>
        private static void EnrichRequestLog(
        IDiagnosticContext diagnosticContext,
        HttpContext httpContext)
        {
            // Request context
            diagnosticContext.Set("RequestMethod", httpContext.Request.Method);
            diagnosticContext.Set("RequestPath", httpContext.Request.Path.Value ?? "/");
            diagnosticContext.Set("RequestProtocol", httpContext.Request.Protocol);
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);

            // Response context (available because this runs after the response is sent)
            diagnosticContext.Set("StatusCode", httpContext.Response.StatusCode);
            diagnosticContext.Set("ContentType", httpContext.Response.ContentType ?? string.Empty);

            // Correlation ID — read from HttpContext.Items (set by CorrelationIdMiddleware)
            if (httpContext.Items.TryGetValue(
                    CorrelationIdConstants.HttpContextItemKey, out var correlationId)
                && correlationId is string cid)
            {
                diagnosticContext.Set("CorrelationId", cid);
            }

            // Authenticated user ID — only if request is authenticated.
            // PITFALL: Do NOT log email or username — only an opaque ID.
            // Email is PII and can end up in log aggregation tools with broad access.
            var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                diagnosticContext.Set("UserId", userId);
            }

            // Trace ID from ASP.NET Core's activity tracking.
            // Useful for correlating with APM even before full OTEL setup.
            var traceId = Activity.Current?.TraceId.ToString();
            if (!string.IsNullOrEmpty(traceId))
            {
                diagnosticContext.Set("TraceId", traceId);
            }

            // ── INTENTIONALLY OMITTED ────────────────────────────────────────────
            // Authorization header  → would log bearer tokens
            // Cookie header         → session tokens, auth cookies
            // Request body          → may contain passwords, PII
            // Response body         → may contain sensitive data
            // Full query string     → may contain API keys or tokens
            //   (RequestPath above is path-only, no query string)
        }


        private static bool IsExcludedPath(HttpContext context, string[] excludedPaths)
        {
            var path = context.Request.Path.Value;
            if (string.IsNullOrEmpty(path)) return false;

            foreach (var excluded in excludedPaths)
            {
                if (path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static LogEventLevel ParseLogLevel(string level) =>
        Enum.TryParse<LogEventLevel>(level, ignoreCase: true, out var result)
            ? result
            : LogEventLevel.Information;        
    }
}
