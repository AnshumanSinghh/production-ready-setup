using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Topology
{
    /// <summary>
    /// Constants for RabbitMQ topology arguments and exchange types.
    ///
    /// WHY: RabbitMQ.Client uses raw string keys for special arguments
    /// (x-dead-letter-exchange, x-message-ttl, etc.) and exchange type names.
    /// Centralizing these avoids typos that fail silently at the protocol level.
    /// </summary>
    public static class RabbitMqTopologyConstants
    {
        // ── Exchange types ───────────────────────────────────────────────────────
        public const string ExchangeTypeDirect = "direct";
        public const string ExchangeTypeTopic = "topic";
        public const string ExchangeTypeFanout = "fanout";

        // ── Queue argument keys (x-arguments) ───────────────────────────────────

        /// <summary>
        /// Argument key for specifying the dead-letter exchange on a queue.
        /// When a message is nacked/rejected without requeue, or expires,
        /// RabbitMQ routes it to this exchange instead of discarding it.
        /// </summary>
        public const string ArgDeadLetterExchange = "x-dead-letter-exchange";

        /// <summary>
        /// Argument key for the routing key used when dead-lettering a message.
        /// If omitted, RabbitMQ reuses the message's original routing key.
        /// We set it explicitly for clarity and control.
        /// </summary>
        public const string ArgDeadLetterRoutingKey = "x-dead-letter-routing-key";

        /// <summary>
        /// Argument key for queue message TTL in milliseconds.
        /// Not used in Phase 1 — reserved for future delay/retry queue patterns.
        /// </summary>
        public const string ArgMessageTtl = "x-message-ttl";

        /// <summary>
        /// Argument key for queue max length — caps how many messages a queue
        /// can hold before RabbitMQ drops the oldest (or dead-letters them).
        /// WHY USEFUL: Prevents an unbounded DLQ from consuming all disk space
        /// if poison messages flood in during an incident.
        /// </summary>
        public const string ArgMaxLength = "x-max-length";
    }
}
