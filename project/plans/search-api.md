# weaver — search API (the left-panel query layer)

The query layer behind "legit search" (`sensemaking-pivot.md`, left panel).
All of it sits over the **existing read-only telemetry** — no schema change
(see the data-model assessment). Three endpoint groups: **facets**, **search**,
**node evidence**. Everything here *enumerates* — it filters and lists facts; it
never ranks by likelihood-of-cause (that's the operator's job —
`analysis-architecture.md`).

## 1. Facets — populate the structured-query controls

```
GET /api/search/facets
-> {
  window:        { start, end },          // min/max ts in the data
  subsystems:    string[],                // SELECT DISTINCT …
  kinds:         string[],                // gateway|api|worker|db|cache|queue|external
  teams:         string[],
  metrics:       string[],                // latency_p99, error_rate, …
  logLevels:     string[],
  logTemplates:  string[],
  routes:        string[],                // request_type ids
  traceStatuses: string[],                // ok|error|timeout
  changeKinds:   string[],                // deploy|config|migration|feature_flag
  knowledgeSources: string[],             // doc|runbook|incident|board
}
```

Pure `SELECT DISTINCT` over the telemetry. Drives the facet dropdowns and the
base-period picker (the `window` bounds the "vs base" choices).

## 2. Search — structured query → typed results

One endpoint. A **scope** picks the result kind; **facets** filter where they
apply; **free text** drives log FTS / id match. Returns a uniform envelope of
typed results, each already carrying *how to pin it*.

```
GET /api/search
  ?scope = services | anomalies | logs | traces | metrics | changes | knowledge
  &q          free text (log FTS5 / knowledge FTS5; service-id match)
  &subsystem= &kind= &team=        service facets (apply to every scope via the subject's service)
  &level= &template=               logs
  &route= &status= &minMs=         traces
  &metric= &subjectKind=           metrics
  &source=                         knowledge (doc|runbook|incident|board)
  &from= &to=                      window (IGNORED by knowledge — it's timeless)
  &split= &z= &minPct=             anomalies/timeline params
  &limit=
-> SearchResult[]
```

### The result envelope

Every result is typed, self-describing, and pre-resolves its pin:

```
SearchResult {
  type:     "service" | "edge" | "anomaly" | "log" | "trace" | "metric"
  id:       string          // stable per-row id (pin-tracking, de-dup)
  title:    string          // native-English headline
  subtitle: string
  payload:  object          // type-specific, for the rich card
  pin:      PinTarget       // node + evidence to layer (see board model)
}

PinTarget {
  nodeIds:  string[]              // the service node(s) this pins onto
  evidence?: {                    // absent for a bare service pin
    kind:    "metric" | "log" | "trace" | "anomaly",
    aspect:  string,              // "latency_p99" | "db.pool.timeout" | "route:checkout"
    at:      string | {from,to},  // time t or window
    payload: object               // the snapshot proof
  }
}
```

`pin` is the bridge to the board: pinning a result resolves to **node(s) +
layered evidence** (the refactored board model). The search card and the board
evidence share one shape.

### Per-scope behaviour

| scope | title / subtitle | pin.nodeIds | evidence |
|---|---|---|---|
| `services` | `id` / `kind · subsystem` | `[id]` | none (you're pinning the node) |
| `anomalies` | `svc metric +X%` / `z · dir · onset` | `[subjectId]` | `{anomaly, metric, onset, AnomalyDto}` |
| `logs` | `message` / `level · svc · time` | `[serviceId]` | `{log, templateId, ts, LogEventDto}` |
| `traces` | `route Nms status` / hot hop | participant svcs (root + hottest `self_ms`) | `{trace, "route:R", startedAt, span breakdown}` |
| `metrics` | `svc metric` / shape | `[subjectId]` | `{metric, metric, window, shape_code + prose}` |
| `changes` | `summary` / `kind · ts · target` | `[targetId]` (or `[]` fleet-wide) | `{change, kind, ts, ChangeEventDto}` |
| `knowledge` | `title` / `source · svc · part` | `[serviceId]` | `{knowledge, sourceRef\|source, at:null, snippet}` |

Reuses what's built: `Analysis.Anomalies/Timeline`, FTS5 logs, the `Trajectory`
encoder for metric payloads. Ordering is neutral (magnitude / recency /
duration / service), never a cause-score.

> **Knowledge is the one scope backed by a NEW stored table** — the rest are
> pure query-layer over the read-only telemetry, but the knowledge bag is a
> genuine new data source (a `knowledge_snippets` table + FTS5, emitted by
> datagen). It's still read-only observation; it just isn't *derived* from the
> telemetry. Timeless → the pin's evidence `at` is `null` and the window facets
> skip it. Canonical spec: `knowledge-snippets.md`.

> Cross-type unified search ("everything matching `q`, mixed") is a later
> enhancement; v1 is one scope per query (mirrors the CLI verbs).

## 3. Node evidence — the dossier behind the panel + the pin

"What's at **node X**, over **window W**?" — the fan-out across signals that
powers the evidence panel and the pin menu. Each entry is shaped to become a
`PinTarget.evidence`.

```
GET /api/nodes/{id}/evidence?from=&to=&base=
-> {
  node:   ServiceDto,
  window: { from, to },
  signals: [ { metric, shapeCode, prose, deltaPct? } ],   // per-metric trajectory (vs base if given)
  logs:    [ { templateId, level, count, sample, newSinceBase } ],  // grouped; "new template since base" is high-signal
  traces:  { participated, slow:[…], errored:[…], hotSpans:[{route, self_ms, status}] },
  edges:   [ { edgeId, peer, direction, deltaSummary } ], // edges touching this node
  knowledge: [ { refId, source, sourceRef, title, excerpt } ], // NOT window-filtered — snippets are timeless
}
```

This is the time-scoped, multi-signal view of one node — plus the timeless
knowledge attached to it (the window filters skip that section, like the trace
count). The evidence panel
renders it; the operator (or agent) pins slices of it onto the board. It's the
same dossier whether reached from a search result or from clicking a board node.

## How it composes

```
facets ──> structured query controls
search ──> typed SearchResult[] (each carries pin = node + evidence)
   │
   └─ pin ─> board: ensure node(s), layer evidence   (refactored board model)

node/{id}/evidence ──> evidence panel  ──> pin slices ─> board
```

One evidence shape, three surfaces (search card, node dossier, board node). No
new telemetry columns — this is all query-layer over the read-only store.
