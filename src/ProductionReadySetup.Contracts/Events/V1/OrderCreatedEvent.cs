using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Contracts.Events.V1
{
    /// <summary>
    /// Event published when a new order is successfully created.
    ///
    /// CONTRACT RULES — treat this like a public API:
    ///   ← NEVER remove a field (breaks existing consumers)
    ///   ← NEVER rename a field (breaks deserialization)
    ///   ← NEVER change a field type (breaks deserialization)
    ///   ← SAFE to add new optional (nullable) fields
    ///   ← Breaking change? → Create V2/OrderCreatedEvent.cs, keep this intact
    ///
    /// SERIALIZATION:
    ///   All properties use init — immutable after construction.
    ///   All properties are value types or strings — no circular references.
    ///   Nullable fields are explicitly marked — consumer knows what to expect.
    ///
    /// NAMING CONVENTION:
    ///   "{Entity}{PastTenseVerb}Event"
    ///   WHY PAST TENSE: Events describe something that ALREADY HAPPENED.
    ///   "OrderCreated" = the order was created (fact, immutable).
    ///   Never use future/imperative: "CreateOrder" is a command, not an event.
    ///
    /// PITFALL: Do not put business logic here.
    ///   No methods, no validation, no computed properties.
    ///   Pure data — serialized and deserialized across process boundaries.
    /// </summary>
    public sealed class OrderCreatedEvent
    {
        /// <summary>
        /// Unique identifier of the created order.
        /// Consumer uses this to look up or store the order.
        /// </summary>
        public string OrderId { get; init; } = string.Empty;


        /// <summary>
        /// Identifier of the customer who placed the order.
        /// Consumer may use this to send confirmation email,
        /// update customer order history, etc.
        /// </summary>
        public string CustomerId { get; init; } = string.Empty;


        /// <summary>
        /// Total monetary value of the order.
        /// WHY decimal: Never use float/double for money.
        /// Floating point precision errors = wrong charges.
        /// </summary>
        public decimal TotalAmount { get; init; }


        /// <summary>
        /// ISO 4217 currency code.
        /// Example: "USD", "EUR", "INR"
        /// Consumer needs this for any monetary formatting or conversion.
        /// </summary>
        public string Currency { get; init; } = "USD";


        /// <summary>
        /// Line items in the order.
        /// Nullable — consumer must handle orders with no items gracefully.
        /// PITFALL: Keep items lightweight. Large payloads in RabbitMQ
        /// increase memory pressure and slow down routing.
        /// Consider storing large data in DB and referencing by ID instead.
        /// </summary>
        public IReadOnlyList<OrderLineItem> Items { get; init; } = [];


        /// <summary>
        /// UTC timestamp of when the order was created in the source system.
        /// Distinct from MessageEnvelope.Timestamp (when the message was published).
        /// WHY: Order creation and message publication may not happen at the same instant.
        /// </summary>
        public DateTimeOffset OrderedAt { get; init; } = DateTimeOffset.UtcNow;
    }


    /// <summary>
    /// Represents a single line item within an order.
    ///
    /// NESTED CONTRACT RULE: Same rules apply as the parent event.
    /// Never remove/rename fields. Add only optional fields for additive changes.
    /// </summary>
    public sealed class OrderLineItem
    {
        public string ProductId { get; init; } = string.Empty;
        public string ProductName { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public decimal UnitPrice { get; init; }
    }
}
