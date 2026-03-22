#!/usr/bin/env python3
"""
Nutrir Kibana Dashboard Creator
Creates all 6 Nutrir dashboards via Kibana Saved Objects API.

Usage:
    python3 create-dashboards.py              # local dev defaults
    python3 create-dashboards.py --delete     # remove all Nutrir dashboards
    KIBANA_URL=http://host:5601 API_KEY=... python3 create-dashboards.py
"""

import json
import os
import sys
import urllib.request
import urllib.error
from datetime import datetime

KIBANA_URL = os.environ.get("KIBANA_URL", "http://localhost:5601")
API_KEY = os.environ.get(
    "API_KEY",
    "Uzg3TDE1d0J2cEVySFF5bzB0Rm86UThYc1lUSjFUWnlldEFnQ3dYY0xPUQ==",
)

# Saved data view IDs (existing or created in Phase 1)
DV_APM = "apm_static_data_view_id_default"  # traces-apm*,logs-apm*,metrics-apm*
DV_LOGS = "nutrir-logs"                      # logs-nutrir-*
DV_MB = "nutrir-metricbeat"                  # metricbeat-*

DASHBOARD_IDS = [
    "nutrir-app-overview",
    "nutrir-endpoint-performance",
    "nutrir-db-dependencies",
    "nutrir-errors",
    "nutrir-infrastructure",
    "nutrir-ai-assistant",
]

TAG_IDS = [
    "nutrir-tag",
    "nutrir-tag-apm",
    "nutrir-tag-logs",
    "nutrir-tag-infra",
    "nutrir-tag-ai",
]

DATAVIEW_IDS = ["nutrir-logs", "nutrir-metricbeat"]


def log(msg):
    print(f"[{datetime.now().strftime('%H:%M:%S')}] {msg}")


def kibana_request(method, path, body=None):
    url = f"{KIBANA_URL}{path}"
    headers = {
        "kbn-xsrf": "true",
        "Content-Type": "application/json",
        "Authorization": f"ApiKey {API_KEY}",
    }
    data = json.dumps(body).encode() if body else None
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as resp:
            raw = resp.read()
            return json.loads(raw) if raw else {}
    except urllib.error.HTTPError as e:
        err_body = e.read().decode()
        return {"error": err_body, "status": e.code}


# ---------------------------------------------------------------------------
# Column builders — only native operations, NO formulas
# ---------------------------------------------------------------------------

def col_date_histogram(label="Timestamp"):
    return {
        "dataType": "date", "isBucketed": True, "scale": "interval",
        "label": label, "operationType": "date_histogram",
        "sourceField": "@timestamp",
        "params": {"interval": "auto", "includeEmptyRows": True, "dropPartials": False},
    }


def col_count(label="Count"):
    return {
        "customLabel": True, "dataType": "number", "isBucketed": False,
        "scale": "ratio", "label": label, "operationType": "count",
        "sourceField": "___records___",
        "params": {"emptyAsNull": False},
    }


def col_avg(field, label):
    return {
        "customLabel": True, "dataType": "number", "isBucketed": False,
        "scale": "ratio", "label": label, "operationType": "average",
        "sourceField": field,
        "params": {"emptyAsNull": False},
    }


def col_percentile(field, pct, label):
    return {
        "customLabel": True, "dataType": "number", "isBucketed": False,
        "scale": "ratio", "label": label, "operationType": "percentile",
        "sourceField": field,
        "params": {"percentile": pct, "emptyAsNull": False},
    }


def col_terms(field, label, size=10, order_col="c2", direction="desc", dtype="string"):
    return {
        "customLabel": True, "dataType": dtype, "isBucketed": True,
        "scale": "ordinal", "label": label, "operationType": "terms",
        "sourceField": field,
        "params": {
            "size": size,
            "orderBy": {"type": "column", "columnId": order_col},
            "orderDirection": direction,
            "otherBucket": False, "missingBucket": False,
        },
    }


def col_last_value(field, label):
    return {
        "customLabel": True, "dataType": "string", "isBucketed": False,
        "scale": "ordinal", "label": label, "operationType": "last_value",
        "sourceField": field,
        "params": {"sortField": "@timestamp", "showArrayValues": False},
    }


# ---------------------------------------------------------------------------
# Panel builder
# ---------------------------------------------------------------------------

def make_panel(pid, title, viz_type, x, y, w, h, dv_id, columns, col_order, visualization, query=""):
    return {
        "type": "lens",
        "gridData": {"x": x, "y": y, "w": w, "h": h, "i": pid},
        "panelIndex": pid,
        "title": title,
        "embeddableConfig": {
            "attributes": {
                "description": "",
                "title": "",
                "type": "lens",
                "visualizationType": viz_type,
                "state": {
                    "adHocDataViews": {},
                    "datasourceStates": {
                        "formBased": {
                            "layers": {
                                "l1": {
                                    "columnOrder": col_order,
                                    "columns": columns,
                                    "ignoreGlobalFilters": False,
                                    "incompleteColumns": {},
                                    "indexPatternId": dv_id,
                                    "sampling": 1,
                                }
                            }
                        },
                        "indexpattern": {"layers": {}},
                        "textBased": {"layers": {}},
                    },
                    "filters": [],
                    "internalReferences": [],
                    "query": {"language": "kuery", "query": query},
                    "visualization": visualization,
                },
                "references": [
                    {"id": dv_id, "name": "indexpattern-datasource-layer-l1", "type": "index-pattern"}
                ],
            },
            "enhancements": {},
        },
    }


def viz_metric(col, color="#6092C0"):
    return {"layerId": "l1", "layerType": "data", "metricAccessor": col, "showBar": False, "color": color}


def viz_xy(series_type, x_col, y_cols, split_col=None, y_extent=None):
    layer = {"layerId": "l1", "layerType": "data", "seriesType": series_type,
             "xAccessor": x_col, "accessors": y_cols}
    if split_col:
        layer["splitAccessor"] = split_col
    viz = {"preferredSeriesType": series_type, "layers": [layer],
           "legend": {"isVisible": True, "position": "right"}, "valueLabels": "hide"}
    if y_extent:
        viz["yLeftExtent"] = y_extent
    return viz


def viz_pie(group_cols, metric_col, shape="donut"):
    return {"shape": shape, "layers": [{
        "layerId": "l1", "layerType": "data",
        "primaryGroups": group_cols, "metrics": [metric_col],
        "numberDisplay": "percent", "categoryDisplay": "default", "legendDisplay": "default",
    }]}


def viz_table(col_ids):
    return {"layerId": "l1", "layerType": "data",
            "columns": [{"columnId": c} for c in col_ids]}


def make_dashboard(dash_id, title, desc, panels, time_from, time_to, tag_refs):
    # When top-level references is non-empty, Kibana expects panel data view
    # references here too (prefixed with panelIndex), not just inline.
    panel_refs = []
    for p in panels:
        pid = p["panelIndex"]
        for ref in p["embeddableConfig"]["attributes"]["references"]:
            panel_refs.append({
                "type": ref["type"],
                "id": ref["id"],
                "name": f"{pid}:{ref['name']}",
            })
    return {
        "attributes": {
            "title": title, "description": desc,
            "panelsJSON": json.dumps(panels),
            "optionsJSON": json.dumps({"useMargins": True, "syncColors": True, "syncCursor": True, "syncTooltips": True}),
            "kibanaSavedObjectMeta": {
                "searchSourceJSON": json.dumps({"query": {"query": "", "language": "kuery"}, "filter": []})
            },
            "timeRestore": True, "timeFrom": time_from, "timeTo": time_to,
        },
        "references": tag_refs + panel_refs,
    }


# ---------------------------------------------------------------------------
# Dashboard 1: Application Overview
# ---------------------------------------------------------------------------

def dash_app_overview():
    Q = "service.name: Nutrir AND processor.event: transaction AND transaction.type: request"
    panels = [
        # Row 1: Throughput + Error rate line
        make_panel("p1", "Request Throughput", "lnsXY", 0, 0, 24, 12, DV_APM,
            {"c1": col_date_histogram(), "c2": col_count("Requests")},
            ["c1", "c2"], viz_xy("line", "c1", ["c2"], y_extent={"mode": "dataBounds"}), Q),

        make_panel("p2", "Throughput by Outcome", "lnsXY", 24, 0, 24, 12, DV_APM,
            {"c1": col_date_histogram(), "c2": col_count("Count"),
             "c3": col_terms("event.outcome", "Outcome", size=5, order_col="c2")},
            ["c1", "c3", "c2"], viz_xy("area_stacked", "c1", ["c2"], split_col="c3"),
            "service.name: Nutrir AND processor.event: transaction"),

        # Row 2: KPI metrics
        make_panel("p3", "Avg Latency (us)", "lnsMetric", 0, 12, 12, 8, DV_APM,
            {"c1": col_avg("transaction.duration.us", "Avg Latency (us)")},
            ["c1"], viz_metric("c1", "#6092C0"), Q),

        make_panel("p4", "P95 Latency (us)", "lnsMetric", 12, 12, 12, 8, DV_APM,
            {"c1": col_percentile("transaction.duration.us", 95, "P95 Latency (us)")},
            ["c1"], viz_metric("c1", "#E7664C"), Q),

        make_panel("p5", "Total Requests", "lnsMetric", 24, 12, 12, 8, DV_APM,
            {"c1": col_count("Total Requests")},
            ["c1"], viz_metric("c1", "#54B399"), Q),

        make_panel("p6", "Failed Requests", "lnsMetric", 36, 12, 12, 8, DV_APM,
            {"c1": col_count("Errors")},
            ["c1"], viz_metric("c1", "#E7664C"),
            "service.name: Nutrir AND event.outcome: failure AND processor.event: transaction"),

        # Row 3: Status codes + log levels
        make_panel("p7", "HTTP Status Codes", "lnsXY", 0, 20, 24, 14, DV_APM,
            {"c1": col_date_histogram(), "c2": col_count("Count"),
             "c3": col_terms("http.response.status_code", "Status", size=10, order_col="c2", dtype="number")},
            ["c1", "c3", "c2"], viz_xy("bar_stacked", "c1", ["c2"], split_col="c3"),
            "service.name: Nutrir AND transaction.type: request AND http.response.status_code: *"),

        make_panel("p8", "Log Level Distribution", "lnsPie", 24, 20, 24, 14, DV_LOGS,
            {"c1": col_terms("log.level", "Level", size=10, order_col="c2"),
             "c2": col_count("Count")},
            ["c1", "c2"], viz_pie(["c1"], "c2", "donut"), ""),

        # Row 4: Recent errors table (message is match_only_text, can't sort/aggregate)
        make_panel("p9", "Recent Errors", "lnsDatatable", 0, 34, 48, 12, DV_LOGS,
            {"c1": col_date_histogram("Time"),
             "c2": col_terms("error.type", "Error Type", size=20, order_col="c3"),
             "c3": col_count("Count")},
            ["c1", "c2", "c3"], viz_table(["c1", "c2", "c3"]), "log.level: Error"),
    ]
    return make_dashboard("nutrir-app-overview", "[Nutrir] Application Overview",
        "Throughput, latency, error rates, and log summary.", panels, "now-24h", "now",
        [{"type": "tag", "id": "nutrir-tag", "name": "tag-ref-nutrir-tag"},
         {"type": "tag", "id": "nutrir-tag-apm", "name": "tag-ref-nutrir-tag-apm"},
         {"type": "tag", "id": "nutrir-tag-logs", "name": "tag-ref-nutrir-tag-logs"}])


# ---------------------------------------------------------------------------
# Dashboard 2: Endpoint Performance
# ---------------------------------------------------------------------------

def dash_endpoint_performance():
    Q = "service.name: Nutrir AND processor.event: transaction AND transaction.type: request"
    panels = [
        # Slowest endpoints table
        make_panel("p1", "Endpoints by Latency", "lnsDatatable", 0, 0, 48, 16, DV_APM,
            {"c1": col_terms("transaction.name", "Endpoint", size=20, order_col="c2"),
             "c2": col_avg("transaction.duration.us", "Avg (us)"),
             "c3": col_percentile("transaction.duration.us", 95, "P95 (us)"),
             "c4": col_count("Throughput")},
            ["c1", "c2", "c3", "c4"], viz_table(["c1", "c2", "c3", "c4"]), Q),

        # Top by throughput
        make_panel("p2", "Top Endpoints by Throughput", "lnsXY", 0, 16, 24, 14, DV_APM,
            {"c1": col_terms("transaction.name", "Endpoint", size=10, order_col="c2"),
             "c2": col_count("Requests")},
            ["c1", "c2"], viz_xy("bar_horizontal", "c1", ["c2"]), Q),

        # Latency over time
        make_panel("p3", "Latency Over Time", "lnsXY", 24, 16, 24, 14, DV_APM,
            {"c1": col_date_histogram(), "c2": col_avg("transaction.duration.us", "Avg (us)"),
             "c3": col_percentile("transaction.duration.us", 95, "P95 (us)")},
            ["c1", "c2", "c3"], viz_xy("line", "c1", ["c2", "c3"], y_extent={"mode": "dataBounds"}), Q),

        # API endpoints comparison
        make_panel("p4", "Endpoint Latency Comparison", "lnsXY", 0, 30, 48, 14, DV_APM,
            {"c1": col_date_histogram(), "c2": col_avg("transaction.duration.us", "Avg (us)"),
             "c3": col_terms("transaction.name", "Endpoint", size=10, order_col="c2")},
            ["c1", "c3", "c2"],
            viz_xy("line", "c1", ["c2"], split_col="c3", y_extent={"mode": "dataBounds"}),
            Q + " AND transaction.name: (*api* OR *Account* OR GET /)"),
    ]
    return make_dashboard("nutrir-endpoint-performance", "[Nutrir] Endpoint Performance",
        "Endpoint latency, throughput, and comparisons.", panels, "now-24h", "now",
        [{"type": "tag", "id": "nutrir-tag", "name": "tag-ref-nutrir-tag"},
         {"type": "tag", "id": "nutrir-tag-apm", "name": "tag-ref-nutrir-tag-apm"}])


# ---------------------------------------------------------------------------
# Dashboard 3: Database & Dependencies
# ---------------------------------------------------------------------------

def dash_db_dependencies():
    panels = [
        make_panel("p1", "DB Span Latency", "lnsXY", 0, 0, 24, 14, DV_APM,
            {"c1": col_date_histogram(), "c2": col_avg("span.duration.us", "Avg (us)"),
             "c3": col_terms("span.subtype", "DB Type", size=5, order_col="c2")},
            ["c1", "c3", "c2"],
            viz_xy("line", "c1", ["c2"], split_col="c3", y_extent={"mode": "dataBounds"}),
            "service.name: Nutrir AND span.type: db"),

        make_panel("p2", "DB Operations Throughput", "lnsXY", 24, 0, 24, 14, DV_APM,
            {"c1": col_date_histogram(), "c2": col_count("Operations"),
             "c3": col_terms("span.subtype", "DB Type", size=5, order_col="c2")},
            ["c1", "c3", "c2"], viz_xy("area_stacked", "c1", ["c2"], split_col="c3"),
            "service.name: Nutrir AND span.type: db"),

        make_panel("p3", "Dependency Breakdown", "lnsPie", 0, 14, 24, 14, DV_APM,
            {"c1": col_terms("span.destination.service.resource", "Dependency", size=10, order_col="c2"),
             "c2": col_count("Calls")},
            ["c1", "c2"], viz_pie(["c1"], "c2", "donut"),
            "service.name: Nutrir AND span.destination.service.resource: *"),

        make_panel("p4", "Anthropic API Latency", "lnsXY", 24, 14, 24, 14, DV_APM,
            {"c1": col_date_histogram(), "c2": col_avg("span.duration.us", "Avg (us)"),
             "c3": col_percentile("span.duration.us", 95, "P95 (us)")},
            ["c1", "c2", "c3"],
            viz_xy("line", "c1", ["c2", "c3"], y_extent={"mode": "dataBounds"}),
            "service.name: Nutrir AND span.destination.service.resource: api.anthropic.com*"),

        make_panel("p5", "EF Core Activity", "lnsXY", 0, 28, 48, 14, DV_LOGS,
            {"c1": col_date_histogram(), "c2": col_count("Events"),
             "c3": col_terms("event.action", "EF Action", size=8, order_col="c2")},
            ["c1", "c3", "c2"], viz_xy("bar_stacked", "c1", ["c2"], split_col="c3"),
            "log.logger: Microsoft.EntityFrameworkCore*"),
    ]
    return make_dashboard("nutrir-db-dependencies", "[Nutrir] Database & Dependencies",
        "PostgreSQL, SQLite, SignalR, and Anthropic API performance.", panels, "now-24h", "now",
        [{"type": "tag", "id": "nutrir-tag", "name": "tag-ref-nutrir-tag"},
         {"type": "tag", "id": "nutrir-tag-apm", "name": "tag-ref-nutrir-tag-apm"},
         {"type": "tag", "id": "nutrir-tag-logs", "name": "tag-ref-nutrir-tag-logs"}])


# ---------------------------------------------------------------------------
# Dashboard 4: Errors & Exceptions
# ---------------------------------------------------------------------------

def dash_errors():
    panels = [
        make_panel("p1", "Errors & Warnings Over Time", "lnsXY", 0, 0, 48, 12, DV_LOGS,
            {"c1": col_date_histogram(), "c2": col_count("Count"),
             "c3": col_terms("log.level", "Level", size=5, order_col="c2")},
            ["c1", "c3", "c2"], viz_xy("bar_stacked", "c1", ["c2"], split_col="c3"),
            "log.level: Error OR log.level: Warning"),

        make_panel("p2", "Error Type Breakdown", "lnsPie", 0, 12, 24, 14, DV_LOGS,
            {"c1": col_terms("error.type", "Exception Type", size=10, order_col="c2"),
             "c2": col_count("Count")},
            ["c1", "c2"], viz_pie(["c1"], "c2", "pie"), "error.type: *"),

        make_panel("p3", "Failed Transactions by Endpoint", "lnsXY", 24, 12, 24, 14, DV_APM,
            {"c1": col_terms("transaction.name", "Endpoint", size=10, order_col="c2"),
             "c2": col_count("Failed")},
            ["c1", "c2"], viz_xy("bar_horizontal", "c1", ["c2"]),
            "service.name: Nutrir AND event.outcome: failure AND processor.event: transaction"),

        # url.path is wildcard type — can't use last_value (top_metrics)
        make_panel("p4", "Error Detail Log", "lnsDatatable", 0, 26, 48, 16, DV_LOGS,
            {"c1": col_date_histogram("Time"),
             "c2": col_terms("error.type", "Exception", size=20, order_col="c4"),
             "c3": col_terms("url.path", "URL", size=20, order_col="c4"),
             "c4": col_count("Count")},
            ["c1", "c2", "c3", "c4"], viz_table(["c1", "c2", "c3", "c4"]),
            "log.level: Error"),
    ]
    return make_dashboard("nutrir-errors", "[Nutrir] Errors & Exceptions",
        "Error trends, exception types, and failed transactions.", panels, "now-7d", "now",
        [{"type": "tag", "id": "nutrir-tag", "name": "tag-ref-nutrir-tag"},
         {"type": "tag", "id": "nutrir-tag-apm", "name": "tag-ref-nutrir-tag-apm"},
         {"type": "tag", "id": "nutrir-tag-logs", "name": "tag-ref-nutrir-tag-logs"}])


# ---------------------------------------------------------------------------
# Dashboard 5: Infrastructure & Docker
# ---------------------------------------------------------------------------

def dash_infrastructure():
    panels = [
        make_panel("p1", "Host CPU %", "lnsXY", 0, 0, 24, 12, DV_MB,
            {"c1": col_date_histogram(), "c2": col_avg("system.cpu.total.pct", "CPU %")},
            ["c1", "c2"], viz_xy("area", "c1", ["c2"], y_extent={"mode": "custom", "lowerBound": 0, "upperBound": 1}),
            "event.module: system AND metricset.name: cpu"),

        make_panel("p2", "Host Memory %", "lnsXY", 24, 0, 24, 12, DV_MB,
            {"c1": col_date_histogram(), "c2": col_avg("system.memory.used.pct", "Memory %")},
            ["c1", "c2"], viz_xy("area", "c1", ["c2"], y_extent={"mode": "custom", "lowerBound": 0, "upperBound": 1}),
            "event.module: system AND metricset.name: memory"),

        make_panel("p3", "Container CPU", "lnsXY", 0, 12, 24, 14, DV_MB,
            {"c1": col_date_histogram(), "c2": col_avg("docker.cpu.total.pct", "CPU %"),
             "c3": col_terms("container.name", "Container", size=10, order_col="c2")},
            ["c1", "c3", "c2"],
            viz_xy("line", "c1", ["c2"], split_col="c3", y_extent={"mode": "dataBounds"}),
            "event.module: docker AND metricset.name: container"),

        make_panel("p4", "Container Memory", "lnsXY", 24, 12, 24, 14, DV_MB,
            {"c1": col_date_histogram(), "c2": col_avg("docker.memory.usage.pct", "Memory %"),
             "c3": col_terms("container.name", "Container", size=10, order_col="c2")},
            ["c1", "c3", "c2"],
            viz_xy("line", "c1", ["c2"], split_col="c3", y_extent={"mode": "dataBounds"}),
            "event.module: docker AND metricset.name: container"),

        make_panel("p5", "ES JVM Heap", "lnsXY", 0, 26, 48, 12, DV_MB,
            {"c1": col_date_histogram(),
             "c2": col_avg("elasticsearch.node.stats.jvm.mem.heap.used.pct", "JVM Heap %")},
            ["c1", "c2"], viz_xy("area", "c1", ["c2"], y_extent={"mode": "custom", "lowerBound": 0, "upperBound": 1}),
            "event.module: elasticsearch AND metricset.name: node_stats"),
    ]
    return make_dashboard("nutrir-infrastructure", "[Nutrir] Infrastructure & Docker",
        "Host CPU/memory, container resources, ES cluster health.", panels, "now-6h", "now",
        [{"type": "tag", "id": "nutrir-tag", "name": "tag-ref-nutrir-tag"},
         {"type": "tag", "id": "nutrir-tag-infra", "name": "tag-ref-nutrir-tag-infra"}])


# ---------------------------------------------------------------------------
# Dashboard 6: AI Assistant Activity
# ---------------------------------------------------------------------------

def dash_ai_assistant():
    panels = [
        make_panel("p1", "AI Transactions", "lnsMetric", 0, 0, 16, 8, DV_APM,
            {"c1": col_count("AI Transactions")}, ["c1"], viz_metric("c1", "#6092C0"),
            "processor.event: transaction AND transaction.type: ai AND service.name: Nutrir"),

        make_panel("p2", "AI Agent Log Entries", "lnsMetric", 16, 0, 16, 8, DV_LOGS,
            {"c1": col_count("Log Entries")}, ["c1"], viz_metric("c1", "#54B399"),
            "log.logger: Nutrir.Infrastructure.Services.AiAgentService"),

        make_panel("p3", "Anthropic API Calls", "lnsMetric", 32, 0, 16, 8, DV_APM,
            {"c1": col_count("API Calls")}, ["c1"], viz_metric("c1", "#D36086"),
            "span.destination.service.resource: api.anthropic.com* AND service.name: Nutrir"),

        make_panel("p4", "AI Agent Logs Over Time", "lnsXY", 0, 8, 48, 14, DV_LOGS,
            {"c1": col_date_histogram(), "c2": col_count("Entries"),
             "c3": col_terms("log.level", "Level", size=5, order_col="c2")},
            ["c1", "c3", "c2"], viz_xy("bar_stacked", "c1", ["c2"], split_col="c3"),
            "log.logger: Nutrir.Infrastructure.Services.Ai*"),

        make_panel("p5", "Anthropic API Latency", "lnsXY", 0, 22, 24, 14, DV_APM,
            {"c1": col_date_histogram(), "c2": col_avg("span.duration.us", "Avg (us)"),
             "c3": col_percentile("span.duration.us", 95, "P95 (us)")},
            ["c1", "c2", "c3"],
            viz_xy("line", "c1", ["c2", "c3"], y_extent={"mode": "dataBounds"}),
            "span.destination.service.resource: api.anthropic.com* AND service.name: Nutrir"),

        make_panel("p6", "AI Conversation Store", "lnsXY", 24, 22, 24, 14, DV_LOGS,
            {"c1": col_date_histogram(), "c2": col_count("Events")},
            ["c1", "c2"], viz_xy("bar", "c1", ["c2"]),
            "log.logger: Nutrir.Infrastructure.Services.AiConversationStore"),

        # message is match_only_text — can't sort/aggregate
        make_panel("p7", "AI Log Detail", "lnsDatatable", 0, 36, 48, 14, DV_LOGS,
            {"c1": col_date_histogram("Time"),
             "c2": col_terms("log.level", "Level", size=5, order_col="c3"),
             "c3": col_count("Count")},
            ["c1", "c2", "c3"], viz_table(["c1", "c2", "c3"]),
            "log.logger: Nutrir.Infrastructure.Services.Ai*"),
    ]
    return make_dashboard("nutrir-ai-assistant", "[Nutrir] AI Assistant Activity",
        "AI agent usage, Anthropic API performance, conversations.", panels, "now-24h", "now",
        [{"type": "tag", "id": "nutrir-tag", "name": "tag-ref-nutrir-tag"},
         {"type": "tag", "id": "nutrir-tag-ai", "name": "tag-ref-nutrir-tag-ai"},
         {"type": "tag", "id": "nutrir-tag-apm", "name": "tag-ref-nutrir-tag-apm"},
         {"type": "tag", "id": "nutrir-tag-logs", "name": "tag-ref-nutrir-tag-logs"}])


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def delete_all():
    log("Deleting all Nutrir dashboards...")
    for did in DASHBOARD_IDS:
        log(f"  {did}")
        kibana_request("DELETE", f"/api/saved_objects/dashboard/{did}?force=true")
    log("Deleting tags...")
    for tid in TAG_IDS:
        kibana_request("DELETE", f"/api/saved_objects/tag/{tid}?force=true")
    log("Deleting data views...")
    for dvid in DATAVIEW_IDS:
        kibana_request("DELETE", f"/api/data_views/data_view/{dvid}")
    log("Done.")


def create_all():
    log("=== Phase 1: Data Views ===")
    for dvid, title, name in [
        ("nutrir-logs", "logs-nutrir-*", "Nutrir Application Logs"),
        ("nutrir-metricbeat", "metricbeat-*", "Nutrir Metricbeat"),
    ]:
        log(f"  Creating: {name}")
        r = kibana_request("POST", "/api/data_views/data_view", {
            "data_view": {"id": dvid, "title": title, "name": name, "timeFieldName": "@timestamp"},
            "override": True,
        })
        log(f"    -> {r.get('data_view', {}).get('id', r.get('error', '?'))}")

    log("=== Phase 1b: Tags ===")
    for tid, name, color, desc in [
        ("nutrir-tag", "Nutrir", "#6DCCB1", "Nutrir application dashboards"),
        ("nutrir-tag-apm", "Nutrir: APM", "#6092C0", "APM dashboards"),
        ("nutrir-tag-logs", "Nutrir: Logs", "#D6BF57", "Log dashboards"),
        ("nutrir-tag-infra", "Nutrir: Infra", "#54B399", "Infrastructure dashboards"),
        ("nutrir-tag-ai", "Nutrir: AI", "#D36086", "AI assistant dashboards"),
    ]:
        log(f"  Creating: {name}")
        r = kibana_request("POST", f"/api/saved_objects/tag/{tid}?overwrite=true",
                           {"attributes": {"name": name, "description": desc, "color": color}})
        log(f"    -> {r.get('id', r.get('error', '?'))}")

    log("=== Phase 2: Dashboards ===")
    builders = [
        dash_app_overview, dash_endpoint_performance, dash_db_dependencies,
        dash_errors, dash_infrastructure, dash_ai_assistant,
    ]
    for builder, did in zip(builders, DASHBOARD_IDS):
        log(f"  Creating: {did}")
        body = builder()
        r = kibana_request("POST", f"/api/saved_objects/dashboard/{did}?overwrite=true", body)
        log(f"    -> {r.get('id', r.get('error', '?'))}")

    log("=== Phase 3: Verification ===")
    r = kibana_request("GET", "/api/saved_objects/_find?type=dashboard&search=Nutrir&search_fields=title&per_page=20")
    log(f"Found {r.get('total', 0)} dashboards:")
    for obj in r.get("saved_objects", []):
        log(f"  - {obj['attributes']['title']} ({obj['id']})")
    log(f"=== Done === View at: {KIBANA_URL}/app/dashboards")


if __name__ == "__main__":
    if "--delete" in sys.argv:
        delete_all()
    else:
        create_all()
