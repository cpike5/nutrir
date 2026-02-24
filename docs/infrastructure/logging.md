# Logging & Observability

## Overview

Nutrir uses **Serilog** for structured logging, **Seq** for local log search/dashboards, and **Elastic APM** for distributed tracing. Logs are automatically correlated with APM traces via enricher-injected trace IDs.

## Serilog Configuration

Serilog is configured entirely via `appsettings.json` using `ReadFrom.Configuration()`, with programmatic additions for the APM correlation enricher.

### Bootstrap Logger

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();
```

Active during startup before the host is built. Writes to console only. Catches fatal startup errors.

### Host Logger

```csharp
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithElasticApmCorrelationInfo());
```

Replaces the bootstrap logger once the host is configured. Reads sinks, enrichers, and levels from `appsettings.json`. The APM correlation enricher adds `ElasticApmTraceId`, `ElasticApmTransactionId`, and `ElasticApmSpanId` to every log event.

### Fatal Error Handling

```csharp
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
```

The entire `Program.cs` is wrapped in a try/catch to ensure fatal startup errors are logged and the logger is flushed.

### appsettings.json — Serilog Section

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.Seq", "Serilog.Enrichers.Environment", "Serilog.Enrichers.Thread"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
        "System.Net.Http": "Warning",
        "Elastic.Apm": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "Seq", "Args": { "serverUrl": "http://localhost:7102" } }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithEnvironmentName", "WithThreadId"],
    "Properties": {
      "Application": "Nutrir"
    }
  }
}
```

### appsettings.Development.json

Overrides the default minimum level to `Debug` for development:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  }
}
```

### Level Overrides

| Namespace | Level | Reason |
|-----------|-------|--------|
| `Microsoft.AspNetCore` | Warning | Suppresses per-request framework noise |
| `Microsoft.EntityFrameworkCore.Database.Command` | Warning | Suppresses SQL command logging (visible in APM spans instead) |
| `System.Net.Http` | Warning | Suppresses outbound HTTP client noise |
| `Elastic.Apm` | Warning | Suppresses APM agent internal diagnostics |

### Enrichers

Every log event is enriched with:

| Property | Source | Description |
|----------|--------|-------------|
| `Application` | Static property | Always `"Nutrir"` |
| `MachineName` | `Serilog.Enrichers.Environment` | Host machine name |
| `EnvironmentName` | `Serilog.Enrichers.Environment` | ASP.NET environment (Development, Production) |
| `ThreadId` | `Serilog.Enrichers.Thread` | Thread ID |
| `ElasticApmTraceId` | `Elastic.Apm.SerilogEnricher` | APM distributed trace ID |
| `ElasticApmTransactionId` | `Elastic.Apm.SerilogEnricher` | APM transaction ID |
| `ElasticApmSpanId` | `Elastic.Apm.SerilogEnricher` | APM span ID (if within a span) |

### Request Logging

`app.UseSerilogRequestLogging()` is registered early in the middleware pipeline. It produces a single structured log event per HTTP request with:

- `RequestMethod`, `RequestPath`, `StatusCode`, `Elapsed` (ms)
- Replaces the multiple log events that ASP.NET Core would normally emit per request

## Sinks

| Sink | Environment | Endpoint | Purpose |
|------|-------------|----------|---------|
| Console | All | stdout | Development output, container logs |
| Seq | Dev (primary) | `http://localhost:7102` (local) / `http://seq:5341` (Docker) | Structured log search and dashboards |
| Elasticsearch | Prod (future) | External cluster | Production log aggregation |

## Seq

### Access

| What | URL/Port |
|------|----------|
| Seq UI | `http://localhost:7101` |
| Seq ingestion API | `http://localhost:7102` |
| Default admin password | `SeqDev123!` (from `.env.example`) |

In Docker, the app service writes to `http://seq:5341` (internal Docker network).

## Elastic APM

### What It Does

Elastic APM provides distributed tracing with zero-code auto-instrumentation via `Elastic.Apm.NetCoreAll`:

| Signal | What Gets Captured |
|--------|--------------------|
| HTTP transactions | Every inbound HTTP request → APM transaction (method, route, status, duration) |
| EF Core spans | Every database query → child span (SQL statement, duration) |
| Outbound HTTP spans | Every `HttpClient` call → child span with W3C trace propagation |
| Log correlation | Every Serilog log event → enriched with trace/transaction/span IDs |

### Registration

In `Program.cs`:

```csharp
builder.Services.AddAllElasticApm();
```

This registers the APM agent via DI and enables all auto-instrumentation modules (ASP.NET Core, EF Core, HttpClient). No middleware call is needed — `AddAllElasticApm()` handles everything.

### Configuration

In `appsettings.json`:

```json
{
  "ElasticApm": {
    "ServiceName": "Nutrir",
    "Environment": "Development",
    "TransactionSampleRate": 1.0,
    "CaptureBody": "off",
    "SanitizeFieldNames": ["password", "token", "authorization", "secret"]
  }
}
```

| Setting | Value | Description |
|---------|-------|-------------|
| `ServiceName` | `Nutrir` | Identifies the service in Kibana APM |
| `Environment` | `Development` | Groups traces by environment |
| `TransactionSampleRate` | `1.0` | Sample 100% of transactions (reduce in production) |
| `CaptureBody` | `off` | Don't capture request/response bodies (privacy) |
| `SanitizeFieldNames` | `[password, token, ...]` | Redact sensitive fields in captured data |

### Docker Environment Variables

The `app` service in `docker-compose.yml` includes:

```yaml
- ElasticApm__ServerUrl=${ELASTIC_APM_SERVER_URL:-http://host.docker.internal:8200}
- ElasticApm__SecretToken=${ELASTIC_APM_SECRET_TOKEN:-}
- ElasticApm__Environment=${ASPNETCORE_ENVIRONMENT:-Development}
```

These can be overridden via `.env` or environment variables.

### Without an APM Server

If no APM Server is reachable, the agent logs a warning at startup but **does not crash**. The application runs normally. The Serilog enricher still adds trace ID fields to log events (they'll be empty/null, which is expected).

## NuGet Packages

| Package | Purpose |
|---------|---------|
| `Serilog.AspNetCore` | Serilog integration, `UseSerilogRequestLogging()` |
| `Serilog.Sinks.Console` | Console sink |
| `Serilog.Sinks.Seq` | Seq sink |
| `Serilog.Enrichers.Environment` | `WithMachineName`, `WithEnvironmentName` |
| `Serilog.Enrichers.Thread` | `WithThreadId` |
| `Elastic.Apm.NetCoreAll` | APM agent with auto-instrumentation |
| `Elastic.Apm.SerilogEnricher` | Injects trace IDs into Serilog log events |

## Future Considerations

- **Elasticsearch sink**: Add `Serilog.Sinks.Elasticsearch` for production, configured to an external Elastic cluster
- **Custom spans**: Add manual instrumentation for domain operations (e.g., meal plan generation)
- **Transaction sample rate**: Reduce `TransactionSampleRate` in production (e.g., `0.1` for 10%)
- **Health checks**: Add APM health check endpoint
- **Structured logging conventions**: Establish property naming standards (e.g., `UserId`, `ClientId`, `Action`)
