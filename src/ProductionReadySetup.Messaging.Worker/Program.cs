using ProductionReadySetup.Common.Extensions;
using ProductionReadySetup.Messaging.Extensions;
using ProductionReadySetup.Messaging.Worker.Extensions;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// ── RABBITMQ INFRASTRUCTURE ───────────────────────────────────────────────────
// From Messaging project — shared connection, topology setup, health check.
// RabbitMqTopologySetup (IHostedService) runs FIRST — declares/verifies
// exchanges and queues before OrderCreatedConsumer starts consuming.
builder.Services.AddProductionRabbitMq();


// ── CONSUMER SERVICES ─────────────────────────────────────────────────────────
// Worker-specific — ConsumerOptions, Redis idempotency store,
// OrderCreatedConsumer (IHostedService consumer loop).
// Must be called AFTER AddProductionRabbitMq — depends on connection provider.
builder.Services.AddProductionMessagingConsumer(builder.Configuration);

// ── HEALTH CHECKS ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();


//builder.Services.AddHostedService<Worker>();

var host = builder.Build();

try
{
    host.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Messaging Worker terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
