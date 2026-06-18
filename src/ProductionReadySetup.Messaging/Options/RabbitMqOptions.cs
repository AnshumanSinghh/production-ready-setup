using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Options
{
    /// <summary>
    /// Strongly-typed options for RabbitMQ connection and topology configuration.
    ///
    /// WHY: No magic strings for host, credentials, or topology names
    /// scattered across Messaging.Api and Messaging.Worker.
    /// Both projects bind this SAME options class from their own appsettings.json.
    ///
    /// BOUND TO: appsettings.json → "RabbitMq" section
    ///
    /// PITFALL: Never hardcode credentials. Use environment variables or
    /// secrets manager in production. Local dev guest/guest is fine ONLY
    /// because RabbitMQ guest user cannot connect remotely by default.
    /// </summary>
    public sealed class RabbitMqOptions
    {
        public const string SectionName = "RabbitMq";

        /// <summary>
        /// RabbitMQ broker hostname.
        /// Local: "localhost". Production: managed service endpoint.
        /// </summary>
        [Required]
        public string HostName { get; init; } = "localhost";

        /// <summary>
        /// AMQP (Advance Message Queuing Protocol) protocol port. Default RabbitMQ port is 5672.
        /// </summary>
        public int Port { get; init; } = 5672;

        /// <summary>
        /// RabbitMQ virtual host. "/" is the default vhost.
        /// WHY VHOSTS MATTER: Multi-tenant isolation on a shared broker.
        /// Different teams/environments can use different vhosts on one cluster.
        /// </summary>
        public string VirtualHost { get; init; } = "/";

        [Required]
        public string UserName { get; init; } = "guest";

        [Required]
        public string Password { get; init; } = "guest";

        /// <summary>
        /// Whether to use TLS for the connection.
        /// Always true in production (managed RabbitMQ services require TLS).
        /// False for local Docker development only.
        /// </summary>
        public bool UseTls { get; init; } = false;

        /// <summary>
        /// Heartbeat interval in seconds — keeps connection alive and detects
        /// dead connections faster than relying on TCP timeout alone.
        /// WHY 30s: RabbitMQ default. Balances detection speed vs network overhead.
        /// </summary>
        public ushort HeartbeatSeconds { get; init; } = 30;

        /// <summary>
        /// Automatic connection recovery on network failure.
        /// PITFALL: Without this, a network blip kills the connection permanently
        /// until app restart. Always true in production.
        /// </summary>
        public bool AutomaticRecoveryEnabled { get; init; } = true;

        /// <summary>
        /// Delay before attempting connection recovery, in seconds.
        /// WHY 10s: Avoid hammering a broker that's still restarting.
        /// </summary>
        public int NetworkRecoveryIntervalSeconds { get; init; } = 10;

        /// <summary>
        /// Exchange name for the orders domain.
        /// NOTE: As the system grows, consider one options section per domain
        /// rather than growing this class indefinitely. Acceptable for this track.
        /// </summary>
        public string OrdersExchange { get; init; } = "production-ready.orders";

        /// <summary>
        /// Dead-letter exchange name for the orders domain.
        /// </summary>
        public string OrdersDeadLetterExchange { get; init; } = "production-ready.orders.dlx";

        /// <summary>
        /// Main queue name for order-created events.
        /// </summary>
        public string OrderCreatedQueue { get; init; } = "orders.created";

        /// <summary>
        /// Dead-letter queue name for order-created events.
        /// </summary>
        public string OrderCreatedDeadLetterQueue { get; init; } = "orders.created.dlq";

        /// <summary>
        /// Routing key used for order-created events.
        /// </summary>
        public string OrderCreatedRoutingKey { get; init; } = "orders.created";

        /// <summary>
        /// Whether topology should be actively declared (dev/staging) or
        /// passively verified only (production, where IaC owns topology).
        ///
        /// WHY: In production, Terraform/IaC creates topology ahead of deployment.
        /// App code should only VERIFY it exists, not create/modify it.
        /// See RabbitMqTopologySetup for how this flag is used.
        /// </summary>
        public bool ActivelyDeclareTopology { get; init; } = true;
    }
}
