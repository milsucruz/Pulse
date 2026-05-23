# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Pulse is a .NET 10 async notification system (Projeto 01 de um roadmap de portfólio). It accepts notification requests via REST, persists them to SQL Server, and publishes messages to RabbitMQ for downstream processing. The Worker service consumes those messages and dispatches them via pluggable sender implementations.

The project will evolve into Projeto 03 (webhooks confiáveis com Outbox Pattern). Design decisions — especially abstractions in `Application` — must consider that evolution.

## Technology Stack

| Technology | Version | Role |
|---|---|---|
| .NET / C# | 10 | Runtime and SDK |
| ASP.NET Core | 10 | REST API (publisher) |
| Worker Service | 10 | Queue consumer (separate process) |
| RabbitMQ | 3.13 | Message broker |
| RabbitMQ.Client | 7.x | .NET AMQP SDK |
| EF Core | 10 | ORM — SQL Server |
| SQL Server | 2022 | Persistence |
| Polly | 8.x | Retry, Circuit Breaker, Timeout |
| Serilog | 4.x | Structured logging |
| OpenTelemetry | 1.x | Tracing and metrics |
| Testcontainers | 3.x | Integration tests with real containers |
| GitHub Actions | — | CI/CD |
| Azure Container Apps | — | Deploy (free tier, Consumption plan) |

## Commands

### Infrastructure (Docker)

> **Windows gotcha**: if RabbitMQ is installed locally as a Windows service, it competes with the Docker container on port 5672 and causes `ACCESS_REFUSED` errors. Disable it once: `Stop-Service RabbitMQ; Set-Service RabbitMQ -StartupType Disabled` (Admin PowerShell required).

```bash
./scripts/dev.sh up              # Start SQL Server + RabbitMQ (waits for healthchecks)
./scripts/dev.sh down            # Stop containers, preserve volumes
./scripts/dev.sh reset           # Stop and delete all data/volumes
./scripts/dev.sh logs            # Follow all logs
./scripts/dev.sh logs rabbitmq   # Follow a specific service log
./scripts/dev.sh sql             # Open sqlcmd session in the SQL Server container
```

### Build & Run

```bash
dotnet build Pulse/Pulse.slnx
dotnet run --project Pulse/Api
dotnet run --project Pulse/Worker
```

### Tests

```bash
dotnet test Pulse/Pulse.slnx                                  # All tests
dotnet test Pulse/UnitTests                                   # Unit tests only
dotnet test Pulse/IntegrationTests                            # Integration tests only
dotnet test Pulse/UnitTests --filter "FullyQualifiedName~Foo" # Single test
```

## Architecture

Six projects under `Pulse/` follow a strict layered dependency order:

```
Domain → Application → Infrastructure
                     → Api
                     → Worker
Shared (referenced by Application, Api, Worker)
```

- **Domain**: `Notification` entity with state-transition methods (`MarkAsProcessed`, `MarkAsFailed`, `MarkAsDispatched`). Each transition validates the current status and throws `InvalidOperationException` if the transition is illegal. No external dependencies.
- **Application**: `NotificationAppService` orchestrates the write flow. Interfaces (`INotificationRepository`, `IMessagePublisher`) are defined here; implementations live in Infrastructure.
- **Shared**: `NotificationMessage` record — the serialized contract that crosses the API/Worker boundary over RabbitMQ. `IsValid()` checks `NotificationId`, `Recipient`, `Subject`, `Body`, and `Priority`.
- **Infrastructure**: Implements all Application interfaces. `RabbitMqPublisher` → `IMessagePublisher`. `NotificationRepository` → `INotificationRepository`. `EmailSender` and `PushSender` implement `INotificationSender` and are stub implementations (log only — no real delivery yet). `PollyPolicies` registers resilience pipelines for both senders.
- **Worker**: Two `BackgroundService` consumers — `EmailNotificationConsumer` (listens on `email.queue`) and `PushNotificationConsumer` (listens on `push.queue`). Each injects `INotificationSender` via keyed DI (`"email"` / `"push"`), uses Polly for retry/circuit-breaker, and follows the ACK/NACK conventions.

### Request Flow

`POST /api/pulse` → `PulseController` → `NotificationAppService.SendAsync`:
1. Creates a `Notification` domain entity (status = `Pending`)
2. Persists via `INotificationRepository` and calls `SaveChangesAsync`
3. Builds routing key `pulse.{type}.{priority}` (e.g. `pulse.email.high`)
4. Publishes a `NotificationMessage` to RabbitMQ via `IMessagePublisher`
   - If publish throws, calls `MarkAsFailed(error)` on the entity, saves again, then re-throws
5. Returns `202 Accepted` with the new notification `Guid`

### Critical Interfaces — Do Not Change Without Strong Reason

These are the extension points for Projeto 03:

```csharp
// Projeto 01: implemented by RabbitMqPublisher (publishes directly to broker)
// Projeto 03: implemented by OutboxPublisher (saves to DB; a job dispatches later)
public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, string routingKey, CancellationToken ct = default)
        where T : class;
}

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

**Never inject the concrete implementation directly into application services.** `NotificationAppService` depends on `IMessagePublisher`, not on `RabbitMqPublisher`.

### RabbitMqPublisher — Thread-Safety

`RabbitMqPublisher` is registered as a **singleton** and must be safe for concurrent use. Implementation details:

- Connection and channel are created **lazily** on the first `PublishAsync` call — no sync-over-async in the constructor.
- A `SemaphoreSlim(1, 1)` serializes all publish operations, including the lazy init, so concurrent requests never share the same channel simultaneously.
- Implements `IAsyncDisposable` (not `IDisposable`); the DI container calls `DisposeAsync` on shutdown.

### RabbitMQ Topology

Defined in `infrastructure/rabbitmq/definitions.json` and loaded at container startup. **Do not recreate topology in application startup code — it already exists.**

| Exchange | Type | Purpose |
|---|---|---|
| `pulse.topic` | topic | Main exchange; routes by type+priority |
| `pulse.dlx` | fanout | Dead-letter exchange |

| Queue | Binding Key | TTL |
|---|---|---|
| `email.queue` | `pulse.email.#` | 5 min |
| `push.queue` | `pulse.push.#` | 5 min |
| `pulse.dlq` | (from dlx) | — |

Routing key pattern: `pulse.{type}.{priority}` — the `#` wildcard means adding new priorities or segments requires no binding changes.

Valid priority values: `"low"`, `"medium"`, `"high"`. These are enforced by `[AllowedValues]` on the request DTO. The default is `"high"`.

Dead-letter triggers: consumer `BasicNack(requeue: false)` or message TTL expiry. Flow: queue → `pulse.dlx` (fanout) → `pulse.dlq`.

SMS (`NotificationTypeEnum.Sms`) has no queue or binding defined yet.

### Database

SQL Server database `PulseDB`, schema `pulse`. `infrastructure/sqlserver/create-database.sql` runs at container init and **only creates the database and the `pulse` schema** — it does not create any tables. EF Core migrations own all table DDL; run `dotnet ef database update --project Pulse/Infrastructure` before starting the app on a fresh database.

Tables managed by EF migrations:
- `pulse.Notifications` — main records; `IsDispatched` flag is a placeholder for the Outbox Pattern
- `pulse.OutboxEntries` — schema placeholder for Projeto 03; do not use yet

Relevant indexes:
- `IX_Notifications_Status_CreatedAt` — worker queries by status
- `IX_Notifications_IsDispatched` (filtered where `= 0`) — outbox dispatch job

## Configuration

`Pulse/Api/appsettings.Development.json` and `Pulse/Worker/appsettings.json` hold dev defaults.

Both hosts register configuration with startup validation:

```csharp
builder.Services.AddOptions<RabbitMqConfiguration>()
    .BindConfiguration("RabbitMq")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

`RabbitMqConfiguration` has `[Required, MinLength(1)]` on `Host`, `Username`, `Password`, `ExchangeName`, `DeadLetterExchange`, and `DeadLetterQueue`. A missing or empty value in any of these fails fast at startup.

Configuration sections:
- `RabbitMq` → `RabbitMqConfiguration`
- `ConnectionStrings:Default` → EF Core connection string

Dev credentials (mirrored in `docker-compose.override.yml`):
- RabbitMQ: `admin` / `admin123` at `localhost:15672`
- SQL Server: `sa` / `Pulse#Dev@2026` at `localhost,1433`

## Code Conventions

### Async / CancellationToken

All I/O methods are `async Task`. `CancellationToken` is the last parameter on every public async method, always named `cancellationToken` (not `ct`).

Exception: the `IMessagePublisher` interface uses `ct` by convention — do not rename it there.

### Logging

Use Serilog structured logging. Never interpolate strings — always use named properties:

```csharp
// Correct
logger.LogInformation("Notification {NotificationId} published to {RoutingKey}", id, key);

// Wrong
logger.LogInformation($"Notification {id} published to {key}");
```

Log levels:
- `Debug` — protocol details (individual publish, ack, nack)
- `Information` — successful operations
- `Warning` — retries, circuit breaker half-open, nack with requeue
- `Error` — permanent failures, circuit breaker open, message sent to DLQ

### ACK/NACK in Consumers

```
Success                 → BasicAck
Transient failure       → BasicNack(requeue: true)   — retry
Permanent failure       → BasicNack(requeue: false)  — goes to DLQ
BrokenCircuitException  → BasicNack(requeue: true)   — circuit open, retry later
```

Consumer handlers receive `CancellationToken.None` (not `stoppingToken`) so that in-flight messages complete cleanly during graceful shutdown instead of being requeued spuriously.

### Domain State Transitions

`Notification` only transitions from `Pending`. Calling `MarkAsProcessed()` or `MarkAsFailed()` on a notification already in `Sent` or `Failed` state throws `InvalidOperationException`. Always check state before transitioning.

### Input Validation

`SendNotificationRequest` enforces:
- `Type`: `NotificationTypeEnum?` with `[Required]` — omitting the field returns 400 (prevents silent default to `None = 0`)
- `Priority`: `[AllowedValues("low", "medium", "high")]` — any other value returns 400
- `Recipient`: `[Required, EmailAddress]`
- `Subject`: `[Required, MaxLength(200)]`
- `Body`: `[Required, MaxLength(5000)]`

### Dates

Always `DateTime.UtcNow` — never `DateTime.Now`. The database stores UTC. Columns are `DATETIME2`, mapped to `DateTime` in EF Core.

## Polly Resilience Policies

Defined in `Infrastructure/Resilience/PollyPolicies.cs`. Registered via `AddNotificationSenderPolicies()` extension method, keyed by sender name (`AddResiliencePipeline<string, bool>`). Never define policies inline in consumers.

| Pipeline | Retry | Backoff | Circuit Breaker | Timeout |
|---|---|---|---|---|
| `email-sender` | 3x | Exponential + jitter from 2s | 50% failures in 30s, break 60s | 10s |
| `push-sender` | 3x | Exponential + jitter from 2s | 50% failures in 30s, break 60s | 10s |

## DI Registration Conventions

- `IMessagePublisher` → `RabbitMqPublisher` — **Singleton** (manages a single AMQP connection, thread-safe via semaphore)
- `INotificationRepository` → `NotificationRepository` — **Scoped** (per HTTP request)
- `INotificationAppService` → `NotificationAppService` — **Scoped**
- `INotificationSender` for email → `EmailSender` — **Keyed Singleton** (`"email"`)
- `INotificationSender` for push → `PushSender` — **Keyed Singleton** (`"push"`)

Never inject `EmailSender` or `PushSender` directly — always use `[FromKeyedServices("email")]` / `[FromKeyedServices("push")]` with `INotificationSender`.

## Architectural Decisions (ADRs)

| ADR | Decision | Status |
|---|---|---|
| 001 | Topic Exchange as central topology | Accepted |
| 002 | Worker Service as a separate process | Accepted |
| 003 | Polly centralized in PollyPolicies | Accepted |

## Evolution to Projeto 03 (Outbox Pattern)

Already prepared in this project:
- `IMessagePublisher` in Application — swap implementation without touching consumers
- `IsDispatched` on `Notification` entity — flag for the dispatch job
- `pulse.OutboxEntries` table in schema — awaiting EF Core migration
- Filtered index `IX_Notifications_IsDispatched` — dispatch job performance

What changes in Projeto 03:

```
Today (Projeto 01):
  NotificationService → IMessagePublisher → RabbitMqPublisher → RabbitMQ

Projeto 03:
  NotificationService → IMessagePublisher → OutboxPublisher → DB (same transaction)
                                                             ↑
                                               DispatchJob reads OutboxEntries and publishes
```

Only the DI registration in `Program.cs` changes: `OutboxPublisher` replaces `RabbitMqPublisher`. No consumer, handler, or application service needs to change.

## CI/CD (planned — not yet implemented)

Pipeline in `.github/workflows/ci.yml`:

| Job | Trigger | What it does |
|---|---|---|
| `build-and-test` | push / PR to main, develop | Build + unit tests + integration tests |
| `build-images` | push to main | Build and push Docker images to GHCR |
| `deploy` | after `build-images` | Deploy to Azure Container Apps |

Required GitHub secrets: `AZURE_CREDENTIALS`, `ACR_NAME`, `RESOURCE_GROUP`.

## Azure Deployment (planned — not yet implemented)

| Resource | Config |
|---|---|
| Container Apps Env | Consumption plan — free up to 180k vCPU-s/month |
| `pulse-api` | min replicas: 0 (scale to zero) |
| `pulse-worker` | min replicas: 1 (always up to consume) |
| RabbitMQ | CloudAMQP — Little Lemur plan (free) |
| SQL Server | Azure SQL serverless (free tier) |

Environment variables in containers (never hardcoded): `RabbitMq__Host`, `RabbitMq__Username`, `RabbitMq__Password`, `ConnectionStrings__Default`.

Provisioning script: `scripts/provision-azure.sh` (to be created).
