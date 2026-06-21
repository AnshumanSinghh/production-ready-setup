# ProductionReadySetup

A structured, hands-on .NET 8 solution demonstrating **enterprise-grade, production-ready patterns** directly applicable to real systems. Built track-by-track — each track is a self-contained, fully wired implementation covering a core production concern.

> **Who is this for?**
> Mid-to-Lead .NET developers preparing for technical interviews, upskilling in enterprise patterns, or building a reference codebase they can carry into real projects.

---

## Table of Contents

- [Solution Overview](#solution-overview)
- [Solution Structure](#solution-structure)
- [Prerequisites](#prerequisites)
- [Local Infrastructure Setup](#local-infrastructure-setup)
- [Track 1 — Global Exception Handling](#track-1--global-exception-handling)
- [Track 2 — Structured Logging + Observability](#track-2--structured-logging--observability)
- [Track 3 — Redis Cache](#track-3--redis-cache)
- [Track 4 — Hybrid Caching + Keyed Services](#track-4--hybrid-caching--keyed-services)
- [Track 5 — RabbitMQ Messaging](#track-5--rabbitmq-messaging)
- [Running the Projects](#running-the-projects)
- [Key Design Decisions](#key-design-decisions)
- [Coding Standards](#coding-standards)
- [Future Roadmap](#future-roadmap)

---

## Solution Overview

| Track | Concern | Project(s) |
|---|---|---|
| 1 | Global Exception Handling | `ProductionReadySetup.Api` |
| 2 | Structured Logging + Observability | `ProductionReadySetup.Api` |
| 3 | Redis Cache | `ProductionReadySetup.Caching.Api` |
| 4 | Hybrid Caching + Keyed Services | `ProductionReadySetup.Caching.Api` |
| 5 | RabbitMQ Messaging (Producer + Consumer) | `ProductionReadySetup.Messaging.Api` `ProductionReadySetup.Messaging.Worker` |

---

## Solution Structure

```
production-ready-setup/
├── docker-compose.yml                              ← RabbitMQ + Redis (local dev)
├── src/
│   ├── ProductionReadySetup.Common/                ← Shared .dll (classlib)
│   │   ├── Exceptions/                             ← AppException base hierarchy
│   │   ├── ErrorHandling/                          ← GlobalExceptionHandler, ProblemDetails
│   │   ├── Observability/                          ← CorrelationId middleware + Datadog enricher
│   │   ├── Options/                                ← ObservabilityOptions
│   │   └── Extensions/                             ← AddProductionObservability, AddProductionErrorHandling
│   │
│   ├── ProductionReadySetup.Contracts/             ← Shared message contracts (classlib)
│   │   ├── Envelopes/                              ← MessageEnvelope<T>, MessageEnvelopeFactory
│   │   └── Events/
│   │       ├── MessageTypes.cs                     ← Routing key constants
│   │       └── V1/
│   │           └── OrderCreatedEvent.cs            ← Versioned event contract
│   │
│   ├── ProductionReadySetup.Messaging/             ← Shared RabbitMQ infrastructure (classlib)
│   │   ├── Connection/                             ← RabbitMqConnectionProvider (singleton)
│   │   ├── Topology/                               ← RabbitMqTopologySetup (IHostedService)
│   │   ├── Publishing/                             ← IMessagePublisher, RabbitMqMessagePublisher
│   │   ├── Serialization/                          ← MessageSerializer (System.Text.Json)
│   │   ├── HealthChecks/                           ← RabbitMqHealthCheck
│   │   ├── Options/                                ← RabbitMqOptions
│   │   └── Extensions/                             ← AddProductionRabbitMq
│   │
│   ├── ProductionReadySetup.Api/                   ← Track 1 + Track 2 (Web API)
│   │   └── Controllers/
│   │
│   ├── ProductionReadySetup.Caching.Api/           ← Track 3 + Track 4 (Web API)
│   │   ├── Caching/                                ← IAppCache, RedisAppCache, MemoryAppCache, HybridAppCache
│   │   ├── Options/                                ← RedisOptions, CacheOptions
│   │   └── Extensions/                             ← AddProductionRedisCache, AddProductionHybridCaching
│   │
│   ├── ProductionReadySetup.Messaging.Api/         ← Track 5 Producer (Web API)
│   │   ├── Controllers/                            ← OrdersController
│   │   ├── Models/                                 ← CreateOrderRequest
│   │   ├── Services/                               ← IOrderPublisher, OrderPublisher
│   │   └── Extensions/                             ← AddProductionMessagingPublisher
│   │
│   └── ProductionReadySetup.Messaging.Worker/      ← Track 5 Consumer (Worker Service)
│       ├── Consumers/                              ← OrderCreatedConsumer (BackgroundService)
│       ├── Idempotency/                            ← IIdempotencyStore, RedisIdempotencyStore
│       ├── Options/                                ← ConsumerOptions
│       └── Extensions/                             ← AddProductionMessagingConsumer
```

### Dependency Direction

```
ProductionReadySetup.Common         ← no internal dependencies
ProductionReadySetup.Contracts      ← no internal dependencies
ProductionReadySetup.Messaging      ← Contracts
ProductionReadySetup.Api            ← Common
ProductionReadySetup.Caching.Api    ← Common
ProductionReadySetup.Messaging.Api  ← Common + Contracts + Messaging
ProductionReadySetup.Messaging.Worker ← Common + Contracts + Messaging
```

> **Rule:** No project depends on a sibling API project. Common and Contracts are the only shared foundations. Peers never reference peers.

---

## Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| .NET SDK | 8.0+ | Build and run all projects |
| Docker Desktop | Latest | Run RabbitMQ + Redis locally |
| RabbitMQ.Client | 7.x | Async API (`CreateChannelAsync`, `BasicConsumeAsync`) |

Install Docker Desktop (free for personal use):
👉 https://www.docker.com/products/docker-desktop/

---

## Local Infrastructure Setup

All infrastructure runs via Docker Compose. One command starts everything.

**`docker-compose.yml`** (place in repo root):

```yaml
services:

  rabbitmq:
    image: rabbitmq:3.13-management
    container_name: production-ready-rabbitmq
    ports:
      - "5672:5672"       # AMQP protocol — app connects here
      - "15672:15672"     # Management UI → http://localhost:15672
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq

  redis:
    image: redis:7.2-alpine
    container_name: production-ready-redis
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data

volumes:
  rabbitmq_data:
  redis_data:
```

```bash
# Start all infrastructure
docker-compose up -d

# Stop all infrastructure
docker-compose down
```

| Service | URL | Credentials |
|---|---|---|
| RabbitMQ Management UI | http://localhost:15672 | guest / guest |
| Redis | localhost:6379 | none (local only) |

---

## Track 1 — Global Exception Handling

**Project:** `ProductionReadySetup.Api`

### What It Covers

- `IExceptionHandler` — ASP.NET Core's built-in exception handler interface
- RFC 9457 `ProblemDetails` — industry-standard error response format
- Typed error catalog — `ErrorDescriptor`, `ErrorFactory`, `Errors`
- `AppException` hierarchy — typed, domain-specific exceptions
- Safe fallback — unknown exceptions never leak internals to clients

### Key Concepts

**AppException hierarchy:**
```
AppException (abstract base — lives in Common)
    ├── NotFoundException       → 404
    ├── ValidationAppException  → 422
    ├── UnauthorizedException   → 401
    └── ConflictException       → 409
```

**Logging strategy:**
```
AppException  → LogWarning (no stack trace — known, expected)
Unknown       → LogError   (with stack trace — unknown, needs investigation)
```

**Client response (RFC 9457):**
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Resource Not Found",
  "status": 404,
  "detail": "Order '123' was not found.",
  "errorCode": "ORDER_NOT_FOUND",
  "correlationId": "f47ac10b-...",
  "traceId": "4bf92f35..."
}
```

> Stack traces, inner exceptions, connection strings, and internal messages are **never** returned to the client.

### Extension Methods

```csharp
builder.Services.AddProductionErrorHandling();
app.UseProductionErrorHandling();
```

---

## Track 2 — Structured Logging + Observability

**Project:** `ProductionReadySetup.Api` + `ProductionReadySetup.Common`

### What It Covers

- Serilog as the structured logging provider
- JSON stdout logging — Datadog/agent-ready
- Correlation ID middleware — distributed trace chain across services
- Datadog enrichment — `dd.service`, `dd.env`, `dd.version`, `dd.trace_id`
- Request logging — one structured log line per request
- Safe logging — no Authorization headers, tokens, passwords, or request bodies logged

### Key Concepts

**Why Serilog over default ILogger:**
```
Default ILogger → flat, unstructured text → unsearchable in Datadog
Serilog         → structured JSON fields  → queryable, filterable, alertable
```

**Correlation ID flow:**
```
HTTP Request (no header)  → CorrelationIdMiddleware generates UUID
HTTP Request (with header) → CorrelationIdMiddleware reuses upstream ID
      ↓
Pushed into Serilog LogContext (all logs in this request carry it)
      ↓
Echoed in X-Correlation-Id response header
      ↓
Forwarded into RabbitMQ message envelope (Track 5)
      ↓
Worker reads it → pushes into its own LogContext
      ↓
One Datadog query shows full journey end-to-end
```

**Datadog agent vs direct sink:**
```
✅ RECOMMENDED: stdout JSON → Datadog Agent → Datadog
   No API key in app. Agent handles buffering and retry.

⚠️  AVOID in prod: Serilog.Sinks.Datadog.Logs (direct HTTP)
   API key in config. Network failure = lost logs.
```

**Log level rules:**
```
Development → WriteToConsoleJson: false → human-readable console
Production  → WriteToConsoleJson: true  → JSON stdout (agent collects)
```

### Configuration (`appsettings.json`)

```json
{
  "Observability": {
    "ServiceName": "production-ready-api",
    "Environment": "development",
    "Version": "1.0.0",
    "MinimumLevel": "Information",
    "MicrosoftLevel": "Warning",
    "WriteToConsoleJson": false,
    "WriteToFile": false,
    "EnableRequestLogging": true
  }
}
```

### Extension Methods

```csharp
builder.AddProductionObservability();   // Phase 1 — DI + Serilog config
app.UseProductionObservability();       // Phase 2 — middleware pipeline
```

---

## Track 3 — Redis Cache

**Project:** `ProductionReadySetup.Caching.Api`

### What It Covers

- `StackExchange.Redis` — production Redis client
- `IAppCache` — clean cache abstraction (testable, swappable)
- `RedisAppCache` — Redis implementation with safe JSON serialization
- `CacheKeyBuilder` — structured, prefixed, environment-aware key construction
- TTL + TTL jitter — prevents cache stampede on mass expiry
- Redis health check — Kubernetes readiness probe integration
- Cache-aside pattern — `GetOrSetAsync<T>` factory delegate

### Key Concepts

**Cache-aside pattern:**
```
Request arrives
    ↓
Check cache → HIT  → return cached value (fast path)
    ↓ MISS
Load from source of truth (DB / API)
    ↓
Store in cache with TTL + jitter
    ↓
Return data
```

**Key format:**
```
"{appPrefix}:{environment}:{resource}:{identifier}"
Example: "caching-api:production:orders:123"
```

**TTL jitter:**
```
WITHOUT jitter: 1000 entries expire at 10:00:00 → 1000 simultaneous DB queries → DB melts
WITH jitter:    TTL = base + Random(0, MaxJitter) → expiry spread across a window → load smoothed
```

**What we never do:**
```
❌ KEYS * command     → blocks Redis (single-threaded) — use SCAN with MATCH instead
❌ Store sensitive data (PII, tokens) without explicit approval and encryption
❌ Fire-and-forget SetAsync without error handling
```

### Configuration (`appsettings.json`)

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "default",
    "ConnectTimeoutMs": 5000,
    "SyncTimeoutMs": 3000,
    "ConnectRetry": 3,
    "UseSsl": false
  },
  "Cache": {
    "AppPrefix": "caching-api",
    "Environment": "development",
    "DefaultTtl": "00:05:00",
    "MaxJitter": "00:00:30",
    "ThrowOnCacheFailure": false
  }
}
```

### Extension Methods

```csharp
builder.Services.AddProductionRedisCache();
```

---

## Track 4 — Hybrid Caching + Keyed Services

**Project:** `ProductionReadySetup.Caching.Api`

### What It Covers

- L1 / L2 / Source of Truth mental model
- `MemoryAppCache` — in-process L1 cache (`IMemoryCache`)
- `HybridAppCache` — L1 + L2 with stampede protection
- Per-key `SemaphoreSlim` locking — prevents thundering herd within a pod
- Double-checked locking — avoids redundant factory calls under concurrency
- .NET 8 Keyed Services — resolve the right cache provider by scenario

### Key Concepts

**L1 / L2 / Source of Truth:**
```
L1 (Memory)       → nanoseconds, pod-local, lost on restart
L2 (Redis)        → milliseconds, shared across all pods, survives restart
Source of Truth   → DB / API, slowest, always authoritative

Request flow:
  L1 HIT  → return immediately (zero network)
  L1 MISS → check L2
  L2 HIT  → backfill L1 → return
  L2 MISS → call factory → store L2 → store L1 → return
```

**TTL rules:**
```
L1 TTL (MemoryTtl)  < L2 TTL (DefaultTtl)   ← always
WHY: L1 expires first → fallback hits L2 (not DB)
     If L1 TTL >= L2 TTL → both expire simultaneously → stampede hits DB
```

**Stampede protection:**
```
SemaphoreSlim per cache key (in-process):
  Only ONE factory call per key per pod at a time.
  Other threads wait → double-check inside lock → L1/L2 likely populated → skip factory.

LIMITATION: Pod-local only. Two pods may still call factory simultaneously.
True distributed protection requires Redis SETNX locking (future roadmap).
```

**Keyed Services:**
```csharp
// Register
services.AddKeyedSingleton<IAppCache, MemoryAppCache>(CacheProviderKeys.Memory);
services.AddKeyedSingleton<IAppCache, RedisAppCache>(CacheProviderKeys.Redis);
services.AddKeyedSingleton<IAppCache, HybridAppCache>(CacheProviderKeys.Hybrid);

// Resolve
public class OrderService(
    [FromKeyedServices(CacheProviderKeys.Hybrid)] IAppCache cache)
{ }
```

**When to use which:**
```
Memory → rate limiting counters, pod-local lookups, ultra-fast non-shared data
Redis  → shared state, inventory counts, feature flags, cross-pod consistency
Hybrid → default choice for read-heavy, expensive-to-fetch, shared reference data
```

### Extension Methods

```csharp
builder.Services.AddProductionRedisCache();    // Track 3 — must be first
builder.Services.AddProductionHybridCaching(); // Track 4 — layered on top
```

---

## Track 5 — RabbitMQ Messaging

**Projects:** `ProductionReadySetup.Contracts` + `ProductionReadySetup.Messaging` + `ProductionReadySetup.Messaging.Api` + `ProductionReadySetup.Messaging.Worker`

### What It Covers

- Raw `RabbitMQ.Client` 7.x (no abstraction framework — learn the fundamentals)
- Message envelope pattern — idempotency, tracing, versioning in every message
- Durable exchanges, durable queues, persistent messages
- Publisher confirms — guaranteed delivery acknowledgement
- Manual ACK/NACK — process first, ACK after, never auto-ack
- Prefetch count — controls consumer concurrency and message hoarding
- Exponential backoff retry — transient failure handling
- Dead-letter exchange (DLX) + Dead-letter queue (DLQ) — poison message handling
- Redis idempotency store (SETNX) — at-least-once delivery safety
- Graceful shutdown drain pattern — zero message loss during rolling deploys
- Correlation ID forwarding — full distributed trace from HTTP → queue → worker

---

### Track 5-A: `ProductionReadySetup.Contracts`

**Type:** Class Library (`.dll`)

Pure message contract definitions. No infrastructure. No dependencies.

```
Envelopes/
  MessageEnvelope<T>          ← universal wrapper for all messages
  MessageEnvelopeFactory      ← safe envelope construction
Events/
  MessageTypes.cs             ← routing key constants (no magic strings)
  V1/
    OrderCreatedEvent.cs      ← versioned business event payload
```

**Envelope structure:**
```json
{
  "messageId":     "uuid-v4",          // idempotency key
  "correlationId": "uuid-v4",          // distributed trace chain
  "messageType":   "OrderCreatedEvent",// routing + consumer resolution
  "version":       "1.0",              // contract version
  "timestamp":     "2024-01-01T...",   // UTC publish time
  "source":        "messaging-api",    // which service published
  "payload":       { }                 // actual business data
}
```

**Contract versioning rules:**
```
SAFE (no version bump):   add new optional/nullable field
BREAKING (create V2):     remove field, rename field, change type, make optional → required

V1 consumers keep running while V2 consumers are deployed.
Both coexist during rolling migration. No big-bang cutover.
```

---

### Track 5-B: `ProductionReadySetup.Messaging`

**Type:** Class Library (`.dll`)

Shared RabbitMQ infrastructure consumed by both `Messaging.Api` and `Messaging.Worker`.

**Topology diagram:**
```
Publisher → [production-ready.orders exchange] ──"orders.created"──► [orders.created queue]
                                                                              │
                                                                    nack (no requeue)
                                                                              │
                                                                              ▼
                                                             [production-ready.orders.dlx]
                                                                              │
                                                                    ──"orders.created"──►
                                                                              │
                                                                              ▼
                                                                   [orders.created.dlq]
                                                                   (no consumer — ops inspects)
```

**Key infrastructure decisions:**

| Concern | Decision | Why |
|---|---|---|
| IConnection | Singleton | TCP connection is expensive — reuse for app lifetime |
| IChannel | Per-operation | Channels are NOT thread-safe — never share across threads |
| Publisher confirms | Always enabled | Without confirms, publish "success" means nothing |
| Topology ownership (dev) | App declares actively | Fast feedback, self-contained local dev |
| Topology ownership (prod) | Passive verify only | IaC (Terraform) owns production topology |
| Exchange type | Direct | Point-to-point, exact routing key match — simplest and most predictable |

**`ActivelyDeclareTopology` flag:**
```
true  (dev/staging) → ExchangeDeclareAsync / QueueDeclareAsync → creates if missing
false (production)  → ExchangeDeclarePassiveAsync / QueueDeclarePassiveAsync
                      → verifies exists, throws if missing (IaC created it before deploy)
```

### Extension Methods

```csharp
builder.Services.AddProductionRabbitMq();
```

---

### Track 5-C: `ProductionReadySetup.Messaging.Api`

**Type:** ASP.NET Core Web API

Producer side. Accepts HTTP requests, publishes messages to RabbitMQ.

**Request → Message flow:**
```
POST /api/orders
    ↓
CorrelationIdMiddleware → correlationId: "CORR-001"
    ↓
OrdersController → reads correlationId from HttpContext.Items
    ↓
OrderPublisher:
  1. Map CreateOrderRequest → OrderCreatedEvent
  2. Wrap in MessageEnvelope (fresh MessageId, correlationId forwarded)
  3. Serialize to JSON bytes
  4. Publish to "production-ready.orders" exchange, routingKey "orders.created"
    ↓
RabbitMqMessagePublisher:
  Publishes with DeliveryMode = Persistent
  Awaits publisher confirm from broker
    ↓
202 Accepted returned to client
{ "message": "Order accepted for processing.", "correlationId": "CORR-001" }
```

**Why 202 and not 200:**
```
200 OK       = "I processed your request completely"
202 Accepted = "I received and queued it — processing is async"
Publishing to a queue hands off work. The order is not processed yet. 202 is correct.
```

### Extension Methods

```csharp
builder.Services.AddProductionRabbitMq();
builder.Services.AddProductionMessagingPublisher();
```

---

### Track 5-D: `ProductionReadySetup.Messaging.Worker`

**Type:** .NET Worker Service (background process, no HTTP endpoints)

Consumer side. Continuously consumes from RabbitMQ with full production patterns.

**Message processing flow:**
```
Message delivered from "orders.created" queue
    ↓
Deserialize → MessageEnvelope<OrderCreatedEvent>
    ↓ (fail → NACK requeue:false → DLQ)
Push correlationId into Serilog LogContext
    ↓
Version check (envelope.Version == "1.0"?)
    ↓ (unknown version → NACK requeue:false → DLQ)
Idempotency check (Redis SETNX messageId)
    ↓ (duplicate → ACK and skip)
ProcessWithRetryAsync (exponential backoff)
    ├── Success → BasicAck ✅
    └── Failure after MaxRetryAttempts → BasicNack(requeue:false) → DLX → DLQ
```

**Idempotency store (Redis SETNX):**
```
Key:   "idempotency:messaging-worker:{messageId}"
TTL:   24 hours (auto-cleanup, no maintenance job)
Op:    SETNX = "SET if Not eXists" — atomic check-and-mark
Result:
  true  → first time → process
  false → already processed → ACK and skip
```

**Why Redis for idempotency (not in-memory):**
```
In-memory → lost on pod restart → duplicates guaranteed after every deploy ❌
Database  → durable but slower, adds DB dependency to messaging layer
Redis     → sub-millisecond, shared across all pods, built-in TTL, atomic SETNX ✅
```

**Retry strategy:**
```
Attempt 1 → fail → wait 500ms   (BaseRetryDelayMs × 2⁰)
Attempt 2 → fail → wait 1000ms  (BaseRetryDelayMs × 2¹)
Attempt 3 → fail → NACK(requeue:false) → DLX → DLQ
```

**Graceful shutdown (drain pattern):**
```
SIGTERM received
    ↓
BasicCancelAsync → stop broker delivering new messages
    ↓
Wait up to 45s for _inFlightCount to reach 0
    ↓
Channel closed cleanly → process exits

Result: Zero message loss during Kubernetes rolling deploys.
```

**Prefetch count:**
```
BasicQos(prefetchCount: 10)
→ broker sends at most 10 unacknowledged messages at once
→ prevents this pod from hoarding all messages
→ other pods get fair share of queue load
→ tune up (50-100) in production based on processing time and downstream capacity
```

### Configuration (`appsettings.json`)

```json
{
  "Consumer": {
    "PrefetchCount": 10,
    "MaxRetryAttempts": 3,
    "BaseRetryDelayMs": 500,
    "GracefulShutdownTimeoutSeconds": 45,
    "IdempotencyTtlSeconds": 86400
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

### Extension Methods

```csharp
builder.Services.AddProductionRabbitMq();
builder.Services.AddProductionMessagingConsumer(builder.Configuration);
```

---

## Running the Projects

### Step 1 — Start Infrastructure

```bash
docker-compose up -d
```

Verify:
- RabbitMQ UI → http://localhost:15672 (guest / guest)
- Redis → localhost:6379

### Step 2 — Run Individual Projects

```bash
# Track 1 + 2 — Exception Handling + Logging
cd src/ProductionReadySetup.Api
dotnet run

# Track 3 + 4 — Redis + Hybrid Cache
cd src/ProductionReadySetup.Caching.Api
dotnet run

# Track 5 — Producer API
cd src/ProductionReadySetup.Messaging.Api
dotnet run

# Track 5 — Consumer Worker (run alongside Producer)
cd src/ProductionReadySetup.Messaging.Worker
dotnet run
```

### Step 3 — Verify Track 5 End-to-End

1. Start `Messaging.Worker` — verify RabbitMQ UI shows exchanges + queues created under **Exchanges** and **Queues** tabs
2. Start `Messaging.Api` — open Swagger at `http://localhost:{port}/swagger`
3. `POST /api/orders` with sample body:

```json
{
  "customerId": "cust-123",
  "currency": "USD",
  "items": [
    {
      "productId": "prod-1",
      "productName": "Widget",
      "quantity": 2,
      "unitPrice": 19.99
    }
  ]
}
```

4. Response: `202 Accepted` with `X-Correlation-Id` header
5. Worker console logs show message received, idempotency checked, processed, ACKed
6. RabbitMQ UI → `orders.created` queue depth returns to 0

### Health Checks

All API projects expose:

| Endpoint | Purpose | Includes |
|---|---|---|
| `/health` | General | All checks |
| `/health/ready` | Kubernetes readiness | Redis / RabbitMQ (infrastructure tag) |
| `/health/live` | Kubernetes liveness | Process alive only (no infra checks) |

---

## Key Design Decisions

### Why `ProductionReadySetup.Common` (shared classlib)?

Cross-cutting concerns (logging, exception handling, correlation ID) work identically regardless of domain. Extracting them to a shared `.dll` means every API project gets them free via one project reference — no duplication, no drift.

```
Common  → mechanism (how exceptions are caught, how logs are written)
Each Api → domain (what exceptions exist, what error codes mean)
```

### Why separate `Contracts` project?

Producer and consumer must agree on message shape at **compile time**, not runtime. A shared `.dll` with no infrastructure dependencies means both sides see contract breaks as build errors — not silent runtime deserialization failures.

### Why Options Pattern everywhere?

```
No magic strings in application code.
Every configurable value lives in a typed options class.
ValidateOnStart() ensures bad config crashes at startup — not mid-request.
```

### Why raw RabbitMQ.Client (not MassTransit)?

Understanding the fundamentals before adopting an abstraction. When something breaks in production with MassTransit, you need to know what a channel is, what prefetch does, what publisher confirms mean, and what DLX wiring looks like — this codebase teaches exactly that.

### Why `IAppCache` abstraction (not `IDistributedCache` directly)?

`IDistributedCache` from Microsoft is string/byte-only with no generic methods, no cache-aside pattern, no TTL jitter, no stampede protection. `IAppCache` wraps these concerns cleanly while remaining testable via interface.

---

## Coding Standards

All code in this repo follows these rules consistently:

| Rule | Rationale |
|---|---|
| Extension methods named `AddProductionXxx` / `UseProductionXxx` | Consistent, discoverable registration pattern |
| Options Pattern with `ValidateOnStart()` on every options class | Fail at startup, not mid-request |
| No magic strings in application code — constants classes used | Typos cause silent failures |
| Controllers kept thin — logic in service layer | Single responsibility, testability |
| Interfaces only when providing real testability or multiple implementations | Avoid over-abstraction |
| Never leak stack traces, connection strings, or internal messages to clients | Security baseline |
| Comments styled as "notes to self" — what, why, objective, pitfalls | Learning-friendly, production-honest |
| `async`/`await` throughout — no `.Result` or `.Wait()` | Deadlock prevention |
| `CancellationToken` on all async methods | Production requirement — supports timeout and graceful shutdown |

---

## Future Roadmap

| Item | Description |
|---|---|
| **Generic Topology Declaration** | Descriptor-driven topology setup — pass exchange/queue/routing key config and have topology declared generically without modifying `RabbitMqTopologySetup` |
| **MassTransit (Phase 2)** | Layer MassTransit over raw RabbitMQ.Client implementation — same patterns, less boilerplate, built-in retry/saga support |
| **OpenTelemetry + Datadog APM** | Wire distributed traces (spans) from HTTP → queue → worker using OTEL SDK + Datadog exporter |
| **Distributed Stampede Protection** | Redis SETNX-based distributed lock for true cross-pod stampede prevention in `HybridAppCache` |
| **Outbox Pattern** | Transactional outbox for guaranteed message publishing — publish only if DB write succeeds |
| **Event-Driven Cache Invalidation** | RabbitMQ fanout exchange — Worker consumes cache invalidation events and calls `RemoveAsync` on specific keys across all pods |
| **Health Check UI** | `AspNetCore.HealthChecks.UI` dashboard aggregating all project health endpoints |
| **Docker Compose — Full Solution** | Single `docker-compose.yml` that starts all API projects + Worker + infrastructure together |

---

## License

This project is for educational purposes. Free to use, reference, and adapt for personal and professional learning.

---

> Built track-by-track. Every pattern production-aligned. Every comment a note to self.
