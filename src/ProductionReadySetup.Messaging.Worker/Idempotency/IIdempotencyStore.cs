using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Worker.Idempotency
{
    /// <summary>
    /// Abstraction for checking and marking message processing state.
    ///
    /// WHY AN INTERFACE:
    ///   Unit tests can use an in-memory fake without Redis.
    ///   Future implementations could use a DB for higher durability.
    ///
    /// CONTRACT:
    ///   TryMarkAsProcessedAsync is ATOMIC — it both checks AND marks
    ///   in a single operation. This prevents a race condition where two
    ///   concurrent consumers check "not processed" simultaneously, both
    ///   proceed to process, and both mark as processed after.
    ///   Redis SETNX ('SET' if 'N'ot e'X'ists) is atomic by design — exactly
    ///   one caller wins.
    /// </summary>
    public interface IIdempotencyStore
    {
        /// <summary>
        /// Atomically checks if messageId was already processed.
        /// If NOT processed → marks as processed → returns true (caller should process).
        /// If ALREADY processed → returns false (caller should skip).
        ///
        /// WHY COMBINED CHECK-AND-MARK:
        ///   Separating into IsProcessed() + MarkProcessed() creates a
        ///   TOCTOU race (Time-Of-Check-Time-Of-Use) — two consumers
        ///   could both pass IsProcessed() before either calls MarkProcessed().
        ///   Atomic SETNX eliminates this race entirely.
        /// </summary>
        Task<bool> TryMarkAsProcessedAsync(string messageId, CancellationToken ct = default);
    }
}
