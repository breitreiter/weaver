# weaver — telemetry realism (steering the fiction toward real traces)

Status: **first cut landed** (items ① + ②) — datagen + backend plumbing done,
verified on scratch DBs; **canonical regen + API restart still owed** (see below).
Items ③–⑥ remain on record as a later "v2 dataset" milestone.

### Decisions locked

1. **`trace_id`/`span_id` are first-class columns** on `log_events` — not stuffed
   into the `fields` JSON. The correlation is the headline; it shouldn't hide in a
   blob. Accepts the four-file plumbing pass.
2. **Both datasets get the pivot.** Correlated logs fire on baseline error/timeout
   spans too, not only incident spans — so healthy `weaver.db` shows log↔trace
   correlation as well. More honest (real systems correlate everything), and the
   capability isn't secretly incident-only.
3. **Sample the fraction, but guarantee the demo beat.** ~30–50% of eligible spans
   get a correlated log for realism — *except* the culprit `payments-db` spans,
   which are **force-correlated** so the presented pivot never lands on a span with
   no log.
4. **UI link is a fast-follow**, not part of this cut. Land the data + verify the
   pivot via CLI/DTO first; the web affordance is its own small change.

## Why this exists

We are **not** porting off SQLite, and we are **not** writing a Grafana Cloud
back-fill. The generated SQLite stays. The immediate job is a **design case
study**, and the demo (the flash-sale / email-blast incident) must keep telling
its story — the authored richness (named routes, `critical` edge into
`payments-db`, clean subsystems, the planted `db.pool.exhausted` mechanism) is
the story, not debt.

What we *are* doing: making the on-screen telemetry **look the way real OTel /
Grafana telemetry looks**. This pays off twice — it makes the narrative more
convincing ("this is real observability data") and it quietly pre-pays a future
port, because the shapes already match. See `demo-vs-production.md` for the
seam philosophy this extends.

## The organizing principle: converge what's on screen, defer what's under the hood

Split "realism" by whether the audience sees it.

- **On-screen shapes** (traces, spans, logs as rendered) — converge now. Improves
  the narrative *and* pre-pays the port.
- **Under-the-hood model** (metrics as raw Prometheus instruments, unix
  timestamps, resource-attribute identity renaming) — pure port-cost the audience
  never sees, and exactly what would break the spoken demo vocabulary ("p99
  latency"). **Defer to a real port.** Earns nothing now.

Everything in the first cut is the first bucket.

## Where the generator stands today (grounded in `tools/datagen/generate.py`)

Already realistic — keep as-is:
- trace/span ids are 128-bit random (`uuid.UUID(int=rng.getrandbits(128))`)
- span attributes are namespaced (`db.pool_wait_ms`, `db.exec_ms`, `db.pool_max`)
- log templates read like Drain patterns (`db.pool.exhausted`, `upstream.timeout`,
  `http.503`); log `fields` are structured JSON

Diverges from real telemetry:
1. **logs carry no `trace_id`/`span_id`** — the "pool exhausted" log and the
   errored `payments-db` span are the *same event*, but nothing links them
2. every span is `kind: "server"`; the edge rides on a weaver-specific `edge_id`
3. `status: "timeout"` is not an OTel status (`UNSET|OK|ERROR` + a reason)
4. ids are UUID-with-dashes; OTel is lowercase hex (trace = 32, span = 16)
5. spans lack `http.*` semantic attrs the *logs* already carry

---

## First cut — ① log↔trace correlation + ② OTel-format ids

Both are datagen-only. SQLite schema gains **nullable additive columns**; the
demo's spoken flow, metric names, and trace waterfall are unchanged.

### ② OTel-format ids (do first — trivial, unblocks ①'s span refs)

In `generate.py`:
- trace id → 32 lowercase hex: `f"{rng.getrandbits(128):032x}"`
- span id → 16 lowercase hex: `f"{rng.getrandbits(64):016x}"`
- replace the two `str(uuid.UUID(int=rng.getrandbits(128)))` sites in `_emit_trace`
- `uid()` in `_gen_logs` stays a UUID (log *ids* aren't OTel trace ids)

No consumer parses these as UUIDs. The API's `tr:<id>` typed-id wrapper just
concatenates (fine); `TraceResult`'s `t.Id[..8]` slice still works on hex.
**Touch: `generate.py` only.**

### ① `trace_id` / `span_id` on logs — the correlation pivot

Model it the way real systems produce it: a correlated log is **emitted during
request handling**, so it carries the trace/span it happened under. Ambient
per-minute logs stay uncorrelated (realistic — most logs aren't on a sampled
trace). We add a *subset* of incident logs that pivot to traces — exactly the
beat we demo.

**Approach:** emit the correlated log from inside `_emit_trace`. When a span is
`error`/`timeout` on **any** service (baseline or incident — decision 2), append a
matching log row carrying that span's `trace_id` + `span_id`. This gives exact
linkage ("this slow trace → its hot span → the log line on that span"), and it
means healthy `weaver.db` shows the pivot too.

Steps:

1. **Schema** — `build_schema()`, `log_events` table: add
   `trace_id TEXT, span_id TEXT` (both nullable, trailing).
2. **INSERT arity** — `log_events` INSERT goes from 7 to 9 placeholders. To avoid
   editing every `rows.append(...)` in `_gen_logs` by hand, add a small helper:
   ```python
   def logrow(id, sid, ts, level, tmpl, msg, fields, trace_id=None, span_id=None):
       return (id, sid, ts, level, tmpl, msg, fields, trace_id, span_id)
   ```
   Route all existing appends through it (trace_id/span_id default `None`).
3. **Correlated emission** — pass the shared `log_rows` list into `_emit_trace`.
   For each errored/timeout span, append a `logrow(...)` whose `trace_id`/`span_id`
   = that span's ids, reusing the same template text already used in `_gen_logs`
   (`db.pool.exhausted`, `upstream.timeout`, `http.503` for incident spans; the
   `kind`'s baseline error template otherwise) so the wording stays consistent.
   Sampling (decision 3): emit for ~30–50% of eligible spans so it reads sampled,
   **but force-correlate every errored/timeout `payments-db` span** so the demo's
   hot-span pivot never lands on a span with no log.
4. **FTS** — `log_events_fts` indexes only `(message, fields)` via an explicit
   column list, so the new columns need no FTS change.

**Backend plumbing (thin, mechanical):**
- `src/Weaver.Core/Entities.cs` — `LogEventEntity`: add `TraceId`, `SpanId` (string?)
- `src/Weaver.Core/WeaverDbContext.cs` — map `trace_id` / `span_id`
- `src/Weaver.Contracts/Dtos.cs` — `LogEventDto`: add `TraceId`, `SpanId`
- `src/Weaver.Api/Program.cs` — `ToLogDto` projection carries the two fields

**UI (fast-follow — decision 4, not this cut):**
- `web/` log rows: when `traceId` present, render a link/affordance to the trace
  drawer. Out of scope here — land the data first and verify the pivot via CLI
  (`weaver logs`, `search logs`) and the raw DTO, then do the web affordance as its
  own small change.

### Regen + verify

- Regenerate **both** datasets (`weaver.db`, `weaver-flash-sale.db`) — ask Joseph
  to run the generator; don't assume it's wired to a task.
- Sanity: `SELECT COUNT(*) FROM log_events WHERE trace_id IS NOT NULL;` > 0 in
  **both** DBs (decision 2); pick one such row and confirm its `trace_id` matches a
  real trace and `span_id` matches that trace's span. In the flash-sale DB, confirm
  every errored/timeout `payments-db` span has a correlated log (decision 3's
  force-correlate).
- Demo click-path unchanged: `search anomalies` → hot trace → span on `payments-db`
  → **new**: the `db.pool.exhausted` log on that span. Walk it once before showing.

## Demo-safety checklist (must all hold)

- [ ] additive nullable columns only — no column renames, no dropped fields
- [ ] metric names untouched (no p99→histogram reshaping in this cut)
- [ ] trace waterfall shape untouched (no client/server split in this cut — that's ③)
- [ ] spoken demo vocabulary unchanged
- [ ] both DBs regenerate clean; the existing flow works end-to-end after regen

---

## Later — v2 dataset milestone

On record so the fiction keeps steering the right way. These change on-screen
shape and need a rehearsal pass before presenting.

- **③ client + server span pairs.** Makes the topology *derivable from the
  traces* (client→server pairs) instead of authored alongside them — how Tempo's
  service graph actually works, and the strongest rebuttal to the case-study
  critique "you authored the answer; the graph *is* the conclusion." Roughly
  doubles spans per hop and reshapes the waterfall → new dataset version, rehearse.
- **④ OTel status model** — `ERROR` + `http.response.status_code: 504` instead of
  a `timeout` enum value; keep the visible "timed out" notion.
- **⑤ semantic attr keys on spans** — `db.system`, `server.address`,
  `http.request.method`, `http.route`, `http.response.status_code`.
- **⑥ severity** — `severity_text` / `severity_number` on logs alongside `level`.

## Explicitly deferred to a real port (audience never sees these)

- metrics as raw Prometheus instruments (histogram `_bucket`, counters), p50/p99 /
  error_rate derived as queries
- unix timestamps in place of ISO strings
- identity renamed to resource-attribute conventions (`service.name`,
  `service.namespace`)

## Grounding reference — Grafana o11y-bench

`github.com/grafana/o11y-bench/tree/main/tasks-spec/investigation` — a set of real
investigation task specs (statement + grounded rubric) that externally validate
weaver's premise and hand us concrete shape targets. Mine these when writing the
demo script and when deciding what "realistic" means.

What it confirms:
- The three backends are **Loki / Prometheus / Tempo** — our three.
- weaver's cause-vs-collateral thesis is the bench's thesis (`dependency-outage-
  false-lead`: "is the gateway the problem, or is something behind it failing and
  this is just how it shows up to users?"). Rubrics reward: cite a real trace id,
  tie claims to log+metric evidence, separate primary vs knock-on, no verdict /
  no next-steps before actually querying. That's weaver's manner, graded.
- **Trace-id grounding is a first-class check** (`type: grounding, mode:
  tool_trace_id, prefix_min_chars: 8`) → validates ② (real hex ids) and ① (the
  log↔trace pivot); "which backend service first fails in a representative trace"
  is our `self_ms` span drill.

Concrete shape targets it reveals:
- **Logs:** structured, LogQL `| json`, fields like `path="/api/payments"`,
  `status >= 500`, `level="info"`. Our log `fields` already carry `status`/`route`;
  nudging `route`→`path` matches the bench's endpoint-centric framing.
- **Deploys are structured info *logs*** (`event="deployment"`), not a separate
  change-events table — a realism signal for how `change_events` could surface.
- **Metrics:** raw Prometheus counters (`http_requests_total{status=~"5..",
  job=…}`), 5xx share = `sum(rate(5xx))/sum(rate(all))`. This is the deferred
  "query-model realism" (raw instruments over rolled-up `error_rate`/`latency_p99`)
  — port-time, not first cut.

Divergences to weigh (not decided):
- **Naming:** bench uses `payment-service` / `order-service` / `api-gateway`
  (identity = `job` label). Ours is richer (a `payments-db` tier where the
  mechanism actually lives) — don't flatten it, but aligning a few names is a cheap
  "same universe" tell. Ripples through `topology-flashsale.yaml` + ground-truth.
- The `statement` blocks are a **ready-made demo-script library** — realistic
  responder phrasing to lift or adapt.

## The one guardrail going forward

When adding any query or field, ask: **does this lean on a shape a real backend
also has (a series, an edge list, a log line, a span tree), or on something only
the generator knows?** Both are legitimate — the demo is allowed authored
garnish — but book the answer consciously so the fiction never becomes
load-bearing in the portable core where it didn't need to be. `request_types` /
routes are the current concentration point of authored-only dependence; leave
them, just don't deepen them.
