using Microsoft.Extensions.Logging;
using ProductionReadySetup.Messaging.Connection;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Publishing
{
    /// <summary>
    /// RabbitMQ implementation of IMessagePublisher using publisher confirms.
    ///
    /// CHANNEL STRATEGY:
    ///   One channel created per publish call via the shared connection.
    ///   WHY NOT a single long-lived shared channel:
    ///     IChannel is NOT thread-safe. A shared channel across concurrent
    ///     HTTP requests (Messaging.Api handles many requests concurrently)
    ///     would require external locking on every publish — serializing
    ///     all publishes through one lock, killing throughput.
    ///   WHY per-call channel is acceptable:
    ///     Creating a channel from an existing connection is CHEAP — no new
    ///     TCP handshake, just a new AMQP channel ID on the existing connection.
    ///     RabbitMQ.Client documentation explicitly supports this pattern for
    ///     moderate-throughput publishing scenarios.
    ///   HIGH-THROUGHPUT EVOLUTION: For very high publish volume, a channel
    ///   pool (rented/returned per publish) avoids repeated channel open/close
    ///   overhead. Noted as a future optimization, not needed for this track.
    ///
    /// PUBLISHER CONFIRMS:
    ///   Channel is put into confirm mode. After publishing, we await
    ///   WaitForConfirmsAsync — this blocks until the broker acknowledges
    ///   the message was received and routed (or rejects it).
    ///   THIS IS WHAT MAKES PUBLISHING RELIABLE — without it, Publish()
    ///   returning successfully means nothing more than "bytes were written
    ///   to a TCP socket," not "RabbitMQ has the message."
    /// </summary>
    public sealed class RabbitMqMessagePublisher(
        RabbitMqConnectionProvider connectionProvider,
        ILogger<RabbitMqMessagePublisher> logger) : IMessagePublisher
    {
        public async Task PublishAsync(
            string exchange, 
            string routingKey, 
            byte[] messageBody,
            CancellationToken ct = default)
        {
            var connection = await connectionProvider.GetConnectionAsync(ct);

            // Publisher confirms require a channel opened with confirms enabled.
            // From 7.x version onwards: Configure the channel to handle publisher confirmations automatically
            var createChannelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);

            using IChannel channel = await connection.CreateChannelAsync(createChannelOptions, ct);

            var properties = new BasicProperties 
            {
                // Persistent = survives broker restart, GIVEN the queue is durable.
                // PITFALL: Persistent message + non-durable queue = still lost on restart.
                // Both must be true together (see topology setup — queues are durable).
                DeliveryMode = DeliveryModes.Persistent,

                // ContentType helps any tooling (Management UI message inspector,
                // future consumers in other languages) understand the body format.
                ContentType = "application/json"
            };

            try
            {
                await channel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: routingKey,
                    mandatory: true, // throws/returns if message is unroutable — see note below
                    basicProperties: properties,
                    body: messageBody,
                    cancellationToken: ct);

                // Await broker confirmation that the message was received and routed.
                // Returns true if confirmed, false if nacked by the broker.
                //var confirmed = await channel.WaitForConfirmsAsync(ct); // ==== ERROR NOT SUPPORTED 7.x+ ========

                // In RabbitMQ.Client 7.x with publisherConfirmationsEnabled = true,
                // BasicPublishAsync awaits broker confirmation internally before returning.
                // No separate WaitForConfirmsAsync needed — if broker nacks or times out,
                // this call throws directly. This is cleaner and more reliable than the
                // explicit WaitForConfirmsAsync pattern from 6.x.

                logger.LogDebug(
                    "Message published and confirmed. Exchange: {Exchange}, RoutingKey: {RoutingKey}",
                    exchange, routingKey);
            }
            catch (Exception ex)
            {
                // Connection-level or channel-level failure during publish.
                // Caller (Messaging.Api) decides how to handle — typically
                // surfaces as a 503/500 to the original HTTP caller.
                logger.LogError(ex,
                    "Failed to publish message. Exchange: {Exchange}, RoutingKey: {RoutingKey}",
                    exchange, routingKey);
                throw;                
            }
        }
    }
}
