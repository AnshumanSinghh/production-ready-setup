using RabbitMQ.Client;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;
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

        //HEADERS exchange:
        //  Routes based on message header values, not routing key
        //  Rarely used in practice

        //DIRECT exchange(OUR CHOICE) :
        //  "Send this letter ONLY to the mailbox labeled 'orders.created'"
        //  Exact routing key match — one message → one specific queue
        //  Use when: point-to-point, one producer → one consumer queue
        public const string ExchangeTypeDirect = "direct";

        //TOPIC exchange:
        //  "Send this letter to anyone subscribed to 'orders.*'"
        //  Wildcard routing key matching
        //  Use when: flexible routing with patterns
        public const string ExchangeTypeTopic = "topic";

        // FANOUT exchange:
        // "Send this letter to EVERYONE"
        // One message → ALL bound queues receive it
        // Use when: broadcasting(notifications, cache invalidation)
        // FANOUT here means: broadcast THIS specific invalidation message
        // to ALL pods" NOT = "invalidate ALL cache keys on ALL pods
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
