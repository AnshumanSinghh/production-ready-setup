using Microsoft.Extensions.Options;
using ProductionReadySetup.Contracts.Envelopes;
using ProductionReadySetup.Contracts.Events.V1;
using ProductionReadySetup.Messaging.Connection;
using ProductionReadySetup.Messaging.Options;
using ProductionReadySetup.Messaging.Serialization;
using ProductionReadySetup.Messaging.Worker.Idempotency;
using ProductionReadySetup.Messaging.Worker.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Worker.Consumers
{
    /// <summary>
    /// Background service that consumes OrderCreatedEvents from RabbitMQ.
    ///
    /// LIFECYCLE:
    ///   StartAsync → connect → set prefetch → register consumer → listen (loop)
    ///   StopAsync  → BasicCancel (stop new deliveries) → drain in-flight → close
    ///
    /// ACK STRATEGY:
    ///   ACK   → message processed successfully OR duplicate (idempotency skip)
    ///            → removed from queue permanently
    ///   NACK (requeue: true)  → transient failure, retry attempts remaining
    ///                            → message goes back to front of queue
    ///   NACK (requeue: false) → retries exhausted OR poison message
    ///                            → DLX routes it to DLQ automatically
    ///
    /// PREFETCH:
    ///   BasicQos(prefetchCount) limits unacknowledged messages in flight.
    ///   Prevents this pod from holding more messages than it can handle.
    ///
    /// GRACEFUL SHUTDOWN (drain pattern):
    ///   On SIGTERM (Signal Terminate) → CancellationToken cancelled → StopAsync called
    ///   → BasicCancel (no new messages delivered to this consumer)
    ///   → wait up to GracefulShutdownTimeoutSeconds for in-flight to complete
    ///   → close channel and connection cleanly
    ///   → process exits
    ///   Result: zero message loss during rolling deploys.
    ///
    /// CORRELATION ID:
    ///   Extracted from envelope → pushed into Serilog LogContext.
    ///   Every log line inside ProcessMessageAsync automatically carries
    ///   the same correlationId as the originating HTTP request in Messaging.Api.
    /// </summary>
    public sealed class OrderCreatedConsumer(
        RabbitMqConnectionProvider connectionProvider,
        IIdempotencyStore idempotencyStore,
        IOptions<RabbitMqOptions> rabbitOptions,
        IOptions<ConsumerOptions> consumerOptions,
        ILogger<OrderCreatedConsumer> logger) : BackgroundService
    {
        private readonly RabbitMqOptions _rabbitMqOptions = rabbitOptions.Value;
        private readonly ConsumerOptions _consumerOptions = consumerOptions.Value;

        // Tracks count of messages currently being processed.
        // Used during graceful shutdown to wait for in-flight messages.
        private int _inFlightCount;

        // ConsumerTag returned by RabbitMQ when we register the consumer.
        // Needed to cancel (BasicCancel) the consumer on shutdown.
        private string? _consumerTag;

        private IChannel? _channel;
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation(
                "OrderCreatedConsumer starting. Queue: {Queue}, PrefetchCount: {PrefetchCount}",
                _rabbitMqOptions.OrderCreatedQueue, _consumerOptions.PrefetchCount);

            var connection = await connectionProvider.GetConnectionAsync(stoppingToken);
            _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

            // Prefetch — broker sends at most this many unacked messages at once.
            // global: false = per-consumer limit (not shared across all consumers on channel).
            await _channel.BasicQosAsync(  // Quality of Service (Qos)
                prefetchSize: 0,  // no size limit
                prefetchCount: _consumerOptions.PrefetchCount,
                global: false,
                cancellationToken: stoppingToken);

            // setting the channel property
            var consumer = new AsyncEventingBasicConsumer(_channel);
            
            // Wire up the message received handler.
            // Each delivery triggers this delegate asynchronously.
            consumer.ReceivedAsync += async (_, eventArgs) =>
            await HandleDeliveryAsync(eventArgs, stoppingToken);

            // Register consumer with RabbitMQ — broker starts delivering messages.
            // autoAck: false = MANUAL acknowledgement — we control when to ACK/NACK.
            // PITFALL: autoAck: true ACKs the moment the message is delivered,
            // before processing. Any crash during processing = silent message loss.
            _consumerTag = await _channel.BasicConsumeAsync(
                queue: _rabbitMqOptions.OrderCreatedQueue,
                autoAck: false,
                consumerTag: string.Empty,  // empty string = let RabbitMQ generate a unique tag
                noLocal: false,             // false = receive messages published by this connection too
                exclusive: false,           // false = allow other consumers on same queue
                arguments: null,            // no special consumer arguments needed
                consumer: consumer,
                cancellationToken: stoppingToken);

            logger.LogInformation(
                "OrderCreatedConsumer registered. ConsumerTag: {ConsumerTag}", _consumerTag);

            // Keep the background service alive until shutdown is signalled.
            // The actual message processing happens in HandleDeliveryAsync callbacks.
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a single message delivery from RabbitMQ.
        /// Called for every message the broker delivers to this consumer.
        /// </summary>
        private async Task HandleDeliveryAsync(
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref _inFlightCount);

            MessageEnvelope<OrderCreatedEvent>? envelope = null;

            try
            {
                // ── Step 1: Deserialize ───────────────────────────────────────────
                envelope = MessageSerializer
                    .Deserialize<MessageEnvelope<OrderCreatedEvent>>(eventArgs.Body);

                if (envelope is null)
                {
                    // Poison message — cannot deserialize.
                    // NACK without requeue → DLQ.
                    // Retrying a corrupt message will never succeed.
                    logger.LogError(
                        "Failed to deserialize message. DeliveryTag: {DeliveryTag}. " +
                        "Routing to DLQ.", eventArgs.DeliveryTag);

                    await _channel!.BasicNackAsync(
                        deliveryTag: eventArgs.DeliveryTag,
                        multiple: false,
                        requeue: false); // → DLX → DLQ
                    return;
                }

                // ── Step 2: Push correlationId into Serilog LogContext ────────────
                // Every log line inside this using block automatically carries
                // the correlationId from the originating HTTP request.
                using (LogContext.PushProperty("CorrelationId", envelope.CorrelationId))
                using (LogContext.PushProperty("MessageId", envelope.MessageId))
                {
                    logger.LogInformation(
                        "Message received. MessageId: {MessageId} | " +
                        "MessageType: {MessageType} | Version: {Version}",
                        envelope.MessageId, envelope.MessageType, envelope.Version);

                    // ── Step 3: Version check ─────────────────────────────────────
                    // Unknown version → NACK to DLQ.
                    // Cannot safely process a contract version we don't understand.
                    if (envelope.Version != "1.0")
                    {
                        logger.LogError(
                            "Unsupported message version: {Version}. MessageId: {MessageId}. " +
                            "Routing to DLQ.", envelope.Version, envelope.MessageId);

                        await _channel!.BasicNackAsync(
                            deliveryTag: eventArgs.DeliveryTag,
                            multiple: false,
                            requeue: false);

                        return;
                    }

                    // ── Step 4: Idempotency check ─────────────────────────────────
                    var shouldProcess = await idempotencyStore
                        .TryMarkAsProcessedAsync(envelope.MessageId, stoppingToken);

                    if (!shouldProcess)
                    {
                        // Duplicate — already processed. ACK and skip.
                        // WHY ACK NOT NACK: message is not "bad" — it was already
                        // successfully processed. NACKing would loop it back into
                        // the queue or route to DLQ, neither of which is correct.
                        logger.LogInformation(
                            "Duplicate message skipped. MessageId: {MessageId}", envelope.MessageId);

                        await _channel!.BasicAckAsync(eventArgs.DeliveryTag, false);
                        return;
                    }

                    // ── Step 5: Process with retry ────────────────────────────────
                    await ProcessWithRetryAsync(envelope, eventArgs.DeliveryTag, stoppingToken);
                }

            }
            catch (Exception ex)
            {
                // Unexpected exception outside the normal retry path.
                // NACK without requeue → DLQ.
                logger.LogError(ex,
                    "Unexpected error handling delivery. DeliveryTag: {DeliveryTag}",
                    eventArgs.DeliveryTag);
                await SafeNackAsync(eventArgs.DeliveryTag, requeue: false);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlightCount);
            }
        }

        /// <summary>
        /// Processes the message with exponential backoff retry.
        /// ACKs on success. NACKs to DLQ after max retries exhausted.
        /// </summary>
        private async Task ProcessWithRetryAsync(
            MessageEnvelope<OrderCreatedEvent> envelope,
            ulong deliveryTag,
            CancellationToken ct)
        {
            var attempt = 0;

            while (attempt < _consumerOptions.MaxRetryAttempts)
            {
                attempt++;

                try
                {
                    await ProcessMessageAsync(envelope.Payload, ct);

                    // SUCCESS - Ack the message
                    await _channel!.BasicAckAsync(deliveryTag, multiple: false);

                    logger.LogInformation(
                        "Message processed successfully. MessageId: {MessageId} | " +
                        "Attempt: {Attempt}", envelope.MessageId, attempt);
                    return;
                }
                catch (Exception ex) when (attempt < _consumerOptions.MaxRetryAttempts)
                {
                    // Transient failure — retry with exponential backoff.
                    var delay = TimeSpan.FromMilliseconds(
                        _consumerOptions.BaseRetryDelayMs * Math.Pow(2, attempt - 1));

                    logger.LogWarning(ex,
                        "Message processing failed. Retrying in {DelayMs}ms. " +
                        "MessageId: {MessageId} | Attempt: {Attempt}/{MaxAttempts}",
                        delay.TotalMilliseconds, envelope.MessageId,
                        attempt, _consumerOptions.MaxRetryAttempts);

                    await Task.Delay(delay, ct);
                }
                catch(Exception ex) 
                {
                    // Final attempt failed — NACK without requeue → DLQ.
                    logger.LogError(ex,
                        "Message processing failed after {MaxAttempts} attempts. " +
                        "MessageId: {MessageId} — routing to DLQ.",
                        _consumerOptions.MaxRetryAttempts, envelope.MessageId);
                    await SafeNackAsync(deliveryTag, requeue: false);
                }
            }
        }


        /// <summary>
        /// Simulates actual business processing of an OrderCreatedEvent.
        ///
        /// IN A REAL SYSTEM:
        ///   ← Persist order to database
        ///   ← Trigger inventory reservation
        ///   ← Send order confirmation email
        ///   ← Publish downstream events (OrderConfirmedEvent)
        ///
        /// KEPT SIMPLE HERE:
        ///   Demonstrates the processing boundary clearly.
        ///   Replace with real business logic when integrating into a product.
        /// </summary>
        private async Task ProcessMessageAsync(
            OrderCreatedEvent orderMessage, 
            CancellationToken ct)
        {
            logger.LogInformation(
                "Processing OrderCreatedEvent. OrderId: {OrderId} | " +
                "CustomerId: {CustomerId} | TotalAmount: {TotalAmount} {Currency}",
                orderMessage.OrderId, orderMessage.CustomerId, orderMessage.TotalAmount, orderMessage.Currency);

            // Simulate processing latency (DB write, downstream call, etc.)
            // Like, ReportRunner.Process() --> Preocess the whole report and PUT it in REDIS
            await Task.Delay(100, ct);

            logger.LogInformation(
                "Order processed successfully. OrderId: {OrderId}", orderMessage.OrderId);
        }

        /// <summary>
        /// Graceful shutdown — drain pattern.
        ///
        /// SEQUENCE:
        ///   1. BasicCancel → stop broker from delivering new messages
        ///   2. Wait up to GracefulShutdownTimeoutSeconds for in-flight to finish
        ///   3. Close channel
        ///
        /// WHY THIS MATTERS:
        ///   Without drain, a rolling deploy on 5 pods with 10 messages each
        ///   = 50 simultaneous NACKs + requeues = spike on remaining pods.
        ///   With drain, each pod finishes its in-flight messages cleanly.
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation(
                "OrderCreatedConsumer shutting down — draining in-flight messages.");

            // Step 1: Tell broker to stop sending new messages to this consumer.
            if (_channel is not null && _consumerTag is not null)
            {
                await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken);
                logger.LogInformation("Consumer cancelled — no new messages will be delivered.");
            }

            // Step 2: Wait for in-flight messages to complete.
            var timeout = TimeSpan.FromMilliseconds(_consumerOptions.GracefulShutdownTimeoutSeconds);
            var deadline = DateTime.UtcNow + timeout;
            
            while(_inFlightCount > 0 && DateTime.UtcNow < deadline)
            {
                logger.LogInformation(
                    "Waiting for {InFlightCount} in-flight message(s) to complete...",
                    _inFlightCount);

                await Task.Delay(500, cancellationToken);
            }

            if (_inFlightCount > 0)
            {
                logger.LogWarning(
                    "Shutdown timeout reached with {InFlightCount} message(s) still in flight. " +
                    "These will be requeued by RabbitMQ.", _inFlightCount);
            }
            else
            {
                logger.LogInformation("All in-flight messages drained successfully.");
            }

            // Step 3: Close channel cleanly.
            if (_channel is not null)
            {
                await _channel.CloseAsync();
                _channel.Dispose();
            }

            await base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Safe NACK wrapper — swallows channel exceptions that can occur
        /// if the channel closes during shutdown while we're trying to NACK.
        /// </summary>
        private async Task SafeNackAsync(ulong deliveryTag, bool requeue)
        {
            try
            {
                await _channel!.BasicNackAsync(
                    deliveryTag, 
                    multiple: false,
                    requeue: requeue);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to NACK message DeliveryTag: {DeliveryTag}. " +
                    "Message will be requeued by broker on connection close.", deliveryTag);
            }
        }
    }
}
