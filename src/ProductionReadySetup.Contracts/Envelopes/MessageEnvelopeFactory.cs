using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Contracts.Envelopes
{
    /// <summary>
    /// Factory for creating MessageEnvelope instances.
    ///
    /// WHY A FACTORY:
    ///   Envelope construction has rules:
    ///     ← MessageId must be a fresh UUID (never caller-supplied)
    ///     ← Timestamp must be UtcNow (never caller-supplied)
    ///     ← MessageType must be stable (derived from payload type, not magic string)
    ///   Centralizing this prevents callers from constructing envelopes incorrectly.
    ///
    /// USAGE:
    ///   var envelope = MessageEnvelopeFactory.Create(
    ///       payload: new OrderCreatedEvent { ... },
    ///       correlationId: correlationId,
    ///       source: "messaging-api");
    /// </summary>
    public static class MessageEnvelopeFactory
    {
        /// <summary>
        /// Creates a fully populated envelope for the given payload.
        ///
        /// MessageId  → always a fresh UUID (idempotency key)
        /// MessageType → derived from T's type name (stable, no magic strings)
        /// Timestamp  → always UtcNow
        /// Version    → caller supplies (defaults to "1.0")
        ///
        /// PITFALL: MessageType uses typeof(T).Name — the simple class name.
        ///   This means "OrderCreatedEvent" not the full namespace.
        ///   Stable across namespace refactoring.
        ///   If you rename the class, MessageType changes — treat as breaking change.
        /// </summary>
        public static MessageEnvelope<T> Create<T>(
            T payload,
            string correlationId,
            string source,
            string version="1.0")
        {
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(source);

            return new MessageEnvelope<T>
            {
                MessageId = Guid.NewGuid().ToString("D"),
                CorrelationId = correlationId,
                MessageType = typeof(T).Name,
                Version = version,
                Timestamp = DateTime.UtcNow,
                Source = source,
                Payload = payload
            };
        }
    }
}
