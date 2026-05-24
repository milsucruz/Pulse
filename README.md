# Pulse

Event-driven notification system built with .NET 10, RabbitMQ, and SQL Server. Project focused on messaging architecture, resilience with Polly, and integration testing with Testcontainers.

---

## Stack

| Technology | Version | Role |
|---|---|---|
| .NET / C# | 10 | Runtime and SDK |
| ASP.NET Core | 10 | REST API (publisher) |
| Worker Service | 10 | Queue consumer (separate process) |
| RabbitMQ | 3.13 | Message broker |
| RabbitMQ.Client | 7.x | AMQP SDK for .NET |
| Entity Framework Core | 10 | ORM — SQL Server |
| SQL Server | 2022 | Persistence |
| Polly | 8.x | Retry, Circuit Breaker, Timeout |
| Serilog | 4.x | Structured logging |
| Testcontainers | 4.x | Integration tests with real containers |
| xUnit + NSubstitute | — | Unit tests |
| Docker Compose | — | Local infrastructure |

---

## Architecture

Two independent processes communicating through RabbitMQ:

```
┌─────────────────┐     POST /api/pulse      ┌──────────────────────┐
│                 │ ──────────────────────►  │                      │
│     Client      │                          │   API  (port 5105)   │
│                 │ ◄──────────────────────  │                      │
└─────────────────┘   202 Accepted + Guid    └──────────┬───────────┘
                                                        │ PublishAsync
                                                        │ pulse.{type}.{priority}
                                                        ▼
                                             ┌──────────────────────┐
                                             │      RabbitMQ        │
                                             │                      │
                                             │  pulse.topic         │
                                             │  ├── email.queue     │
                                             │  ├── push.queue      │
                                             │  └── pulse.dlq       │
                                             └──────────┬───────────┘
                                                        │ consume
                                                        ▼
                                             ┌──────────────────────┐
                                             │   Worker Service     │
                                             │                      │
                                             │   EmailConsumer      │
                                             │   PushConsumer       │
                                             │   + Polly            │
                                             └──────────────────────┘
```

### Layers

```
Domain          → entities, enums, state rules
Application     → interfaces, use cases, commands
Shared          → contracts crossing the API/Worker boundary
Infrastructure  → implementations: EF Core, RabbitMQ, Polly, Senders
Api             → controllers, DTOs, DI wiring
Worker          → BackgroundServices, consumers, DI wiring
```

Strict dependency rule:

```
Domain ← Application ← Infrastructure ← Api
                                       ← Worker
Shared ← Application, Api, Worker
```

### Notification flow

1. `POST /api/pulse` hits `PulseController`
2. `NotificationAppService` creates a `Notification` entity with `Status = Pending`
3. Persists it to SQL Server via `INotificationRepository`
4. Publishes `NotificationMessage` to RabbitMQ with routing key `pulse.{type}.{priority}`
   - If publish fails: calls `MarkAsFailed(error)`, saves, and re-throws
5. Returns `202 Accepted` with the notification `Guid`
6. The Worker consumes from the queue and runs the sender through a Polly pipeline
7. On success: `BasicAck` + updates `Status = Sent` in the database
8. On permanent failure: `BasicNack(requeue: false)` → routed to `pulse.dlq`

---

## RabbitMQ Topology

Defined in `infrastructure/rabbitmq/definitions.json` and loaded automatically on container startup — never recreated by application code.

| Exchange | Type | Purpose |
|---|---|---|
| `pulse.topic` | topic | Main exchange |
| `pulse.dlx` | fanout | Dead-letter exchange |

| Queue | Binding | TTL | Dead-letter |
|---|---|---|---|
| `email.queue` | `pulse.email.#` | 5 min | → `pulse.dlx` |
| `push.queue` | `pulse.push.#` | 5 min | → `pulse.dlx` |
| `pulse.dlq` | (via dlx) | — | — |

Routing key format: `pulse.{type}.{priority}` — e.g. `pulse.email.high`, `pulse.push.low`

---

## Resilience (Polly)

Each sender has an independent named pipeline:

| Pipeline | Retry | Backoff | Circuit Breaker | Timeout |
|---|---|---|---|---|
| `email-sender` | 3x | Exponential + jitter starting at 2s | 50% failures in 30s → break 60s | 10s/attempt |
| `push-sender` | 3x | Exponential + jitter starting at 2s | 50% failures in 30s → break 60s | 10s/attempt |

ACK/NACK convention in consumers:

| Situation | Action |
|---|---|
| Success | `BasicAck` |
| Transient failure | `BasicNack(requeue: true)` |
| Permanent failure | `BasicNack(requeue: false)` → DLQ |
| `BrokenCircuitException` | `BasicNack(requeue: true)` |

---

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) running in the background
- [.NET 10 SDK](https://dotnet.microsoft.com/download) — verify with `dotnet --version`
- Git Bash or WSL (to run `.sh` scripts on Windows)

> **Windows:** if RabbitMQ is installed locally as a Windows service, it will compete with the container on port 5672. Disable it once (PowerShell as Admin):
> ```powershell
> Stop-Service RabbitMQ
> Set-Service RabbitMQ -StartupType Disabled
> ```

---

## How to run

### 1. Clone the repository

```bash
git clone https://github.com/milsucruz/Pulse.git
cd Pulse
```

### 2. Start the infrastructure

```bash
./scripts/dev.sh up
```

The script starts SQL Server and RabbitMQ, waits for healthchecks, and runs the database creation script. When done:

```
✅ Environment ready!

  RabbitMQ Management UI → http://localhost:15672
  Username: admin  |  Password: admin123

  SQL Server → localhost,1433
  Username: sa  |  Password: Pulse#Dev@2026
```

> SQL Server may take up to 60 seconds to become healthy — just wait.

### 3. Apply migrations (first time only)

```bash
dotnet ef database update \
  --project Pulse/Infrastructure \
  --startup-project Pulse/Api
```

Creates tables `pulse.Notifications` and `pulse.OutboxEntries` in the `PulseDB` database.

### 4. Build

```bash
dotnet build Pulse/Pulse.slnx
```

Expected output: `Build succeeded. 0 Warning(s) 0 Error(s)`

### 5. Run the API and Worker

In two separate terminals:

```bash
# Terminal 1 — API (http://localhost:5105)
dotnet run --project Pulse/Api

# Terminal 2 — Worker
dotnet run --project Pulse/Worker
```

### 6. Send a notification

**Via curl:**

```bash
curl -X POST http://localhost:5105/api/pulse \
  -H "Content-Type: application/json" \
  -d '{
    "recipient": "user@example.com",
    "subject": "First notification",
    "body": "Message via Pulse",
    "type": 1,
    "priority": "high"
  }'
```

**Via Swagger UI:** open [http://localhost:5105/swagger](http://localhost:5105/swagger)

**Payload reference:**

| Field | Type | Required | Values |
|---|---|---|---|
| `recipient` | string (email) | yes | valid email address |
| `subject` | string (max 200) | yes | free text |
| `body` | string (max 5000) | yes | free text |
| `type` | integer | yes | `1` = Email · `2` = Push · `3` = Sms |
| `priority` | string | no (default `"high"`) | `"low"` · `"medium"` · `"high"` |

**Expected response — 202 Accepted:**

```json
{
  "notificationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

### 7. Verify processing

**RabbitMQ:** open [http://localhost:15672](http://localhost:15672) → Queues → `email.queue` — watch messages arrive and get consumed.

**SQL Server:**

```bash
./scripts/dev.sh sql
```

```sql
SELECT Id, Recipient, Type, Status, Priority, CreatedAt, ProcessedAt
FROM pulse.Notifications
ORDER BY CreatedAt DESC;
GO
```

`Status` should change from `Pending` to `Sent` after the Worker processes the message.

---

## Tests

```bash
# All tests
dotnet test Pulse/Pulse.slnx

# Unit tests only (no Docker required)
dotnet test Pulse/UnitTests

# Integration tests only (Docker must be running)
dotnet test Pulse/IntegrationTests
```

**Unit tests** cover:
- `NotificationAppService` — routing key construction, persistence, exception propagation, publish-failure compensation
- `Notification` entity — state transitions, idempotency, invariants

**Integration tests** use Testcontainers (real SQL Server and RabbitMQ containers, no mocks):
- `NotificationRepositoryTests` — persistence round-trip, status update
- `RabbitMqPublisherTests` — message delivery to queue, `MessageId` and `Timestamp` in headers

---

## Useful commands

```bash
./scripts/dev.sh status           # container status
./scripts/dev.sh logs             # all container logs in real time
./scripts/dev.sh logs rabbitmq    # RabbitMQ logs only
./scripts/dev.sh logs sqlserver   # SQL Server logs only
./scripts/dev.sh sql              # interactive sqlcmd in the container
./scripts/dev.sh down             # stop containers (data preserved)
./scripts/dev.sh reset            # stop and delete all volumes
```

---

## Project structure

```
Pulse/
├── Api/                          # ASP.NET Core — HTTP publisher
│   ├── Controllers/
│   │   └── PulseController.cs
│   └── DTOs/
│       ├── Requests/SendNotificationRequest.cs
│       └── Responses/SendNotificationResponse.cs
├── Worker/                       # Worker Service — RabbitMQ consumer
│   ├── EmailNotificationConsumer.cs
│   └── PushNotificationConsumer.cs
├── Application/                  # Use cases and interfaces
│   ├── AppServices/NotificationAppService.cs
│   ├── Commands/SendNotificationCommand.cs
│   └── Interfaces/
│       ├── IMessagePublisher.cs  ← extension point for Outbox Pattern (Project 03)
│       └── INotificationRepository.cs
├── Domain/                       # Entities and enums — zero external dependencies
│   ├── Entities/Notification.cs
│   └── Enums/
│       ├── NotificationTypeEnum.cs
│       └── NotificationStatusEnum.cs
├── Shared/                       # Contract shared between API and Worker
│   └── Messages/NotificationMessage.cs
├── Infrastructure/               # Concrete implementations
│   ├── Messaging/
│   │   ├── RabbitMqPublisher.cs
│   │   └── RabbitMqConfiguration.cs
│   ├── Persistence/
│   │   ├── DbContext.cs
│   │   ├── Migrations/
│   │   └── Repositories/NotificationRepository.cs
│   ├── Resilience/PollyPolicies.cs
│   └── Senders/
│       ├── EmailSender.cs
│       └── PushSender.cs
├── UnitTests/
└── IntegrationTests/

infrastructure/                   # Docker configuration
├── rabbitmq/
│   ├── definitions.json          # Pre-created topology (exchanges, queues, bindings)
│   └── rabbitmq.conf
└── sqlserver/
    ├── create-database.sql       # Creates database and schema (tables owned by EF Core)
    └── init-db.sh

scripts/
└── dev.sh                        # Convenience commands for local development
```

---

## Architectural decisions

### ADR-001 — Topic Exchange as the central topology

Routing keys in the format `pulse.{type}.{priority}` allow new types or priorities to be added by creating a new binding only, without changing the publisher. The `#` wildcard absorbs any future routing key segments.

### ADR-002 — API and Worker as separate processes

API and Worker are distinct projects and containers that scale independently. The API never blocks on message processing; Workers scale horizontally without affecting the HTTP endpoint.

### ADR-003 — Polly centralized in `PollyPolicies`

All resilience policies live in a single place, testable and configurable per environment. Consumers never define retry logic inline — they inject the pipeline by name via `ResiliencePipelineProvider<string>`.

## License

MIT
