# Docker & Deployment

## Docker Compose Services

The `docker-compose.yml` defines three services:

| Service | Image | Internal Port | Host Port | Purpose |
|---------|-------|---------------|-----------|---------|
| `app` | Built from `src/Nutrir.Web/Dockerfile` | 8080 | **7100** | Blazor Server application |
| `db` | `postgres:17` | 5432 | **7103** | PostgreSQL database |
| `seq` | `datalust/seq:latest` | 80 / 5341 | **7101** (UI) / **7102** (ingestion) | Log aggregation |

### Port Reference

| Port | Service | Protocol |
|------|---------|----------|
| 7100 | App (Blazor Server) | HTTPS/HTTP |
| 7101 | Seq UI | HTTP |
| 7102 | Seq ingestion API | HTTP |
| 7103 | PostgreSQL | TCP |

### Service Dependencies

`app` depends on both `db` and `seq`. Docker Compose starts them first, but note this only waits for container start, not readiness.

## Dockerfile Build Stages

The Dockerfile at `src/Nutrir.Web/Dockerfile` uses a two-stage build:

### Stage 1: `build` (SDK image)
- Base: `mcr.microsoft.com/dotnet/sdk:9.0`
- Copies solution and `.csproj` files for layer-cached restore
- Runs `dotnet restore`
- Copies full source and runs `dotnet publish` (Release config)

### Stage 2: `runtime` (ASP.NET chiseled image)
- Base: `mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled`
- Ubuntu chiseled image — no shell, no package manager, non-root by default (~110MB vs ~220MB for standard Debian)
- Includes ICU for culture-aware formatting (en-CA dates, currency)
- `docker exec` shell access is unavailable — use Seq and Elastic for diagnostics
- Copies published output from build stage
- Exposes port 8080
- Entry point: `dotnet Nutrir.Web.dll`

## Environment Variables

### `.env.example`

```
# PostgreSQL
POSTGRES_USER=nutrir
POSTGRES_PASSWORD=nutrir_dev
POSTGRES_DB=nutrir

# Seq
SEQ_ADMIN_PASSWORD=SeqDev123!
```

Copy to `.env` and customize for your environment. Docker Compose reads `.env` automatically.

### Variables Set in `docker-compose.yml`

| Variable | Default | Service | Purpose |
|----------|---------|---------|---------|
| `ASPNETCORE_ENVIRONMENT` | `Development` | app | ASP.NET environment name |
| `ConnectionStrings__DefaultConnection` | (composed from PG vars) | app | PostgreSQL connection string |
| `Seq__ServerUrl` | `http://seq:5341` | app | Seq ingestion endpoint (internal Docker network) |
| `POSTGRES_USER` | `nutrir` | db | PostgreSQL username |
| `POSTGRES_PASSWORD` | `nutrir_dev` | db | PostgreSQL password |
| `POSTGRES_DB` | `nutrir` | db | PostgreSQL database name |
| `ACCEPT_EULA` | `Y` | seq | Seq license acceptance |
| `SEQ_FIRSTRUN_ADMINPASSWORD` | `SeqDev123!` | seq | Seq admin password |

## Connection Strings

### Docker (inter-container)

```
Host=db;Port=5432;Database=nutrir;Username=nutrir;Password=nutrir_dev
```

Uses the Docker service name `db` as the host.

### Local Development (`dotnet run`)

```
Host=localhost;Port=7103;Database=nutrir;Username=nutrir;Password=nutrir_dev
```

Connects through the mapped host port 7103. Defined in `appsettings.json`.

## Local Development Setup

### Option 1: Docker Compose (everything in containers)

```bash
docker compose up -d          # Start all services
# App available at http://localhost:7100
# Seq UI at http://localhost:7101
docker compose down            # Stop all services
```

### Option 2: Hybrid (database in Docker, app local)

```bash
# Start only database and Seq
docker compose up -d db seq

# Run app locally with hot reload
dotnet watch --project src/Nutrir.Web

# App uses appsettings.json connection string (localhost:7103)
```

### Option 3: Fully local

```bash
# Run app with dotnet (requires local PostgreSQL on port 7103)
dotnet run --project src/Nutrir.Web
```

## Volumes

| Volume | Service | Mount Point | Purpose |
|--------|---------|-------------|---------|
| `pgdata` | db | `/var/lib/postgresql/data` | Persistent PostgreSQL data |

Seq does not have a persistent volume configured — log data is lost on container recreation. Add a volume mount if persistence is needed.
