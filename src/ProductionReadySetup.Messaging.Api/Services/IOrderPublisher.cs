using ProductionReadySetup.Messaging.Api.Models;

namespace ProductionReadySetup.Messaging.Api.Services
{
    /// <summary>
    /// Abstraction for publishing order events to RabbitMQ.
    ///
    /// WHY THIS INTERFACE EXISTS (not just calling IMessagePublisher directly):
    ///   IMessagePublisher is transport-level — it knows about exchanges,
    ///   routing keys, and bytes. It has no knowledge of orders.
    ///   IOrderPublisher is domain-level — it knows about orders,
    ///   envelope construction, correlation ID wiring, and source naming.
    ///
    ///   Controller depends on IOrderPublisher (domain abstraction).
    ///   IOrderPublisher internally uses IMessagePublisher (transport).
    ///   Controller stays completely isolated from RabbitMQ concerns.
    ///
    ///   Also provides a clean boundary for unit testing the controller
    ///   without needing RabbitMQ infrastructure.
    /// </summary>
    public interface IOrderPublisher
    {
        /// <summary>
        /// Publishes an OrderCreatedEvent envelope to RabbitMQ.
        /// Constructs the envelope, sets correlation ID, and delegates
        /// transport to IMessagePublisher.
        /// </summary>
        Task PublishOrderCreatedAsync(
            CreateOrderRequest request,
            string correlationId,
            CancellationToken ct = default);
    }
}
