using ProductionReadySetup.Contracts.Events.V1;
using ProductionReadySetup.Messaging.Worker.Consumers;
using ProductionReadySetup.Messaging.Worker.Idempotency;
using ProductionReadySetup.Messaging.Worker.Options;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Worker.Extensions
{
    /// <summary>
    /// Registers all Worker-specific services on top of the shared
    /// RabbitMQ infrastructure from AddProductionRabbitMq().
    ///
    /// CALL ORDER IN Program.cs:
    ///   builder.Services.AddProductionRabbitMq();          ← shared infra
    ///   builder.Services.AddProductionMessagingConsumer(config); ← Worker-specific
    ///
    /// WHAT THIS REGISTERS:
    ///   - ConsumerOptions (validated at startup)
    ///   - IConnectionMultiplexer → Redis (for idempotency store)
    ///   - IIdempotencyStore → RedisIdempotencyStore
    ///   - OrderCreatedConsumer (IHostedService — runs the consumer loop)
    /// </summary>
    public static class MessagingWorkerExtensions
    {
        public static IServiceCollection AddProductionMessagingConsumer(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // ── 1. Consumer behavior options ──────────────────────────────────────
            services
                .AddOptions<ConsumerOptions>()
                .BindConfiguration(ConsumerOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // ── 2. Redis for idempotency store ────────────────────────────────────
            // Worker has its own Redis connection — independent of Caching.Api.
            // WHY: Worker is a separate deployable process. It cannot share
            // IConnectionMultiplexer with Caching.Api (different process, different pod).
            // Both connect to the same Redis instance but manage their own connections.
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(
                    configuration.GetConnectionString("Redis") 
                    ??  "localhost:6379"));
            // ── 3. Idempotency store ──────────────────────────────────────────────
            services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

            // ── 4. Consumer hosted service ────────────────────────────────────────
            // Registered as IHostedService — .NET starts it automatically
            // after all other hosted services (including RabbitMqTopologySetup).
            // WHY: Topology must exist before consumer tries to BasicConsume.
            services.AddHostedService<OrderCreatedConsumer>();

            return services;
        }

    }
}

