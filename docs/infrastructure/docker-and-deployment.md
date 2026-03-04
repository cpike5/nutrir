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

## Production Deployment

### docker-compose.prod.yml

The production compose file runs only the `app` and `db` services. Seq is not included — production logging targets the external Elastic cluster (see [Logging & Observability](logging.md)).

| Service | Image | Internal Port | Purpose |
|---------|-------|---------------|---------|
| `app` | `ghcr.io/cpike5/nutrir:latest` | 8080 | Blazor Server application |
| `db` | `postgres:17` | 5432 | PostgreSQL database |

Both services are configured with `restart: unless-stopped`. The `db` service includes a healthcheck, and `app` is configured with `depends_on: db: condition: service_healthy` so the application container does not start until Postgres is accepting connections.

### Environment Variables

#### Required (deployment will fail if unset)

| Variable | Service | Purpose |
|----------|---------|---------|
| `POSTGRES_USER` | db, app | PostgreSQL username |
| `POSTGRES_PASSWORD` | db, app | PostgreSQL password |
| `POSTGRES_DB` | db, app | PostgreSQL database name |

#### Optional

| Variable | Service | Purpose |
|----------|---------|---------|
| `ELASTIC_APM_SERVER_URL` | app | Elastic APM ingestion endpoint |
| `ELASTIC_APM_API_KEY` | app | Elastic APM authentication key |
| `ANTHROPIC_API_KEY` | app | Anthropic API key for the AI assistant |

### Quick Start

```bash
# 1. Create a .env file with required variables
cat > .env <<EOF
POSTGRES_USER=nutrir
POSTGRES_PASSWORD=<strong-password>
POSTGRES_DB=nutrir
EOF

# 2. Pull the latest image and start services
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d

# 3. Check status
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs app
```

To stop without removing containers:

```bash
docker compose -f docker-compose.prod.yml stop
```

To stop and remove containers (data volume is preserved):

```bash
docker compose -f docker-compose.prod.yml down
```

### Reverse Proxy

The application listens on HTTP port 8080 inside the container. It does not terminate TLS. In production, place a reverse proxy (nginx or Caddy) in front of the app container to handle TLS termination and forward traffic to port 8080.

Example Caddy configuration:

```
yourdomain.com {
    reverse_proxy localhost:8080
}
```

Caddy will automatically provision and renew a Let's Encrypt certificate. For nginx, configure an upstream pointing to `localhost:8080` and enable SSL with your certificates.

## CI/CD Pipeline

### Workflow: `docker-publish.yml`

The workflow at `.github/workflows/docker-publish.yml` builds and pushes the Docker image to GitHub Container Registry (GHCR) on every push of a version tag matching `v*`.

**Trigger:** `push` event on tags matching `v*` (e.g., `v1.0.0`, `v0.2.0`)

**Registry:** `ghcr.io/cpike5/nutrir`

### Tags Generated

For a tag such as `v1.2.3`, the workflow pushes the following image tags:

| Tag | Example | Description |
|-----|---------|-------------|
| Semver patch | `1.2.3` | Exact version |
| Semver minor | `1.2` | Latest patch within this minor |
| Semver major | `1` | Latest release within this major |
| `latest` | `latest` | Latest stable release overall |

### Releasing a New Version

```bash
git tag v0.2.0
git push origin v0.2.0
```

The workflow builds from the tagged commit, produces all four tags listed above, and pushes them to GHCR. The production compose file references `ghcr.io/cpike5/nutrir:latest`, so a `docker compose -f docker-compose.prod.yml pull && docker compose -f docker-compose.prod.yml up -d` on the server will pick up the new image.

## Volumes

| Volume | Service | Mount Point | Purpose |
|--------|---------|-------------|---------|
| `pgdata` | db | `/var/lib/postgresql/data` | Persistent PostgreSQL data |

Seq does not have a persistent volume configured — log data is lost on container recreation. Add a volume mount if persistence is needed.
