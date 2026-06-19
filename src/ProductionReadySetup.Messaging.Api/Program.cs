using ProductionReadySetup.Common.Extensions;
using ProductionReadySetup.Messaging.Extensions;
using ProductionReadySetup.Messaging.Api.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── OBSERVABILITY ─────────────────────────────────────────────────────────────
// From Common — bootstrap logger activates here.
builder.AddProductionObservability();

// ── CONTROLLERS ───────────────────────────────────────────────────────────────
builder.Services.AddControllers();

// ── EXCEPTION HANDLING ────────────────────────────────────────────────────────
// From Common — global exception handler + ProblemDetails.
builder.Services.AddProductionErrorHandling();

// ── RABBITMQ INFRASTRUCTURE ───────────────────────────────────────────────────
// From Messaging project — registers connection, topology setup,
// IMessagePublisher, and RabbitMQ health check.
// RabbitMqTopologySetup (IHostedService) runs on startup — declares
// exchanges, queues, and bindings before first request is served.
builder.Services.AddProductionRabbitMq();

// ── MESSAGING API SERVICES ────────────────────────────────────────────────────
// Api-specific — registers IOrderPublisher → OrderPublisher.
// Must be called AFTER AddProductionRabbitMq (depends on IMessagePublisher).
builder.Services.AddProductionMessagingPublisher();

// ── SWAGGER ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── MIDDLEWARE PIPELINE ───────────────────────────────────────────────────────

// [1] Correlation ID + Serilog request logging — must be first
app.UseProductionObservability();

// [2] Global exception handler
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
// liveness  → process alive check only (no RabbitMQ)
// readiness → includes RabbitMQ connection check
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("infrastructure")
});
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
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