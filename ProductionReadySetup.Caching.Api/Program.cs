using ProductionReadySetup.Caching.Api.Extensions;
using ProductionReadySetup.Common.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// ── OBSERVABILITY ─────────────────────────────────────────────────────────────
// From ProductionReadySetup.Common — shared across all API projects.
// Bootstrap logger activates here — captures startup errors before DI is ready.
builder.AddProductionObservability();

// ── CONTROLLERS ───────────────────────────────────────────────────────────────
builder.Services.AddControllers();

// ── EXCEPTION HANDLING ────────────────────────────────────────────────────────
// From ProductionReadySetup.Common — shared mechanism, domain exceptions defined locally.
builder.Services.AddProductionErrorHandling();

// ── REDIS CACHE ───────────────────────────────────────────────────────────────
// Registers IConnectionMultiplexer (singleton), IAppCache, CacheKeyBuilder,
// RedisOptions, CacheOptions, and Redis health check.
builder.Services.AddProductionRedisCache(builder.Configuration);

// ── SWAGGER ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── MIDDLEWARE PIPELINE ───────────────────────────────────────────────────────

// [1] Correlation ID + Serilog request logging — must be first
app.UseProductionObservability();

// [2] Global exception handler — wraps entire remaining pipeline
app.UseProductionErrorHandling();

// [3] Swagger — development only
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// [4] HTTPS redirection
app.UseHttpsRedirection();

// [5] Auth
app.UseAuthentication();
app.UseAuthorization();

// [6] Health checks
// Tags allow Kubernetes to hit /health/live (liveness) and /health/ready (readiness)
// separately — liveness excludes Redis (process alive check only),
// readiness includes Redis (is the app ready to serve traffic?).
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    // Readiness: only pass if Redis is healthy
    Predicate = check => check.Tags.Contains("infrastructure")
});
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    // Liveness: always pass if process is running — Redis being down ≠ process dead
    Predicate = _ => false
});

// [7] Controllers
app.MapControllers();

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