using Microsoft.Extensions.Options;
using ProductionReadySetup.Contracts.Envelopes;
using ProductionReadySetup.Contracts.Events.V1;
using ProductionReadySetup.Messaging.Api.Models;
using ProductionReadySetup.Messaging.Options;
using ProductionReadySetup.Messaging.Publishing;
using ProductionReadySetup.Messaging.Serialization;
using System.Text;

namespace ProductionReadySetup.Messaging.Api.Services
{
    /// <summary>
    /// Handles envelope construction and publishes OrderCreatedEvent to RabbitMQ.
    ///
    /// RESPONSIBILITIES:
    ///   1. Map CreateOrderRequest → OrderCreatedEvent (HTTP contract → message contract)
    ///   2. Wrap in MessageEnvelope via MessageEnvelopeFactory
    ///   3. Serialize envelope to bytes via MessageSerializer
    ///   4. Delegate transport to IMessagePublisher
    ///
    /// WHY THIS CLASS EXISTS:
    ///   Keeps the controller free of mapping, envelope construction,
    ///   and serialization concerns. Each class has one job.
    ///
    /// CORRELATION ID:
    ///   Passed in from the controller which reads it from HttpContext.Items
    ///   (set by CorrelationIdMiddleware in Common). This ensures the same
    ///   correlationId that flows through HTTP logs also flows into the
    ///   message envelope — the Worker will extract it and push it into
    ///   its own Serilog LogContext, completing the full trace chain.
    /// </summary>
    public sealed class OrderPublisher(
        IMessagePublisher messagePublisher,
        IOptions<RabbitMqOptions> options,
        ILogger<OrderPublisher> logger) : IOrderPublisher
    {

        private readonly RabbitMqOptions _options = options.Value;

        // Source name injected into every envelope this service produces.
        // Matches Observability.ServiceName for consistency across logs and messages.
        private const string SourceName = "messaging-api";
        public async Task PublishOrderCreatedAsync(
            CreateOrderRequest request, 
            string correlationId, 
            CancellationToken ct = default)
        {
            // ── Step 1: Map HTTP request → domain event ───────────────────────────
            var orderId = Guid.NewGuid().ToString("D");

            var createdOrderEvent = new OrderCreatedEvent
            {
                OrderId = orderId,
                CustomerId = request.CustomerId,
                Currency = request.Currency,
                TotalAmount = request.Items.Sum(item => item.Quantity * item.UnitPrice),
                OrderedAt = DateTimeOffset.UtcNow,
                Items = request.Items.Select(item => new OrderLineItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice
                }).ToList()
            };


            // ── Step 2: Wrap in envelope ──────────────────────────────────────────
            // MessageEnvelopeFactory generates a fresh MessageId (idempotency key)
            // and stamps Timestamp = UtcNow. CorrelationId is forwarded from
            // the originating HTTP request to maintain the full trace chain.
            var messageEnvelope = MessageEnvelopeFactory.Create(
                payload: createdOrderEvent,
                correlationId: correlationId,
                source: SourceName);

            // ── Step 3: Serialize envelope to bytes ───────────────────────────────
            var body = MessageSerializer.Serialize(messageEnvelope);

            // ── Step 4: Publish via transport abstraction ─────────────────────────
            logger.LogInformation(
                "Publishing OrderCreatedEvent. OrderId: {OrderId} | " +
                "MessageId: {MessageId} | CorrelationId: {CorrelationId}",
                orderId, messageEnvelope.MessageId, correlationId);

            await messagePublisher.PublishAsync(
                exchange: _options.OrdersExchange,
                routingKey: _options.OrderCreatedRoutingKey,
                messageBody: body,
                ct: ct);

            logger.LogInformation(
                "OrderCreatedEvent published successfully. OrderId: {OrderId} | " +
                "MessageId: {MessageId}",
                orderId, messageEnvelope.MessageId);
        }
    }
}
