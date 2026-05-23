# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Pulse is a .NET 10 async notification system (Projeto 01 de um roadmap de portfólio). It accepts notification requests via REST, persists them to SQL Server, and publishes messages to RabbitMQ for downstream processing. The Worker service is intended to consume those messages but is currently a stub.

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

- **Domain**: `Notification` entity with state-transition methods (`MarkAsProcessed`, `MarkAsFailed`, `MarkAsDispatched`). No external dependencies.
- **Application**: `NotificationAppService` orchestrates the write flow. Interfaces (`INotificationRepository`, `IMessagePublisher`) are defined here; implementations live in Infrastructure.
- **Shared**: `NotificationMessage` record — the serialized contract that crosses the API/Worker boundary over RabbitMQ.
- **Infrastructure**: Implements all Application interfaces. `RabbitMqPublisher` → `IMessagePublisher`. `NotificationRepository` → `INotificationRepository`. `EmailSender` and `PushSender` are stub implementations (log only — no real delivery yet). `PollyPolicies` registers resilience pipelines for both senders.
- **Worker**: Two `BackgroundService` consumers — `EmailNotificationConsumer` (listens on `email.queue`) and `PushNotificationConsumer` (listens on `push.queue`). Each uses Polly for retry/circuit-breaker and follows the ACK/NACK conventions.

### Request Flow

`POST /api/pulse` → `PulseController` → `NotificationAppService.SendAsync`:
1. Creates a `Notification` domain entity (status = `Pending`)
2. Persists via `INotificationRepository`
3. Builds routing key `pulse.{type}.{priority}` (e.g. `pulse.email.high`)
4. Publishes a `NotificationMessage` to RabbitMQ via `IMessagePublisher`
5. Returns `202 Accepted` with the new notification `Guid`

`Program.cs` has no DI registrations beyond the ASP.NET Core scaffold — wiring Infrastructure implementations is the next step.

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

`Pulse/Api/appsettings.Development.json` holds dev defaults:

- `ConnectionStrings:Default` — SQL Server connection string
- `RabbitMq` — host, port, credentials, exchange/DLX names
- `Serilog` — structured console logging with context enrichment

Use `IOptions<T>` for all typed configuration. Sections map to:
- `RabbitMq` → `RabbitMqConfiguration`
- `ConnectionStrings:Default` → EF Core connection string

Dev credentials (mirrored in `docker-compose.override.yml`):
- RabbitMQ: `admin` / `admin123` at `localhost:15672`
- SQL Server: `sa` / `Pulse#Dev@2026` at `localhost,1433`

## Code Conventions

### Async / CancellationToken

All I/O methods are `async Task`. `CancellationToken` is the last parameter on every public async method, always named `cancellationToken` (not `ct`).

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

### Dates

Always `DateTime.UtcNow` — never `DateTime.Now`. The database stores UTC. Columns are `DATETIME2`, mapped to `DateTime` in EF Core.

## Polly Resilience Policies

Defined in `Infrastructure/Resilience/PollyPolicies.cs`. Registered via `AddNotificationSenderPolicies()` extension method, keyed by sender name (`AddResiliencePipeline<string, bool>`). Never define policies inline in consumers.

| Pipeline | Retry | Backoff | Circuit Breaker | Timeout |
|---|---|---|---|---|
| `email-sender` | 3x | Exponential + jitter from 2s | 50% failures in 30s, break 60s | 10s |
| `push-sender` | 3x | Exponential + jitter from 2s | 50% failures in 30s, break 60s | 10s |

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
