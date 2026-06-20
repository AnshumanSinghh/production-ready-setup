using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionReadySetup.Messaging.Connection;
using ProductionReadySetup.Messaging.Options;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace ProductionReadySetup.Messaging.Topology
{
    /// <summary>
    /// Declares RabbitMQ topology (exchanges, queues, bindings, DLX/DLQ) on startup.
    ///
    /// WHY IHostedService:
    ///   Runs automatically before the app starts accepting HTTP requests
    ///   or consuming messages. Guarantees topology exists before first use.
    ///
    /// IDEMPOTENT DECLARATION:
    ///   RabbitMQ allows redeclaring an exchange/queue with IDENTICAL settings
    ///   as a no-op. Redeclaring with DIFFERENT settings throws a channel
    ///   exception — this is intentional. It catches configuration drift
    ///   immediately at startup rather than allowing silent misconfiguration.
    ///
    /// PRODUCTION GUIDANCE (RabbitMqOptions.ActivelyDeclareTopology):
    ///   true  (dev/staging) → full declare, creates topology if missing.
    ///   false (production)  → passive declare only, verifies topology exists,
    ///                          throws if missing. Topology is owned by
    ///                          Infrastructure-as-Code (Terraform/CI pipeline)
    ///                          in production — app code never creates/modifies
    ///                          production topology.
    ///
    /// PREFER RABBITMQ POLICIES FOR DLX IN PRODUCTION:
    ///   We declare DLX via queue arguments here for simplicity and to keep
    ///   topology self-contained for this learning repo. In a mature production
    ///   setup, DLX/TTL/max-length are often managed via RabbitMQ Policies
    ///   (applied via rabbitmqctl or Management API) rather than queue arguments —
    ///   policies can be changed without redeclaring/recreating queues.
    ///   Document this as the production evolution path.
    /// </summary>
    public sealed class RabbitMqTopologySetup(
        RabbitMqConnectionProvider rabbitMqConnection,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqTopologySetup> logger) : IHostedService
    {
        private readonly RabbitMqOptions _options = options.Value;
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var connection = await rabbitMqConnection.GetConnectionAsync(cancellationToken);
            using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            if (_options.ActivelyDeclareTopology)
            {
                logger.LogInformation(
                    "Actively declaring RabbitMQ topology (dev/staging mode).");
                await DeclareTopologyAsync(channel, cancellationToken);
            }
            else
            {
                logger.LogInformation(
                    "Passively verifying RabbitMQ topology (production mode — " +
                    "topology owned by Infrastructure-as-Code).");
                await VerifyTopologyAsync(channel, cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;


        /// <summary>
        /// Full declaration — creates exchanges/queues/bindings if missing.
        /// Safe to run repeatedly as long as settings remain consistent.
        /// 
        /// FULL PICTURE, VISUALLY:
        /// Publisher sends message
        ///   │ to exchange: "production-ready.orders"
        ///   │ with routing key: "orders.created"
        ///   ▼
        /// [production-ready.orders exchange] (MAIN EXCHANGE)
        ///   │ (bound with routing key "orders.created")
        ///   ▼
        /// [orders.created queue] ◄── Worker consumes from here (MAIN QUEUE)
        ///   │
        ///   │ IF message is nacked/rejected (handled in Step 4, not yet built)
        ///   ▼
        /// [production-ready.orders.dlx exchange]   ← automatic, via queue arguments
        ///   │ (bound with routing key "orders.created")
        ///   ▼
        /// [orders.created.dlq queue] ◄── humans inspect failed messages here
        /// 
        /// MIND MAP:
        /// QueueBindAsync(queue, exchange, routingKey) creates the actual wire
        /// connecting a QUEUE to an EXCHANGE — declaring both alone does NOT
        /// connect them; without this bind, matching messages are silently dropped.
        /// </summary>
        private async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
        {
            // ── 1. Main exchange ────────────────────────────────────────────────

            //          WHY direct FOR US:
            // We have ONE producer(Messaging.Api) publishing OrderCreatedEvents
            // and ONE consumer queue(orders.created) for that exact event type.
            // No need for wildcards(topic) or broadcasting(fanout).
            // "Send OrderCreatedEvent to exactly the orders.created queue"
            // = direct exchange, exact routing key match.
            // Simple, predictable, no accidental misrouting.
            await channel.ExchangeDeclareAsync(
                exchange: _options.OrdersExchange,
                type: RabbitMqTopologyConstants.ExchangeTypeDirect,
                durable: true,      // survives broker restart
                autoDelete: false,  // PITFALL: never auto-delete production exchanges
                cancellationToken: ct);

            // ── 2. Dead-letter exchange ─────────────────────────────────────────
            await channel.ExchangeDeclareAsync(
                exchange: _options.OrdersDeadLetterExchange,
                type: RabbitMqTopologyConstants.ExchangeTypeDirect,
                durable: true,
                autoDelete: false,
                cancellationToken: ct);

            // ── 3. Dead-letter queue ─────────────────────────────────────────────
            // Declared BEFORE the main queue because the main queue's arguments
            // reference the DLX — the DLX and its bound queue should exist first
            // so messages are never momentarily unroutable.
            await channel.QueueDeclareAsync(
                queue: _options.OrderCreatedDeadLetterQueue,
                durable: true,
                exclusive: false,   // PITFALL: exclusive queues die with the connection — never use in production
                autoDelete: false,  // PITFALL: never auto-delete production queues,
                arguments: new Dictionary<string, object?>
                {
                    // Cap DLQ size to prevent unbounded growth during an incident.
                    // Oldest messages are dropped once limit is hit — acceptable
                    // tradeoff since DLQ is for investigation, not guaranteed storage.
                    [RabbitMqTopologyConstants.ArgMaxLength] = 100_00
                },
                cancellationToken: ct); // Explanation: physically creates the room (queue) where failed
                                        // messages will sit, waiting for a human to look at them.


            // Explanation: connects that room to the "failure sorting office"
            // (DLX)with a specific label(routing key). Without this bind, mail
            // sent to the DLX has nowhere to go — it would just vanish.
            // ===== it IS binding the DLQ to the DLX =======
            await channel.QueueBindAsync(
                queue: _options.OrderCreatedDeadLetterQueue,
                exchange: _options.OrdersDeadLetterExchange,
                routingKey: _options.OrderCreatedRoutingKey,
                cancellationToken: ct);

            // ── 4. Main queue (with DLX wiring) ─────────────────────────────────
            // creates the room "orders.created" The "arguments" dictionary is the
            // IMPORTANT part: it tells RabbitMQ "if a message in THIS queue gets
            // rejected/fails, automatically forward it to the DLX we set up earlier"
            // — this wiring is what makes failed messages automatically land in the
            // DLQ WITHOUT us writing any code to move them manually.
            await channel.QueueDeclareAsync(
                queue: _options.OrderCreatedQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    // On nack-without-requeue or message rejection, RabbitMQ
                    // routes the message to this exchange instead of discarding it.
                    [RabbitMqTopologyConstants.ArgDeadLetterExchange] = _options.OrdersDeadLetterExchange,
                    [RabbitMqTopologyConstants.ArgDeadLetterRoutingKey] = _options.OrderCreatedRoutingKey
                },
                cancellationToken: ct);

            // connects "orders.created" queue to the MAIN exchange
            // ("production-ready.orders") with the routing key "orders.created"
            await channel.QueueBindAsync(
                queue: _options.OrderCreatedQueue,                
                exchange: _options.OrdersExchange,
                routingKey: _options.OrderCreatedRoutingKey,
                cancellationToken: ct
                );

            logger.LogInformation(
                "RabbitMQ topology declared: Exchange={Exchange}, Queue={Queue}, DLX={Dlx}, DLQ={Dlq}",
                _options.OrdersExchange, _options.OrderCreatedQueue,
                _options.OrdersDeadLetterExchange, _options.OrderCreatedDeadLetterQueue);
        }

        /// <summary>
        /// Passive verification — throws if topology does not already exist.
        /// Used in production where Infrastructure-as-Code owns topology creation.
        /// </summary>
        private async Task VerifyTopologyAsync(IChannel channel, CancellationToken ct)
        {
            try
            {
                await channel.ExchangeDeclarePassiveAsync(_options.OrdersExchange, ct);
                await channel.QueueDeclarePassiveAsync(_options.OrderCreatedQueue, ct);

                logger.LogInformation(
                    "RabbitMQ topology verified successfully (production mode).");
            }
            catch (Exception ex)
            {
                // Fail fast — if topology is missing in production, something
                // went wrong in the deployment pipeline. Better to crash loudly
                // at startup than silently fail on first publish/consume.
                logger.LogCritical(ex,
                    "RabbitMQ topology verification failed. Expected topology " +
                    "was not found. Ensure Infrastructure-as-Code has applied " +
                    "topology before deploying this service.");
                throw;
            }
        }
    }
}
