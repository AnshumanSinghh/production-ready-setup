using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ProductionReadySetup.Common.Exceptions;

namespace ProductionReadySetup.Common.ErrorHandling
{
 
    /// <summary>
    /// Centralized exception handler for the entire HTTP pipeline.
    /// Implements <see cref="IExceptionHandler"/> — registered via AddExceptionHandler()
    /// and invoked automatically by ASP.NET Core's exception handling middleware.
    ///
    /// RESPONSIBILITIES:
    ///   1. Intercept all unhandled exceptions before they reach the client.
    ///   2. Map exceptions to RFC 9457-compliant ProblemDetails responses.
    ///   3. Log with the correct severity and structured properties.
    ///   4. Guarantee no internal details (stack traces, connection strings,
    ///      inner exception messages) are ever leaked to the client.
    ///
    /// LOGGING STRATEGY:
    ///   AppException  → LogWarning, no stack trace.
    ///                   These are known, expected application states
    ///                   (not found, validation failed, unauthorized).
    ///                   They are not bugs. Stack traces add noise, not signal.
    ///                   High volume in production — keep logs lean.
    ///
    ///   Unknown       → LogError, WITH stack trace (exception parameter).
    ///                   These are bugs or infrastructure failures.
    ///                   Full stack trace is essential for root cause analysis.
    ///                   This is the log that should trigger your on-call alert.
    ///
    /// STRUCTURED LOG PROPERTIES (every log line):
    ///   {ErrorCode}     → machine-readable code for alerting and filtering
    ///   {CorrelationId} → ties this log to all other logs in the same request chain
    ///   {TraceId}       → ASP.NET Core trace identifier for APM correlation
    ///   {ExceptionType} → (unknown exceptions only) the exact CLR type for quick triage
    ///
    /// SAFETY GUARANTEES:
    ///   - Stack traces are logged server-side only, never returned to the client.
    ///   - Inner exception messages are suppressed in the response.
    ///   - Unknown exception responses always return a generic, safe message.
    ///   - If the response has already started (HasStarted = true), we log and
    ///     bail out — headers are committed, nothing safe can be written.
    ///
    /// NOTE ON AppException SEVERITY:
    ///   The base rule is AppException = Warning, no stack trace.
    ///   As the exception hierarchy grows, specific subclasses (e.g. DataCorruptionException,
    ///   DependencyFailureException) should declare LogLevel = Error and IncludeStackTrace = true
    ///   on the exception itself — not here. This handler should stay policy-free;
    ///   severity policy belongs on the exception definition.
    /// </summary>

    public sealed class GlobalExceptionHandler : IExceptionHandler
    {

        private readonly ILogger<GlobalExceptionHandler> _logger;
        private readonly IProblemDetailsService _problemDetailsService;
        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetailsService)
        {
            _logger = logger;
            _problemDetailsService = problemDetailsService;
        }


        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext, 
            Exception exception, 
            CancellationToken cancellationToken)
        {            
            if (httpContext.Response.HasStarted)
            {
                _logger.LogError(exception, "Exception occurred after response started.");
                return false;
            }

            var problemDetails = exception switch
            {
                AppException appException => appException.ToProblemDetails(httpContext),
                _ => ProblemDetailsExtensions.ToUnExpectedProblemDetials(httpContext)
            };

            httpContext.Response.StatusCode = 
                problemDetails.Status ?? StatusCodes.Status500InternalServerError;

            httpContext.Response.ContentType = "application/problem+json";

            // Pull correlationId from HttpContext.Items — set upstream by CorrelationIdMiddleware.
            // Already in Serilog's LogContext automatically, but we include it explicitly
            // as a structured property so it's searchable as a discrete field in Datadog.
            var correlationId = httpContext.Items
                .TryGetValue(CorrelationIdConstants.HttpContextItemKey, out var cid)
                    ? cid as string ?? "unknown"
                    : "unknown";


            if (exception is AppException)
            {
                // WARNING not ERROR — this is a known, expected application state.
                // No 'exception' parameter here intentionally — stack traces for known
                // errors are noise. errorCode alone is enough to act on.
                _logger.LogWarning(
                    "Handled application exception. ErrorCode: {ErrorCode} | " +
                    "CorrelationId: {CorrelationId} | TraceId: {TraceId}",
                    problemDetails.Extensions["errorCode"],
                    correlationId,
                    httpContext.TraceIdentifier);
            }
            else 
            {
                // ERROR — unknown exception. Log WITH exception for full stack trace.
                // This is the log that should trigger your on-call alert.
                _logger.LogError(
                    exception,
                    "Unhandled exception. CorrelationId: {CorrelationId} | TraceId: {TraceId} | " +
                    "ExceptionType: {ExceptionType}",
                    correlationId,
                    httpContext.TraceIdentifier,
                    exception.GetType().Name);
            }

            var written = await _problemDetailsService.TryWriteAsync(
                    new ProblemDetailsContext
                    {
                        HttpContext = httpContext,
                        ProblemDetails = problemDetails,
                        Exception = exception,
                    });


            if (!written)
            {
                await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
            }

            return true;
        }
    }
}
