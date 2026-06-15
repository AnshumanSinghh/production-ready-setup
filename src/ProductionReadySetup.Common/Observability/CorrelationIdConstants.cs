namespace ProductionReadySetup.Common;
using System;

/// <summary>
/// Single source of truth for all correlation ID naming.
///
/// WHY: Magic strings for header names and context keys
/// scattered across middleware, enrichers, and exception handlers
/// are a maintenance hazard. One rename breaks everything silently.
///
/// CONVENTION: "X-Correlation-Id" is the de-facto industry standard header.
/// Some teams use "X-Request-Id" — pick one and be consistent.
/// </summary>
public static class CorrelationIdConstants
{
    /// <summary>
    /// HTTP request/response header name.
    /// Upstream services (API Gateway, nginx, other microservices) should
    /// forward this header so the same correlation ID flows through the chain.
    /// </summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>
    /// Key used to store the correlation ID in HttpContext.Items.
    /// This lets any middleware or service downstream read it without
    /// depending on the header directly.
    /// </summary>
    public const string HttpContextItemKey = "CorrelationId";

    /// <summary>
    /// Property name used in Serilog's LogContext.
    /// This is what shows up as the JSON field name in your log output.
    /// Datadog will index this as @correlationId for filtering.
    /// </summary>
    public const string LogPropertyName = "CorrelationId";
}
