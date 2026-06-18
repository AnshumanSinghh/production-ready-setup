using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Publishing
{
    /// <summary>
    /// Abstraction for publishing messages to RabbitMQ.
    ///
    /// WHY AN INTERFACE:
    ///   Testability — Messaging.Api unit tests can mock this without
    ///   needing a real RabbitMQ connection.
    ///   Future swappability — if the org migrates to MassTransit (Phase 2)
    ///   or a different broker, only the implementation changes.
    ///
    /// DESIGN:
    ///   Single method, generic payload, explicit routing key and exchange.
    ///   Envelope wrapping happens at the CALLER level (Messaging.Api),
    ///   not inside the publisher — publisher is transport-only, it does not
    ///   know about business event types or envelope construction.
    /// </summary>
    public interface IMessagePublisher
    {
        /// <summary>
        /// Publishes a message to the specified exchange with the given routing key.
        ///
        /// GUARANTEES:
        ///   - Message is marked persistent (survives broker restart, given a durable queue)
        ///   - Publisher confirms are awaited — method only returns successfully
        ///     once the broker has acknowledged receipt
        ///   - Throws on confirm timeout or nack — caller must handle failure
        ///     (retry, alert, or fail the originating HTTP request)
        /// </summary>
        /// <param name="exchange">Target exchange name.</param>
        /// <param name="routingKey">Routing key for message routing to bound queues.</param>
        /// <param name="messageBody">Pre-serialized message body (typically envelope bytes).</param>
        /// <param name="ct">Cancellation token.</param>
        Task PublishAsync(
            string exchange,
            string routingKey,
            byte[] messageBody,
            CancellationToken ct = default);
    }
}
