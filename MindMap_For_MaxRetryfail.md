```mindmap

 #M2 (failing)
          │
          ▼
PUBLISHED → enters MAIN QUEUE [orders.created]
          │
          │ ← stays here in "unacknowledged" state throughout
          │
          ▼
WORKER RECEIVES (delivery, not removal)
          │
          ├── ATTEMPT 1 → fails → wait 500ms  (message still in queue, unacked)
          ├── ATTEMPT 2 → fails → wait 1000ms (message still in queue, unacked)
          └── ATTEMPT 3 → fails
                    │
                    ▼
          BasicNack(requeue: false)
                    │
                    ▼
          MAIN QUEUE acts (broker-side, automatic):
            reads own config: x-dead-letter-exchange = "orders.dlx"
                    │
                    ▼
          MAIN QUEUE removes message → forwards to [DLX]
                    │
                    ▼
          DLX checks bindings:
            routingKey "orders.created" → DLQ bound
                    │
                    ▼
          MESSAGE lands in [DLQ "orders.created.dlq"]
                    │
                    ├── No consumer (intentional)
                    ├── Sits permanently for ops inspection
                    └── Full envelope intact:
                          MessageId, CorrelationId, Timestamp, Payload
                          → ops cross-references with logs via CorrelationId
                          → investigates root cause
                          → fixes issue
                          → manually republishes or discards
```