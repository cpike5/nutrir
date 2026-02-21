# Logging & Observability

## Serilog Configuration

Logging uses Serilog with a two-phase setup in `src/Nutrir.Web/Program.cs`:

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
    .ReadFrom.Services(services)
    .WriteTo.Console()
    .WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://localhost:7102"));
```

Replaces the bootstrap logger once the host is configured. Reads additional settings from DI services and writes to both Console and Seq.

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

### Configuration

In `appsettings.json`:

```json
{
  "Seq": {
    "ServerUrl": "http://localhost:7102"
  }
}
```

In Docker, the app service overrides this to `http://seq:5341` (internal Docker network).

### Log Levels

From `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## Future Considerations

- **Structured logging conventions**: Establish property naming standards (e.g., `UserId`, `ClientId`, `Action`)
- **Correlation IDs**: Add middleware to generate and propagate correlation IDs across requests
- **Elasticsearch sink**: Add `Serilog.Sinks.Elasticsearch` for production, configured to an external Elastic cluster (out of scope for Docker setup)
- **Request logging**: Consider `app.UseSerilogRequestLogging()` for HTTP request/response logging with enrichment
