using Microsoft.AspNetCore.Mvc;
using ProductionReadySetup.Common;
using ProductionReadySetup.Messaging.Api.Models;
using ProductionReadySetup.Messaging.Api.Services;
using System.Diagnostics;

namespace ProductionReadySetup.Messaging.Api.Controllers
{
    /// <summary>
    /// Exposes HTTP endpoints for order operations.
    ///
    /// THIN CONTROLLER RULE:
    ///   Controller does three things only:
    ///     1. Read correlationId from HttpContext
    ///     2. Delegate to IOrderPublisher
    ///     3. Return 202 Accepted
    ///   No mapping, no envelope construction, no RabbitMQ knowledge here.
    ///
    /// WHY 202 ACCEPTED:
    ///   Publishing to a queue hands off work asynchronously.
    ///   The order has NOT been processed yet — only queued for processing.
    ///   202 = "received and accepted for async processing."
    ///   200 would imply the order was fully processed, which is false.
    /// </summary>
    [ApiController]
    [Route("api/orders")]
    public sealed class OrdersController(
        IOrderPublisher orderPublisher,
        ILogger<OrdersController> logger) : ControllerBase
    {
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> CreateOrder(
            [FromBody] CreateOrderRequest request,
            CancellationToken ct)
        {
            // Read correlationId set by CorrelationIdMiddleware (Common project).
            // Forwarded into the message envelope so the Worker's logs carry
            // the same correlationId as this HTTP request's logs.
            var correlationId = HttpContext.Items
                .TryGetValue(CorrelationIdConstants.HttpContextItemKey, out var cid) 
                ? cid as String ?? Guid.NewGuid().ToString("D") 
                : Guid.NewGuid().ToString("D");

            logger.LogInformation(
                "Order creation requested. CustomerId: {CustomerId} | " +
                "CorrelationId: {CorrelationId}",
                request.CustomerId, correlationId);

            await orderPublisher.PublishOrderCreatedAsync(request, correlationId, ct);

            // 202 Accepted — message is in the queue, processing is async.
            // Location header would point to an order status endpoint if one existed.
            return Accepted(new
            {
                Message = "Order accepted for processing.",
                CorrelationId = correlationId
            });
        }
    }
}