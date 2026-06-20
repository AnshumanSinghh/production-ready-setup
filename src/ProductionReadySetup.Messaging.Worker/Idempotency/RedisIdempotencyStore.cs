using Microsoft.Extensions.Options;
using ProductionReadySetup.Messaging.Worker.Options;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Worker.Idempotency
{
    /// <summary>
    /// Redis-backed idempotency store using SETNX (SET if Not eXists).
    ///
    /// WHY REDIS:
    ///   ← Sub-millisecond atomic check-and-set (SETNX)
    ///   ← Shared across ALL Worker pods — pod-safe idempotency
    ///   ← Built-in TTL — no cleanup job needed
    ///   ← Survives pod restarts (Redis is external)
    ///
    /// KEY FORMAT:
    ///   "idempotency:messaging-worker:{messageId}"
    ///   Example: "idempotency:messaging-worker:f47ac10b-58cc-4372-a567-0e02b2c3d479"
    ///
    /// TTL:
    ///   24 hours (configurable via ConsumerOptions.IdempotencyTtlSeconds).
    ///   Messages older than TTL are safe to reprocess — business state
    ///   has settled by then. Redis auto-expires, no maintenance needed.
    ///
    /// DURABILITY NOTE:
    ///   Redis can lose data in rare crash scenarios (before AOF/RDB flush).
    ///   For financial or compliance-critical idempotency, use a database.
    ///   For messaging idempotency, Redis durability is sufficient.
    /// </summary>
    public sealed class RedisIdempotencyStore(
        IConnectionMultiplexer redis,
        IOptions<ConsumerOptions> consumerOptions,
        ILogger<RedisIdempotencyStore> logger) : IIdempotencyStore
    {
        private readonly ConsumerOptions _consumerOptions = consumerOptions.Value;
        private const string KeyPrefix = "idempotency:messaging-worker";

        public async Task<bool> TryMarkAsProcessedAsync(string messageId, CancellationToken ct)
        {
            var key = $"{KeyPrefix}:{messageId}";
            var ttl = TimeSpan.FromSeconds(_consumerOptions.IdempotencyTtlSeconds);

            try
            {
                // SETNX = SET if Not eXists — atomic operation.
                // Returns true  → key was NOT present → we set it → first time seeing this message
                // Returns false → key already present → already processed → skip
                var wasSet = await redis
                    .GetDatabase()
                    .StringSetAsync(key, 1, ttl, When.NotExists);

                if (!wasSet)
                {
                    logger.LogWarning(
                        "Duplicate message detected — already processed. " +
                        "MessageId: {MessageId}", messageId);
                }
                return wasSet;
            }
            catch (RedisException ex)
            {
                // IMPORTANT DECISION:
                // If Redis is unavailable, we ALLOW processing (return true).
                // WHY: Blocking all message processing because Redis is down
                // is worse than occasionally processing a duplicate.
                // The business logic should be naturally idempotent where possible
                // (e.g. upsert instead of insert) as a second safety layer.
                logger.LogError(ex,
                    "Redis idempotency check failed for MessageId: {MessageId}. " +
                    "Allowing processing to proceed — verify business logic is idempotent.",
                    messageId);

                return true; ;
            }
        }
    }
}
