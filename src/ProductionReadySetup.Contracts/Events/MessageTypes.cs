using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Contracts.Events
{
    /// <summary>
    /// Central registry of all message type name constants.
    ///
    /// WHY:
    ///   MessageEnvelope.MessageType is a string field.
    ///   Without constants, producer writes "OrderCreated" and consumer
    ///   checks for "Ordercreated" → mismatch → message never routed correctly.
    ///   One typo = silent routing failure in production.
    ///
    /// USAGE:
    ///   Publisher:  envelope.MessageType = MessageTypes.OrderCreated
    ///   Consumer:   if (envelope.MessageType == MessageTypes.OrderCreated)
    ///   Both reference the same constant → typo impossible at compile time.
    ///
    /// CONVENTION: Match the payload class name exactly.
    ///   MessageTypes.OrderCreated = "OrderCreated" = typeof(OrderCreatedEvent).Name
    ///   This alignment makes routing code predictable and grep-able.
    /// </summary>
    public static class MessageTypes
    {
        // ── Order domain ──────────────────────────────────────────────────────────
        public const string OrderCreated = "OrderCreatedEvent";

        // ── Future domains added here as new events are introduced ───────────────
        // public const string OrderShipped   = "OrderShippedEvent";
        // public const string OrderCancelled = "OrderCancelledEvent";
        // public const string PaymentFailed  = "PaymentFailedEvent";
    }
}
