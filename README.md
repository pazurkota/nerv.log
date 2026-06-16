# nerv.log

A high-performance, distributed log ingestion and analytics pipeline built with **ASP.NET Core**, **gRPC**, and **PostgreSQL**. This project focuses on solving microservice logging bottlenecks by utilizing non-blocking memory architectures and asynchronous batch processing.

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- [Docker](https://docs.docker.com/get-docker/) with Docker Compose

---

## Project structure

```
nerv.log/
├── nerv.log.Server/     # ASP.NET Core gRPC server — receives logs and persists them to PostgreSQL
│   └── Protos/          # Protobuf service definition (log_service.proto)
├── nerv.log.Agent/      # CLI test agent — generates and streams random log entries to the server
├── nerv.log.Tests/      # xUnit unit tests for server services and workers
└── compose.yaml         # Docker Compose — server + PostgreSQL
```

The server exposes a single gRPC streaming endpoint (`LogIngestion.StreamLogs`). Incoming log entries are queued in an in-memory `Channel<LogEntry>` and flushed to PostgreSQL in batches of 1000 by `LogStorageWorker`.

---

## Running with Docker

Copy the example environment file and fill in your credentials:

```bash
cp .env.example .env   # or create .env manually
```

Required variables in `.env`:

```env
DB_USER=postgres
DB_PASSWORD=your_password
DB_NAME=nerv_log_db
```

Start the server and database:

```bash
docker compose up --build
```

The gRPC server will be available on `http://localhost:8080`. Database migrations are applied automatically on startup.

---

## Running the test agent

The agent connects to the server and streams randomly generated log entries. Run it directly with the .NET CLI:

```bash
dotnet run --project nerv.log.Agent [server_url] [delay_ms]
```

Defaults: `server_url = http://localhost:8080`, `delay_ms = 1000`.

Example — connect to a local server with 500 ms between entries:

```bash
dotnet run --project nerv.log.Agent http://localhost:8080 500
```

Stop the agent with `Ctrl+C`. It will complete the gRPC stream gracefully before exiting.

---

## Running tests

```bash
dotnet test
```

Tests use an in-memory EF Core database and Moq — no running database or server is required.

---