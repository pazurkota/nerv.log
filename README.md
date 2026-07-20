# nerv.log

A high-performance, distributed log ingestion and analytics pipeline built with **ASP.NET Core**, **gRPC**, and **PostgreSQL**. This project focuses on solving microservice logging bottlenecks by utilizing non-blocking memory architectures and asynchronous batch processing.

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10)
- [Docker](https://docs.docker.com/get-docker/) (required by Aspire to run containers)

---

## Project structure

```
nerv.log/
├── nerv.log.Aspire/     # .NET Aspire AppHost — orchestrates the server container and PostgreSQL
├── nerv.log.Server/     # ASP.NET Core gRPC server — receives logs and persists them to PostgreSQL
│   └── Protos/          # Protobuf service definition (log_service.proto)
├── nerv.log.Cli/        # CLI tool (nerv) — management and testing commands for the pipeline
└── nerv.log.Tests/      # xUnit unit tests for server services and workers
```

The server exposes a single gRPC streaming endpoint (`LogIngestion.StreamLogs`). Incoming log entries are queued in an in-memory `Channel<LogEntry>` and flushed to PostgreSQL in batches of 1000 by `LogStorageWorker`.

---

## Configuration

The project uses a `.env` file in the repository root to configure the database connection. Create the file before running the project:

```bash
cp .env.example .env   # or create it manually
```

| Variable                       | Description                                          | Default       |
|--------------------------------|------------------------------------------------------|---------------|
| `SCALING_MIN_WORKERS`          | Minimum number of log storage workers                | `2`           |
| `SCALING_MAX_WORKERS`          | Maximum number of log storage workers                | `10`          |
| `SCALING_THRESHOLD_PER_WORKER` | Queue depth per worker that triggers scale-up        | `2000`        |
| `SCALING_BATCH_SIZE`           | Number of log entries flushed to the DB in one batch | `1000`        |

> [!WARNING]
> Never commit `.env` to version control — it is already listed in `.gitignore`.

---

## Running with .NET Aspire

.NET Aspire is the recommended way to run the project locally. It orchestrates the server container, PostgreSQL and RabbitMQ automatically, and provides a Dashboard for observing logs, traces, and resource health.

Start the AppHost from the solution root:

```bash
dotnet run --project nerv.log.Aspire
```

Aspire prints the Dashboard URL on startup (e.g. `https://localhost:17093`). Open it in the browser to monitor running services.

The gRPC server is available at **`http://localhost:8080`**. Database migrations are applied automatically on first startup.

> [!IMPORTANT]
> Aspire automatically generates login credentials for **pgAdmin** and the **RabbitMQ Management UI** on every startup. Find them in the Dashboard under the respective resource's **Environment variables** tab (`PGADMIN_DEFAULT_EMAIL` / `PGADMIN_DEFAULT_PASSWORD` for pgAdmin, `RABBITMQ_DEFAULT_USER` / `RABBITMQ_DEFAULT_PASS` for RabbitMQ).

> [!NOTE] 
> The Aspire Dashboard also exposes an OTLP telemetry endpoint (e.g. `https://localhost:21261`). This is not the gRPC server — do not point the test agent at it.

---

## CLI

`nerv.log.Cli` provides the `nerv` command-line tool. Run it with:

> [!NOTE]
> All avaliable commands are [here](nerv.log.Cli/README.md)

## Running tests

```bash
dotnet test
```

Tests use an in-memory EF Core database and Moq — no running database or server is required.

---
