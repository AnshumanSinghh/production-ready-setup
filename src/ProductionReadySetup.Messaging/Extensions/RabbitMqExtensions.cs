using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProductionReadySetup.Messaging.Connection;
using ProductionReadySetup.Messaging.HealthChecks;
using ProductionReadySetup.Messaging.Options;
using ProductionReadySetup.Messaging.Publishing;
using ProductionReadySetup.Messaging.Topology;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Extensions
{
    /// <summary>
    /// Registers core RabbitMQ infrastructure shared by both producer and consumer.
    ///
    /// USAGE:
    ///   Messaging.Api Program.cs:    builder.Services.AddProductionRabbitMq(...);
    ///   Messaging.Worker Program.cs: builder.Services.AddProductionRabbitMq(...);
    ///
    /// WHAT THIS REGISTERS:
    ///   - RabbitMqOptions (validated at startup)
    ///   - RabbitMqConnectionProvider (singleton, owns the shared IConnection)
    ///   - RabbitMqTopologySetup (IHostedService — runs on startup)
    ///   - IMessagePublisher → RabbitMqMessagePublisher
    ///   - RabbitMQ health check
    ///
    /// WHAT THIS DOES NOT REGISTER:
    ///   - Consumer hosted services (Worker-specific, registered via
    ///     AddProductionMessagingConsumer in Step 4)
    ///   - Idempotency store (Worker-specific, uses Redis directly)
    ///
    /// WHY IMessagePublisher IS REGISTERED HERE, NOT IN A SEPARATE
    /// AddProductionMessagingPublisher METHOD:
    ///   The publisher has zero Api-specific concerns — it is pure RabbitMQ
    ///   transport. Messaging.Api's own extension method (Step 3) will be
    ///   thinner, focused on envelope construction and correlation wiring,
    ///   and will call THIS method internally as its foundation.
    /// </summary>
    public static class RabbitMqExtensions
    {
        public static IServiceCollection AddProductionRabbitMq(this IServiceCollection services)
        {
            // ── 1. Bind and validate options ──────────────────────────────────────
            services
                .AddOptions<RabbitMqOptions>()
                .BindConfiguration(RabbitMqOptions.SectionName)
                .ValidateDataAnnotations() // In case of .dll service add the DataAnnotations package explicitly
                .ValidateOnStart(); // fail fast on bad config at startup

            // ── 2. Connection provider — singleton, owns the shared IConnection ───
            services.AddSingleton<RabbitMqConnectionProvider>();

            // ── 3.Topology setup — runs once on application startup ──────────────
            services.AddSingleton<RabbitMqTopologySetup>();

            // ── 4. Publisher abstraction ───────────────────────────────────────────
            services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();

            // ── 5. Health check ────────────────────────────────────────────────────
            services
                .AddHealthChecks()
                .AddCheck<RabbitMqHealthCheck>(
                    name: "rabbitmq",
                    tags: ["messaging", "infrastructure"]);

            return services;
        }
    }
}

