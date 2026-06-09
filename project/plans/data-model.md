# weaver — data model

The canonical schema for weaver's exemplar data. This doc is the
**handoff spec for the data generator** (Sonnet + a Python script that
spews out graph/metrics/logs) and the contract the .NET backend's
EF/SQLite layer implements. Backend DTOs (`Weaver.Contracts`) mirror
these shapes.

## The governing principle: a mystery, not an oracle

weaver does **not** ship the answer. The exemplar data contains a
*latent, real, solvable incident with no answer label anywhere.* The
investigator (a human, or live-demo Claude) reasons from evidence to a
conclusion using weaver's tools.

Consequences that bind everything below:

- **No `status`/`health` column on services or edges.** Health is
  *derived* from metrics relative to a baseline window. A stored health
  flag would be a leaked answer.
- **No `root_cause` field on incidents.** An incident knows only its
  *symptoms* (where alerts fired — which are typically downstream red
  herrings) and its time window. The cause is recoverable only by
  analysis.
- **The mechanism is planted, not the verdict.** The generator knows the
  ground truth so it can produce a *coherent* failure: the culprit
  degrades first; downstream degrades after, with a lag; the culprit's
  logs are specific ("pool exhausted"), downstream logs are generic
  ("upstream 503 / timeout"). Distinguishing originating error from
  downstream timeout *is* the detective work — and it is emergent from
  the data, never tagged.
- **The answer key never enters the repo.** Per incident the generator
  writes a `ground-truth.md` to `project/private/` (gitignored) so *we*
  can verify the puzzle is solvable. It is never loaded by the API and
  never seen by demo-Claude.

The analysis surface is **tools, not verdicts** (see below): they expose
and compute over facts, but none of them says "this is the cause."

## Entities

Convention: timestamps are UTC ISO-8601. IDs are stable kebab-case slugs
(e.g. `payments-db`) unless noted.

### Service (node)

| field | type | notes |
|---|---|---|
| `id` | slug | stable identity, used everywhere |
| `name` | string | display name |
| `kind` | enum | `gateway` \| `api` \| `worker` \| `db` \| `cache` \| `queue` \| `external` |
| `subsystem` | slug? | grouping for the "collapse healthy subsystems" story (e.g. `payments`) |
| `owner_team` | string? | flavor; possible analysis signal |

No health/status column — derived from metrics.

### Dependency (edge)

| field | type | notes |
|---|---|---|
| `id` | slug | |
| `from_service` | slug | the **dependent** — `from` DEPENDS ON `to` |
| `to_service` | slug | the **dependency** |
| `kind` | enum | `sync_http` \| `grpc` \| `db_query` \| `cache_get` \| `async_publish` |
| `critical` | bool? | optional; is this edge on a critical path |

Direction is load-bearing: if `to` is unhealthy, `from`'s unhealthiness
is *explained* by it. No health column.

### MetricSample (time series — the heart of "yields to analysis")

Narrow/long table so we can model arbitrary telemetry and query any
metric over any window.

| field | type | notes |
|---|---|---|
| `subject_kind` | enum | `service` \| `edge` |
| `subject_id` | slug | service id or edge id |
| `ts` | timestamp | sample time |
| `metric` | enum | `latency_p50` \| `latency_p99` \| `error_rate` \| `throughput_rps` \| `saturation` \| `queue_depth` \| `pool_used` \| … |
| `value` | double | |

### LogEvent

| field | type | notes |
|---|---|---|
| `id` | string | |
| `service_id` | slug | |
| `ts` | timestamp | |
| `level` | enum | `info` \| `warn` \| `error` \| `fatal` |
| `template_id` | string | log fingerprint — groups "this error spiked" |
| `message` | string | rendered line |
| `fields` | json | structured attrs, e.g. `{"error":"pool exhausted","pool_max":50}` |

Logs carry the *qualitative* signal that separates cause from collateral.

### Incident

| field | type | notes |
|---|---|---|
| `id` | slug | |
| `title` | string | symptom-level & vague: "Checkout error rate elevated" |
| `started_at` | timestamp | when the mechanism began (latent) |
| `detected_at` | timestamp | when alerts fired |
| `alerting_services` | slug[] | where alerts fired = the red herrings (downstream) |

No `root_cause`.

### RequestType (route)

The generator uses routes to drive realistic load *and* to synthesize
traces (see Traces below — now in-scope per the brief).

| field | type | notes |
|---|---|---|
| `id` | slug | |
| `name` | string | e.g. `checkout` |
| `path` | slug[] | ordered service ids; consecutive pairs must be existing Dependencies |
| `weight` | double | relative request frequency, drives load |

### Trace & Span

A trace is one request walking a `RequestType.path`; spans are its hops.
Required signal per the brief, and what unifies the microservice and
monolith cases (see Scale & the monolith case).

**Trace**

| field | type | notes |
|---|---|---|
| `id` | string | |
| `request_type_id` | slug | which route this request took |
| `root_service_id` | slug | entry point (e.g. `gateway`) — where the user feels it |
| `started_at` | timestamp | |
| `duration_ms` | int | end-to-end; the user-facing latency |
| `status` | enum | `ok` \| `error` \| `timeout` — the **end-user outcome** |

**Span** (one per hop)

| field | type | notes |
|---|---|---|
| `id` | string | |
| `trace_id` | string | |
| `parent_span_id` | string? | null for the root span |
| `service_id` | slug | the service handling this hop |
| `edge_id` | slug? | the call into this hop (parent.service → service); null for interior work |
| `kind` | enum | OTel-aligned: `server` \| `internal` \| `client`. `internal` = traffic that never exits the node |
| `start_offset_ms` | int | from trace start |
| `duration_ms` | int | total time in this span (incl. children) |
| `self_ms` | int | time attributable to *this* service — where latency accrues |
| `status` | enum | `ok` \| `error` \| `timeout` |
| `attributes` | json | e.g. `{"db.pool_wait_ms": 1900}` |

`self_ms` is the payoff: a slow trace shows *which hop* ate the time, and
the culprit's span carries the originating error while ancestor spans
show `timeout`/waiting — the same cause-vs-collateral signal as logs,
per request.

**Interior spans (`kind: internal`)** are operations within a service
that never cross to another modeled node — `db.connection_acquire`,
`serialize_response`. They make the graph *fractal*: a node zoomed in is
its own graph of interior operations, which is also how the monolith
case is modeled. This reaches interior root causes (pool exhaustion
*inside* payments-db, not a dependency of it). They are **operations,
not code** — same trick as traces, no call graph.

> **⚠ RISK — scope creep.** Interior-span generation + node-zoom is
> additive surface on a 5–6h budget; build it **last**, only if time
> allows. The data model supports it now (one `kind` field), so deferring
> the *generation* costs nothing. High payoff if it lands. See the fractal
> section in `view-model.md`.

### View (curated finding) — created from outside, never precomputed

A View captures a conclusion an investigator *reached*, pinned for
sharing. The CLI `POST`s it; the FE renders it at `/view/:id`. It is not
a server-computed verdict.

| field | type | notes |
|---|---|---|
| `id` | slug | |
| `title` | string | |
| `created_at` | timestamp | |
| `incident_id` | slug? | context |
| `focus_window` | {from, to} | scopes metrics in the rendered view |
| `node_ids` | slug[] | the filtered subgraph |
| `edge_ids` | slug[] | |
| `decorations` | object[] | `{ target, role: suspect\|blast_radius\|healthy, note }` |
| `narrative` | markdown | what the investigator concluded |

## Analysis surface — facts in the backend, correlation in the toolkit, verdicts nowhere

Two hard lines, decided deliberately:

1. **The backend serves facts, not analysis.** Its API is a read surface over
   observed telemetry. It computes no anomalies, no rankings, no partitions —
   it answers *"what was observed for X over window W,"* nothing more.
2. **No layer returns a verdict.** Not the backend, not the toolkit. There is no
   `analyze-incident` route that names a cause, and no `interesting-subgraphs`
   route that pre-cleaves the graph. Those hand over the conclusion — they are
   the oracle this project argues against, and they evaporate the live
   investigation that *is* the demo. **Banned.**

### Backend endpoints — facts only

- `metrics` — query time series (subject, metric, window). Raw facts.
- `logs` — query logs (service, level, window, FTS5 text match). Raw facts.
- `traces` — query traces by route/status/duration; drill into spans to
  see *which hop* is slow or erroring. Raw facts.
- `topology` / `neighbors` / `paths` — the graph as observed. Raw facts.

### Correlation — in the consumer/CLI/agent layer, composed over those facts

*Mechanical correlation, not conclusions.* These narrow the search; the causal
judgment stays with the reasoner. They live in the toolkit the agent drives
(built from backend facts), **not** as backend endpoints — so the server stays a
pure observation surface:

- `anomalies` — given window vs. baseline, metrics that deviated
  (z-score / change-point). Says "these moved," not "this is the cause."
- `timeline` — order anomaly *onset times* across services. Reveals
  precedence; reading precedence as causation is the investigator's call.
- `blast-radius` — given a candidate node, the downstream reachable set
  (pure graph computation). *Tests* a hypothesis; it does not generate one.

**Why this split.** At several-hundred services the agent cannot eyeball raw
metrics for everything, so it needs cheap mechanical correlation to navigate
(see `agent-workflow.md`, claims 1–2) — hence these tools must exist. But
correlation that lived *in the backend, returned as a ranking,* would be the
black box. So: facts are served, correlation is composed by the toolkit, and the
cause is reasoned — never returned. The mystery's difficulty therefore lives in
the **data's texture** (downstream symptoms, cause-vs-collateral, decoy
anomalies), not in any tool.

## Generation defaults (for the Sonnet + Python script)

- Window: ~90–120 min. Sample cadence: 30s.
- A calm **baseline prefix**, then incident onset around T+30m — so
  `anomalies` has a clean baseline to compare against. This prefix is also
  the default **comparison base** (pre-deploy / last-known-good) for the
  differential projection — RCA is differential, so the data must contain
  a clean "before" (see `view-model.md`).
- *(Optional, high-signal)* plant a **structural delta**: during the
  incident a new edge appears (a retry/fallback path kicking in), so the
  topology-as-diff surfaces a "new dependency since base" — often the
  smoking gun. Keep it light; flavor, not required.
- Topology: ~15–25 services, a realistic e-commerce-ish system with
  identifiable subsystems.
- Plant one coherent mechanism per incident (e.g. `payments-db`
  connection-pool exhaustion cascading up through payments → checkout →
  gateway). Propagate effects with realistic lag.
- Generate **traces** by sampling routes: for each `RequestType`, emit
  traces at its `weight`, walking the path and drawing each hop's
  `self_ms`/`status` from that service/edge's metric distribution *at the
  trace's timestamp*. During the incident the culprit's spans inflate or
  error, propagating up the path as longer `duration_ms` and ancestor
  `timeout`s. No fake codebase — just (service, time, status) along a
  known route.
- *(Only if pursuing zoom — see RISK above)* emit a few `internal` spans
  for the culprit service (e.g. a `db.connection_acquire` that inflates
  during the incident) so the interior root cause is visible on zoom.
- Emit telemetry only to the DB. Write the ground truth to
  `project/private/ground-truth-<incident>.md`.

## Traces — the trick (in-scope)

A trace = a sampled path over the topology we already have. No authored
call graph, no fake codebase: we model request *routes* (which is how
developers think), and a trace is one walk of a route with per-hop
latency/status drawn from the metric distributions. The payoff is
`self_ms` per span — a slow trace points at the exact hop, and the
hop's span carries the originating error while ancestors show timeouts.

## Storage & the selector power-tool

**Store: SQLite (EF Core), full stop.** Worst-case volume — several
hundred services × every signal × a bounded window — is single-digit
millions of rows. SQLite with the right indexes answers that instantly;
Lucene/ClickHouse/FeatureBase would be over-engineering for scale we
don't have (and a judgment red flag). Log full-text uses **SQLite FTS5**
(built in, no new dependency). Index hot paths: `MetricSample(subject_id,
metric, ts)`, `LogEvent(service_id, ts)` + FTS5 on `message`/`fields`,
`Span(trace_id)` and `Trace(request_type_id, status)`.

**The power lives in the selector layer, not the engine.** The
"power-tool" is a composable C# selector grammar — predicates that
resolve to a *subgraph selection* (a set of services + edges):

- attribute — `kind:db`, `subsystem:payments`, `team:x`
- metric — `metric:error_rate > baseline.p99 @[window]`, `anomalous(latency_p99)`
- topology — `blast-radius(svc)`, `downstream-of(svc)`, `on-path-to(gateway)`
- trace — `in-traces(route:checkout status:error)`
- logs — `log-matches("pool exhausted")` (→ FTS5)
- compose — `and` / `or` / `not`, plus `+context(n)` to expand a
  selection by n hops so it stays a legible neighborhood, not node-dust.

A selection becomes a View. This is the gatekeeper for the
earn-your-pixels rule: a URL encodes a selector (ad-hoc) or references a
stored View id (durable, Slack/ticket-pasteable). **No selection → no
render. There is no URL that draws the whole graph**, because that isn't
a valid selection.

**Prod nuance (keep in pocket):** the selector layer is store-agnostic.
At real trace volumes (billions of spans) you swap SQLite for a columnar
store like ClickHouse behind the same query interface — but for this
demo's data, that would be malpractice.

## Scale & the monolith case

The brief spans a monolith → several hundred microservices; the design
must hold at both ends.

- **Hundreds of services:** the selector + earn-your-pixels rules are
  what keep it legible — we never render the hairball, only the narrowed
  neighborhood a hypothesis selected.
- **Monolith (N=1):** topology stops carrying signal, so *traces* do. We
  model a monolith as a graph of **modules/endpoints discovered from
  span service names**, so the identical investigation loop (selectors,
  anomalies, timeline, blast-radius) runs at N=1 — just at intra-process
  granularity. Same toolkit, finer grain. This is why traces being
  required is a gift: they unify both ends.
