using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using System.Diagnostics;

namespace ProductionReadySetup.Common.Observability
{
    /// <summary>
    /// Middleware that ensures every inbound request has a Correlation ID.
    ///
    /// BEHAVIOR:
    ///   1. Check if the incoming request carries an "X-Correlation-Id" header.
    ///      - If YES → trust it and use it (forwarded from upstream service/gateway).
    ///      - If NO  → generate a new one (this is the origin request).
    ///   2. Store it in HttpContext.Items for downstream code to access.
    ///   3. Push it into Serilog's LogContext so every log line in this request
    ///      automatically carries the correlationId field — no manual passing needed.
    ///   4. Echo it back in the response header so clients/callers can reference it
    ///      in support tickets or their own logs.
    ///
    /// PITFALL: This middleware must be registered BEFORE any middleware that logs
    /// (including Serilog request logging), otherwise early log lines will lack the
    /// correlationId enrichment.
    ///
    /// PITFALL: Do not blindly trust the incoming header value if it could be
    /// user-controlled and you use correlation IDs for security purposes.
    /// For pure observability, trusting it is fine and actually desirable.
    /// </summary>
    public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            // Step 1: Resolve or generate the correlation ID.
            // Prefer the inbound header (preserves upstream correlation chain).
            // Fall back to Activity.Current.Id (set by ASP.NET Core's activity tracking)
            // so it aligns with distributed tracing when OpenTelemetry is added later.
            // Final fallback: a fresh GUID.
            var correlationId = ResolveCorrelationId(context);

            // Step 2: Store in HttpContext.Items so any service/middleware can read it
            // via IHttpContextAccessor without knowing about HTTP headers.
            context.Items[CorrelationIdConstants.HttpContextItemKey] = correlationId;

            // Step 3: Push into Serilog's async log context.
            // LogContext.PushProperty is scoped to the current async execution context.
            // Every log statement downstream in this request will automatically
            // include { "CorrelationId": "..." } in the JSON output.
            using (LogContext.PushProperty(CorrelationIdConstants.LogPropertyName, correlationId))
            {
                // Step 4: Add to response header so the caller can reference it.
                // WHY: When a user reports an error, they can send you this ID
                // and you can instantly pull all related logs in Datadog.
                context.Response.OnStarting(() =>
                {
                    if (!context.Response.Headers.ContainsKey(CorrelationIdConstants.HeaderName))
                        context.Response.Headers[CorrelationIdConstants.HeaderName] = correlationId;
                    return Task.CompletedTask;
                });

                logger.LogDebug(
                    "CorrelationId resolved: {CorrelationId} for {Method} {Path}",
                    correlationId,
                    context.Request.Method,
                    context.Request.Path);

                await next(context);
            }
        }


        private static string ResolveCorrelationId(HttpContext context)
        {
            // Check forwarded header from upstream (API gateway, load balancer, caller service)
            if (context.Request.Headers.TryGetValue(
                    CorrelationIdConstants.HeaderName, out var headerValue)
                && !string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue.ToString();
            }

            // Use the current Activity ID if ASP.NET Core activity tracking is active.
            // This naturally aligns with W3C Trace Context when OTEL is added later.
            if (Activity.Current?.Id is { Length: > 0 } activityId)
                return activityId;

            // Origin request with no upstream context — mint a new one.
            return Guid.NewGuid().ToString("D"); // "D" = standard format with hyphens
        }
    }
}