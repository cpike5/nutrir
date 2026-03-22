# Kibana Dashboards Implementation Plan

## Overview

Create 6 Kibana dashboards for the Nutrir application using the Saved Objects API. All dashboards use inline Lens panels with ad-hoc data views — no pre-created saved objects required beyond the dashboards themselves.

**Cluster**: Elasticsearch 8.12.0 on `localhost:9200`, Kibana on `localhost:5601`
**Auth**: API Key (same key works for both ES and Kibana)

## Data Sources

| Data Stream | Data View Pattern | Docs | Purpose |
|---|---|---|---|
| `traces-apm-default` | `traces-apm*` | 28,991 | Transactions & spans |
| `logs-nutrir-default` | `logs-nutrir-*` | 1,802 | Application logs (ECS/Serilog) |
| `logs-apm.error-default` | `logs-apm.error-*` | 4 | APM-captured exceptions |
| `metricbeat-8.12.0` | `metricbeat-*` | 346,530 | System/Docker/ES metrics |
| `metrics-apm.service_transaction.1m-default` | `metrics-apm.service_transaction*` | 951 | Pre-aggregated transaction metrics |
| `metrics-apm.service_destination.1m-default` | `metrics-apm.service_destination*` | 1,195 | Dependency call metrics |

### Existing Data Views (reuse where possible)

| ID | Pattern |
|---|---|
| `apm_static_data_view_id_default` | `traces-apm*,logs-apm*,metrics-apm*` (APM bundle) |
| `logs-*` | `logs-*` (covers nutrir + apm error logs) |
| `metrics-*` | `metrics-*` |

### Data Views to Create

| ID | Pattern | Name |
|---|---|---|
| `nutrir-logs` | `logs-nutrir-*` | Nutrir Application Logs |
| `nutrir-metricbeat` | `metricbeat-*` | Nutrir Metricbeat |

## Implementation Steps

### Phase 1: Data Views

Create 2 new data views via `POST /api/data_views/data_view`. The APM data view already exists.

### Phase 1b: Tags

Create 5 tags via `POST /api/saved_objects/tag/<id>` and reference them from dashboards:

| Tag ID | Name | Color | Applied To |
|---|---|---|---|
| `nutrir-tag` | Nutrir | `#6DCCB1` (green) | All 6 dashboards |
| `nutrir-tag-apm` | Nutrir: APM | `#6092C0` (blue) | Overview, Endpoint, DB & Deps, Errors, AI |
| `nutrir-tag-logs` | Nutrir: Logs | `#D6BF57` (yellow) | Overview, DB & Deps, Errors, AI |
| `nutrir-tag-infra` | Nutrir: Infra | `#54B399` (teal) | Infrastructure |
| `nutrir-tag-ai` | Nutrir: AI | `#D36086` (pink) | AI Assistant |

Tags are referenced via the dashboard's top-level `references` array:
```json
{ "type": "tag", "id": "nutrir-tag", "name": "tag-nutrir" }
```

### Phase 2: Dashboards

Each dashboard is created via `POST /api/saved_objects/dashboard/<id>` with inline Lens panels. All panels use ad-hoc data views embedded in the panel config to avoid cross-referencing issues.

---

## Dashboard 1: Application Overview

**ID**: `nutrir-app-overview`
**Time range**: `now-24h` to `now`, auto-refresh 30s

| # | Panel | Type | Size (w x h) | Position (x, y) | Data Source | Key Fields |
|---|---|---|---|---|---|---|
| 1 | Request Throughput | `lnsXY` (line) | 24 x 12 | 0, 0 | `traces-apm*` | `date_histogram(@timestamp)`, `count` filtered by `processor.event: transaction` AND `transaction.type: request` |
| 2 | Error Rate % | `lnsXY` (area) | 24 x 12 | 24, 0 | `traces-apm*` | `formula`: `count(kql='event.outcome: failure') / count() * 100` filtered by `processor.event: transaction` |
| 3 | Avg Latency (ms) | `lnsMetric` | 12 x 8 | 0, 12 | `traces-apm*` | `avg(transaction.duration.us)` with formula `/1000` |
| 4 | P95 Latency (ms) | `lnsMetric` | 12 x 8 | 12, 12 | `traces-apm*` | `percentile(transaction.duration.us, 95)` with formula `/1000` |
| 5 | Total Requests | `lnsMetric` | 12 x 8 | 24, 12 | `traces-apm*` | `count` filtered by `processor.event: transaction` |
| 6 | Error Count | `lnsMetric` | 12 x 8 | 36, 12 | `traces-apm*` | `count` filtered by `event.outcome: failure` |
| 7 | HTTP Status Codes | `lnsXY` (bar_stacked) | 24 x 14 | 0, 20 | `traces-apm*` | `date_histogram(@timestamp)`, `count`, split by `terms(http.response.status_code)` |
| 8 | Log Level Distribution | `lnsPie` (donut) | 24 x 14 | 24, 20 | `logs-nutrir-*` | `terms(log.level)`, `count` |
| 9 | Recent Errors | `lnsDatatable` | 48 x 12 | 0, 34 | `logs-nutrir-*` | Columns: `@timestamp`, `error.type`, `message`, `trace.id`; filtered by `log.level: Error` |

### Dashboard-level filter
```json
{ "query": "service.name: Nutrir", "language": "kuery" }
```

---

## Dashboard 2: Endpoint Performance

**ID**: `nutrir-endpoint-performance`
**Time range**: `now-24h` to `now`

| # | Panel | Type | Size | Position | Data Source | Key Fields |
|---|---|---|---|---|---|---|
| 1 | Slowest Endpoints | `lnsDatatable` | 48 x 16 | 0, 0 | `traces-apm*` | `terms(transaction.name, size=20)`, columns: avg/p95 of `transaction.duration.us`, `count`, formula for error rate |
| 2 | Top Endpoints by Throughput | `lnsXY` (bar_horizontal) | 24 x 14 | 0, 16 | `traces-apm*` | `terms(transaction.name, size=10, orderBy=count)`, `count` |
| 3 | Latency Over Time | `lnsXY` (line) | 24 x 14 | 24, 16 | `traces-apm*` | `date_histogram(@timestamp)`, `avg(transaction.duration.us)`, `percentile(transaction.duration.us, 95)` |
| 4 | Login Endpoint Latency | `lnsXY` (line) | 24 x 14 | 0, 30 | `traces-apm*` | `date_histogram(@timestamp)`, `avg(transaction.duration.us)`, filtered by `transaction.name: *Login*` |
| 5 | API Endpoint Latency | `lnsXY` (line) | 24 x 14 | 24, 30 | `traces-apm*` | `date_histogram(@timestamp)`, `avg(transaction.duration.us)`, split by `terms(transaction.name)`, filtered by `transaction.name: *api*` |
| 6 | SignalR Hub Duration | `lnsXY` (bar) | 48 x 12 | 0, 44 | `traces-apm*` | `terms(transaction.name)` filtered by `transaction.name: *hubs*`, `avg(transaction.duration.us)` |

### Dashboard-level filter
```json
{ "query": "processor.event: transaction AND transaction.type: request AND service.name: Nutrir", "language": "kuery" }
```

---

## Dashboard 3: Database & Dependencies

**ID**: `nutrir-db-dependencies`
**Time range**: `now-24h` to `now`

| # | Panel | Type | Size | Position | Data Source | Key Fields |
|---|---|---|---|---|---|---|
| 1 | DB Span Latency Over Time | `lnsXY` (line) | 24 x 14 | 0, 0 | `traces-apm*` | `date_histogram(@timestamp)`, `avg(span.duration.us)`, split by `terms(span.subtype)`, filtered by `span.type: db` |
| 2 | DB Operations Throughput | `lnsXY` (area_stacked) | 24 x 14 | 24, 0 | `traces-apm*` | `date_histogram(@timestamp)`, `count`, split by `terms(span.subtype)`, filtered by `span.type: db` |
| 3 | Dependency Breakdown | `lnsPie` (donut) | 24 x 14 | 0, 14 | `metrics-apm.service_destination*` | `terms(span.destination.service.resource)`, `sum(span.destination.service.response_time.count)` |
| 4 | Anthropic API Latency | `lnsXY` (line) | 24 x 14 | 24, 14 | `traces-apm*` | `date_histogram(@timestamp)`, `avg(span.duration.us)`, `percentile(span.duration.us, 95)`, filtered by `span.destination.service.resource: api.anthropic.com*` |
| 5 | EF Core Activity | `lnsXY` (bar_stacked) | 24 x 14 | 0, 28 | `logs-nutrir-*` | `date_histogram(@timestamp)`, `count`, split by `terms(event.action, size=8)`, filtered by `log.logger: Microsoft.EntityFrameworkCore*` |
| 6 | DB Connection Lifecycle | `lnsXY` (line) | 24 x 14 | 24, 28 | `logs-nutrir-*` | `date_histogram(@timestamp)`, `count`, split by `terms(event.action)`, filtered by `event.action: *Connection*` |

---

## Dashboard 4: Errors & Exceptions

**ID**: `nutrir-errors`
**Time range**: `now-7d` to `now`

| # | Panel | Type | Size | Position | Data Source | Key Fields |
|---|---|---|---|---|---|---|
| 1 | Error Count Over Time | `lnsXY` (bar) | 48 x 12 | 0, 0 | `logs-nutrir-*` | `date_histogram(@timestamp)`, `count`, filtered by `log.level: Error OR log.level: Warning` |
| 2 | Error Type Breakdown | `lnsPie` (pie) | 24 x 14 | 0, 12 | `logs-nutrir-*` | `terms(error.type)`, `count` |
| 3 | Failed Transactions by Endpoint | `lnsXY` (bar_horizontal) | 24 x 14 | 24, 12 | `traces-apm*` | `terms(transaction.name, size=10)`, `count`, filtered by `event.outcome: failure` |
| 4 | HTTP 5xx Requests | `lnsXY` (bar) | 24 x 12 | 0, 26 | `traces-apm*` | `date_histogram(@timestamp)`, `count`, filtered by `http.response.status_code >= 500` |
| 5 | APM Captured Errors | `lnsDatatable` | 24 x 12 | 24, 26 | `logs-apm.error-*` | Columns: `@timestamp`, `error.exception.type`, `error.grouping_name`, `service.name` |
| 6 | Error Detail Log | `lnsDatatable` | 48 x 16 | 0, 38 | `logs-nutrir-*` | Columns: `@timestamp`, `log.level`, `error.type`, `error.message`, `trace.id`, `url.path`; filtered by `log.level: Error` |

---

## Dashboard 5: Infrastructure & Docker

**ID**: `nutrir-infrastructure`
**Time range**: `now-6h` to `now`, auto-refresh 30s

| # | Panel | Type | Size | Position | Data Source | Key Fields |
|---|---|---|---|---|---|---|
| 1 | Host CPU % | `lnsXY` (area) | 24 x 12 | 0, 0 | `metricbeat-*` | `date_histogram(@timestamp)`, `avg(system.cpu.total.pct)`, filtered by `event.module: system AND metricset.name: cpu` |
| 2 | Host Memory Usage | `lnsXY` (area) | 24 x 12 | 24, 0 | `metricbeat-*` | `date_histogram(@timestamp)`, `avg(system.memory.used.pct)`, filtered by `event.module: system AND metricset.name: memory` |
| 3 | Disk I/O | `lnsXY` (line) | 24 x 12 | 0, 12 | `metricbeat-*` | `date_histogram(@timestamp)`, `max(system.diskio.read.bytes)`, `max(system.diskio.write.bytes)`, filtered by `metricset.name: diskio` |
| 4 | Network Traffic | `lnsXY` (line) | 24 x 12 | 24, 12 | `metricbeat-*` | `date_histogram(@timestamp)`, `max(system.network.in.bytes)`, `max(system.network.out.bytes)`, filtered by `metricset.name: network` |
| 5 | Container CPU | `lnsXY` (line) | 24 x 14 | 0, 24 | `metricbeat-*` | `date_histogram(@timestamp)`, `avg(docker.cpu.total.pct)`, split by `terms(container.name)`, filtered by `event.module: docker AND metricset.name: container` |
| 6 | Container Memory | `lnsXY` (line) | 24 x 14 | 24, 24 | `metricbeat-*` | `date_histogram(@timestamp)`, `avg(docker.memory.usage.pct)`, split by `terms(container.name)`, filtered by `event.module: docker AND metricset.name: container` |
| 7 | ES Cluster JVM Heap | `lnsXY` (area) | 24 x 12 | 0, 38 | `metricbeat-*` | `date_histogram(@timestamp)`, `avg(elasticsearch.node.stats.jvm.mem.heap.used.pct)`, filtered by `event.module: elasticsearch AND metricset.name: node_stats` |
| 8 | ES Index Count | `lnsMetric` | 12 x 8 | 24, 38 | `metricbeat-*` | `last_value(elasticsearch.cluster.stats.indices.total)`, filtered by `metricset.name: cluster_stats` |
| 9 | Process Summary | `lnsMetric` | 12 x 8 | 36, 38 | `metricbeat-*` | `last_value(system.process_summary.total)`, filtered by `metricset.name: process_summary` |

---

## Dashboard 6: AI Assistant Activity

**ID**: `nutrir-ai-assistant`
**Time range**: `now-24h` to `now`

| # | Panel | Type | Size | Position | Data Source | Key Fields |
|---|---|---|---|---|---|---|
| 1 | AI Transaction Count | `lnsMetric` | 16 x 8 | 0, 0 | `traces-apm*` | `count`, filtered by `transaction.type: ai` |
| 2 | AI Agent Log Count | `lnsMetric` | 16 x 8 | 16, 0 | `logs-nutrir-*` | `count`, filtered by `log.logger: Nutrir.Infrastructure.Services.AiAgentService` |
| 3 | Anthropic API Calls | `lnsMetric` | 16 x 8 | 32, 0 | `traces-apm*` | `count`, filtered by `span.destination.service.resource: api.anthropic.com*` |
| 4 | AI Agent Logs Over Time | `lnsXY` (bar) | 48 x 14 | 0, 8 | `logs-nutrir-*` | `date_histogram(@timestamp)`, `count`, split by `terms(log.level)`, filtered by `log.logger: Nutrir.Infrastructure.Services.Ai*` |
| 5 | Anthropic API Latency | `lnsXY` (line) | 24 x 14 | 0, 22 | `traces-apm*` | `date_histogram(@timestamp)`, `avg(span.duration.us)`, `percentile(span.duration.us, 95)`, filtered by `span.destination.service.resource: api.anthropic.com*` |
| 6 | AI Conversations | `lnsXY` (bar) | 24 x 14 | 24, 22 | `logs-nutrir-*` | `date_histogram(@timestamp)`, `count`, filtered by `log.logger: Nutrir.Infrastructure.Services.AiConversationStore` |
| 7 | AI Log Detail | `lnsDatatable` | 48 x 14 | 0, 36 | `logs-nutrir-*` | Columns: `@timestamp`, `log.level`, `message`, `trace.id`; filtered by `log.logger: Nutrir.Infrastructure.Services.Ai*` |

---

## Implementation Approach

### Script Structure

A single Bash script (`scripts/kibana/create-dashboards.sh`) that:

1. Accepts ES/Kibana host and API key as arguments (with defaults for local dev)
2. Creates data views (idempotent — uses `overwrite` flag)
3. Creates each dashboard with all inline Lens panels
4. Exports the created dashboards as NDJSON for version control
5. Provides a `--delete` flag to remove all Nutrir dashboards

### ID Convention

All Nutrir objects use the `nutrir-` prefix:
- Data views: `nutrir-logs`, `nutrir-metricbeat`
- Dashboards: `nutrir-app-overview`, `nutrir-endpoint-performance`, `nutrir-db-dependencies`, `nutrir-errors`, `nutrir-infrastructure`, `nutrir-ai-assistant`

### Panel Construction Pattern

Each panel uses ad-hoc data views to be fully self-contained:

```json
{
  "type": "lens",
  "gridData": { "x": 0, "y": 0, "w": 24, "h": 12, "i": "p1" },
  "panelIndex": "p1",
  "embeddableConfig": {
    "attributes": {
      "title": "Panel Title",
      "visualizationType": "lnsXY",
      "state": {
        "datasourceStates": { "formBased": { "layers": { ... } } },
        "visualization": { ... },
        "filters": [ ... ],
        "query": { "query": "...", "language": "kuery" },
        "adHocDataViews": { ... },
        "internalReferences": [ ... ]
      },
      "references": []
    }
  }
}
```

### Execution Order

1. Create data views (2 API calls)
2. Create dashboards (6 API calls, can run in parallel)
3. Verify by listing dashboards with `nutrir-` prefix
4. Export NDJSON for git storage
