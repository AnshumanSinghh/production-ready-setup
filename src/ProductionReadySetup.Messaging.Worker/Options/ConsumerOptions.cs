using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Worker.Options
{
    /// <summary>
    /// Strongly-typed options for consumer behavior.
    ///
    /// WHY SEPARATE FROM RabbitMqOptions:
    ///   RabbitMqOptions = HOW to connect (infrastructure concern, shared).
    ///   ConsumerOptions = HOW to consume (behavior concern, Worker-specific).
    ///   Retry counts, prefetch, timeouts are tunable per consumer type.
    ///   Different consumers in the same app may have different retry policies.
    ///
    /// BOUND TO: appsettings.json → "Consumer" section
    /// </summary>
    public sealed class ConsumerOptions
    {
        public const string SectionName = "Consumer";

        /// <summary>
        /// Number of unacknowledged messages the broker sends to this
        /// consumer at once before waiting for ACKs.
        ///
        /// WHY THIS MATTERS:
        ///   PrefetchCount = 1  → strictly sequential, one at a time
        ///                        safest, lowest throughput
        ///   PrefetchCount = 10 → up to 10 in-flight simultaneously
        ///                        higher throughput, more memory pressure
        ///
        /// PRODUCTION GUIDANCE:
        ///   Start with a low value (10-50) and tune up based on
        ///   consumer processing time and DB/downstream capacity.
        ///   Too high = consumer crashes holding many unacked messages
        ///   → all requeued simultaneously → spike on recovery.
        /// </summary>
        [Range(1, 1000)]
        public ushort PrefetchCount { get; init; } = 10;

        /// <summary>
        /// Maximum number of retry attempts before routing to DLQ.
        ///
        /// WHY 3:
        ///   Retry 1: transient network blip
        ///   Retry 2: slow dependency recovering
        ///   Retry 3: final attempt before giving up
        ///   After 3: NACK without requeue → DLQ → human investigation
        /// </summary>
        [Range(1, 10)]
        public ushort MaxRetryAttempts { get; init; } = 3;

        // <summary>
        /// Base delay for exponential backoff between retries in milliseconds.
        ///
        /// FORMULA: delay = BaseRetryDelayMs * 2^(attemptNumber - 1)
        ///   Attempt 1: 500ms
        ///   Attempt 2: 1000ms
        ///   Attempt 3: 2000ms
        ///
        /// WHY EXPONENTIAL: Gives recovering dependencies progressively
        /// more time to recover rather than hammering them at fixed intervals.
        /// </summary>
        public int BaseRetryDelayMs { get; init; } = 500;

        /// <summary>
        /// How long to wait for in-flight messages to complete during
        /// graceful shutdown before force-closing the consumer.
        ///
        /// WHY 45 SECONDS:
        ///   Must be less than Kubernetes terminationGracePeriodSeconds (60s).
        ///   Leaves 15s buffer for connection cleanup after drain completes.
        ///   Align this with your longest expected message processing time.
        /// </summary>
        public int GracefulShutdownTimeoutSeconds { get; init; } = 45;

        /// <summary>
        /// TTL for idempotency keys in Redis in seconds.
        /// Default 24 hours — messages older than this are safe to reprocess.
        /// </summary>
        public int IdempotencyTtlSeconds { get; init; } = 86400;
    }
}
