using ProductionReadySetup.Common.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── OBSERVABILITY (Track 2) ───────────────────────────────────────────────────
// Must be registered FIRST — before any other service that might log during
// startup (e.g. options validation, DI registration warnings).
// WHY FIRST: The bootstrap logger activates here and captures any startup errors.
// If this is registered late, early startup failures are invisible.
// PITFALL: Moving this below AddControllers() or AddProductionErrorHandling()
// risks losing logs from those registration steps.
builder.AddProductionObservability();

// ── CONTROLLERS ───────────────────────────────────────────────────────────────
// Registers MVC controller services + model validation pipeline.
// Model validation errors are caught and mapped by ProductionErrorHandling
// via the ApiBehaviorOptions.InvalidModelStateResponseFactory override.
builder.Services.AddControllers();

// ── ERROR HANDLING (Track 1) ──────────────────────────────────────────────────
// Registers GlobalExceptionHandler + ProblemDetails services.
// WHY after AddControllers: ApiBehaviorOptions (model validation mapping) is
// configured inside AddProductionErrorHandling and requires controllers to be
// registered first so the factory override applies correctly.
builder.Services.AddProductionErrorHandling();

// ── SWAGGER / OPENAPI ─────────────────────────────────────────────────────────
// Development-only API documentation. Safe to register always —
// exposure is controlled in the middleware pipeline below (IsDevelopment guard).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── BUILD ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── MIDDLEWARE PIPELINE ───────────────────────────────────────────────────────
// ORDER IS CRITICAL in ASP.NET Core middleware.
// Each middleware wraps everything below it.
// Wrong order = missing correlation IDs in logs, exceptions escaping handlers,
// auth checks running before error handling is ready, etc.

// [1] OBSERVABILITY — Correlation ID + Request Logging
// WHY FIRST: CorrelationIdMiddleware pushes correlationId into Serilog's LogContext.
// Every middleware and log statement below this automatically inherits it.
// Serilog request logging wraps the rest of the pipeline to capture
// the final status code and total elapsed time in one structured log line.
// PITFALL: If placed after error handling or auth, early log lines from those
// middlewares will lack correlationId — breaking log correlation in Datadog.
app.UseProductionObservability();

// [2] ERROR HANDLING — Global Exception Handler
// WHY SECOND: Must wrap the entire remaining pipeline so no exception escapes
// to the raw ASP.NET Core error page (which leaks stack traces in development
// and returns unstructured responses in production).
// PITFALL: Placing this after routing or auth means exceptions from those
// middlewares bypass the handler entirely.
app.UseProductionErrorHandling();

// [3] SWAGGER — Development only
// WHY HERE: Placed after error handling so any Swagger middleware faults
// are caught and returned as ProblemDetails, not raw 500 pages.
// Not exposed in production — gate is the environment check.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// [4] HTTPS REDIRECTION
// WHY HERE: Redirects HTTP → HTTPS before any auth or business logic runs.
// PITFALL: In Kubernetes behind a TLS-terminating ingress, this may cause
// redirect loops. Disable it there and let the ingress handle TLS.
app.UseHttpsRedirection();

// [5] AUTHENTICATION
// WHY BEFORE AUTHORIZATION: Authentication identifies WHO the user is.
// Authorization decides WHAT they can do. Identity must be established first.
// PITFALL: Flipping these means the auth middleware runs against an anonymous
// principal — all [Authorize] attributes silently fail open or throw.
app.UseAuthentication();

// [6] AUTHORIZATION
// WHY HERE: Runs after authentication so ClaimsPrincipal is populated.
// Enforces [Authorize] attributes on controllers and minimal API endpoints.
app.UseAuthorization();

// [7] CONTROLLERS
// WHY LAST: Route matching and controller execution is the innermost layer.
// All cross-cutting concerns (logging, error handling, auth) are already
// in place by the time a controller action executes.
app.MapControllers();

// ── RUN ───────────────────────────────────────────────────────────────────────
// Wrapped in try/catch to catch fatal errors during startup or shutdown
// that occur outside the middleware pipeline (before the first request is served).
// WHY: Exceptions thrown by app.Run() itself (port conflict, cert error, etc.)
// would otherwise exit silently without a log entry.
// CloseAndFlush ensures Serilog's async buffers are drained before process exits —
// without this, the last N log lines may be lost on crash or graceful shutdown.
// PITFALL: HostAbortedException is excluded — it is thrown intentionally by
// .NET's hosting infrastructure during graceful shutdown and is not an error.
try
{
    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}