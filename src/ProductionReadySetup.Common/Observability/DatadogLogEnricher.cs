using Microsoft.Extensions.Options;
using ProductionReadySetup.Common.Options;
using Serilog.Core;
using Serilog.Events;

namespace ProductionReadySetup.Common.Observability
{
    /// <summary>
    /// Serilog ILogEventEnricher that injects Datadog-required fields into every log event.
    ///
    /// WHY: Datadog Log Management uses specific field names to:
    ///   - Link logs to APM traces (dd.trace_id, dd.span_id)
    ///   - Filter/group by service, environment, version (dd.service, dd.env, dd.version)
    ///   Without these, your logs and traces exist as separate, uncorrelated silos.
    ///
    /// DATADOG FIELD CONTRACT:
    ///   dd.service  → appears as "Service" in Datadog log facets
    ///   dd.env      → appears as "Env" and maps to your Datadog environment
    ///   dd.version  → enables deployment tracking; spike after deploy? filter by version.
    ///   dd.trace_id → THE critical field: links a log line to an APM trace
    ///   dd.span_id  → links to the specific span within a trace
    ///
    /// HOW dd.trace_id WORKS:
    ///   ASP.NET Core + Datadog APM agent automatically sets Activity.Current when
    ///   a request is processed. The trace ID lives there. We extract and reformat it.
    ///   Without the Datadog .NET Tracer (dd-trace-dotnet), Activity.Current may be null
    ///   — the enricher gracefully handles that case.
    ///
    /// PITFALL: Datadog expects trace_id as a uint64 decimal string, NOT the W3C hex format.
    ///   W3C format: "4bf92f3577b34da6a3ce929d0e0e4736"
    ///   Datadog expects: "11803532876627986230"
    ///   We handle this conversion below.
    /// </summary>
    public sealed class DatadogLogEnricher(IOptions<ObservabilityOptions> observabilityOptions) : ILogEventEnricher
    {
        private readonly ObservabilityOptions _options = observabilityOptions.Value;

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            // ── Static fields (same for every log line in this process) ──────────────
            // These identify WHICH service, WHERE (env), and WHICH BUILD produced this log.

            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("dd.service", _options.ServiceName));

            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("dd.env", _options.Environment));

            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("dd.version", _options.Version));

            // ── Dynamic fields (per-request, from the active Activity/trace) ─────────
            // These link this specific log line to an APM trace in Datadog.

            var activity = System.Diagnostics.Activity.Current;
            if (activity is null) return;

            // Convert W3C TraceId (hex string, 32 chars) to Datadog's expected uint64 decimal.
            // Datadog uses the LOWER 64 bits of the 128-bit W3C trace ID.
            // WHY lower 64 bits: Datadog's legacy trace IDs are 64-bit.
            // W3C IDs are 128-bit; Datadog maps the last 16 hex chars → uint64.
            var traceId = ConvertTraceId(activity.TraceId.ToString());
            if (traceId is not null)
            {
                logEvent.AddPropertyIfAbsent(
                    propertyFactory.CreateProperty("dd.trace_id", traceId));
            }

            // SpanId: convert from 16-char hex to uint64 decimal (same logic, simpler).
            var spanId = ConvertoSpanId(activity.SpanId.ToString());
            if (spanId is not null)
            {
                logEvent.AddPropertyIfAbsent(
                    propertyFactory.CreateProperty("dd.span_id", spanId));
            }
        }


        /// <summary>
        /// Converts a W3C 32-char hex TraceId to Datadog's uint64 decimal string.
        /// Takes the LAST 16 hex characters (lower 64 bits) and converts to decimal.
        /// Returns null if the input is malformed or conversion fails.
        /// </summary>
        private static string? ConvertTraceId(string? w3cTraceId)
        {
            if (string.IsNullOrWhiteSpace(w3cTraceId) || w3cTraceId.Length < 16)
                return null;

            try
            {
                // Take last 16 hex chars = lower 64 bits of 128-bit W3C trace ID
                var lower64Hex = w3cTraceId[^16..]; // Slice syntax: take last 16 characters
                var value = Convert.ToUInt64(lower64Hex, 16);
                return value.ToString();
            }
            catch
            {
                return null; // Malformed trace ID — log without it rather than crash
            }
        }


        /// <summary>
        /// Converts a W3C 16-char hex SpanId to Datadog's uint64 decimal string.
        /// </summary>
        private static string? ConvertoSpanId(string? w3cSpanId)
        {
            if (string.IsNullOrWhiteSpace(w3cSpanId) || w3cSpanId.Length != 16)
                return null;
            try
            {
                var value = Convert.ToUInt64(w3cSpanId, 16);
                return value.ToString();
            }
            catch
            {
                return null; // Malformed span ID — log without it rather than crash
            }
        }
    }
}
