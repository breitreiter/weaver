> **DECISION (2026-06-10) — superseded; folded into a three-panel evidence model.**
> The standalone "chart wall" and its `/api/search/histogram` endpoint are **cut.**
> The node-evidence drawer and the chart wall were the same surface seen at two
> zooms: the board is the *structural* claim (which nodes, what red string); a
> node's evidence is the *temporal/narrative* claim (its trajectories + marks).
> They unify into a **third panel**: `search | board | evidence`.
>
> - The **evidence panel** is a persistent, scrollable **full summary of every
>   pin on the board**, grouped by node. It renders the `GET /boards/:id` data we
>   already have — no new endpoint, no cap/honesty problem, trivially live.
> - **Evidence is always node-tied** (the `(node, time, aspect, payload)` triple).
>   So "add to a chart but not the graph" is disallowed *by construction* — every
>   chart is some node's justification. No hairball; every pixel accountable.
> - **Clicking a graph element scrolls/highlights** its section in the evidence
>   panel (focused node in the URL: `?focus=<svc>`, agent-drivable).
> - **Charts are deferred.** v1 is plain per-kind text summaries (anomaly delta,
>   log line, change annotation, span breakdown). Enrich with Recharts later *if*
>   there's time — incremental, optional.
> - **The one cross-node exception** (the demo's fleet-wide "two stories",
>   throughput up everywhere) is *not* node-tied, so it stays a **left-panel**
>   temporal render of a search result — corroboration, not a board artifact. That
>   removed the only thing that needed the histogram endpoint.
>
> The text below is the original chart-wall design, kept for the archetype
> reasoning (volume / value / events) we'll reuse when charts get enriched.

# weaver — the chart wall (temporal viz off the search results)

A way to take a search result (or a whole result set) and **drop a time-series
chart into a peer panel of the board** — the "chart wall." Not a fixed chart
pinned above the results; an *additive* surface you build up by clicking "add to
chart wall" on the things you want to watch over time.

This sits over the existing search layer (`search-api.md`) and the read-only
telemetry — no schema change. It's a sibling of the board's evidence pinning:
pinning makes a node+evidence card; adding-to-chart makes a temporal panel.

## The reframe: not one chart, three layers on one time axis

Every scope projects onto the same x-axis — the data window (~2h in the current
dataset). But "line graph" is the wrong single noun; forcing every scope into
volume-over-time hides what each one actually is. Three archetypes:

| archetype  | y-axis            | shape                     | scopes               |
|------------|-------------------|---------------------------|----------------------|
| **Volume** | count per bin     | one aggregate bar/area    | logs, traces, changes|
| **Value**  | the metric's unit | N opt-in lines            | metrics              |
| **Events** | none (marks)      | lollipops sized by z      | anomalies, (changes) |

One chart component, x-axis = "the window," each chart instance carries a layer
of one of these shapes. The same time axis the board's timeline spine will want,
so the component is reusable across both surfaces.

## The interaction: "add to chart wall"

The button's *granularity* differs by scope — this is the core decision:

- **services → no button.** A service has no timestamp; it's a catalog row, not
  a signal. Forcing a time viz here is the tell that "one chart for everything"
  is wrong. (Later maybe an inline per-card sparkline of the service's primary
  metric — a per-card viz, not a chart-wall panel. Out of scope for v1.)
- **metrics → per-card button.** Each metric result is its own series. "Add to
  chart wall" drops a **value** chart (or appends the series to an existing
  metric panel of the same metric — see overlay below).
- **logs / traces / changes → per-result-set button.** The chart is a property
  of the *query*, not any one row. One button at the result-set header → drops a
  **volume** chart of the whole matching set over the window.
- **anomalies → per-result-set button** (or per-card), dropping an **events**
  chart: lollipops at each `onsetTs`, height = z, color = up/down. Binning
  anomalies into a volume count loses the magnitude that's the whole point.

Each added chart is a panel on the wall. The wall is a **peer panel of the
board** — the workbench grows from two panes (search | board) to three
(search | board | chart wall), or the chart wall is a tab/region within the
board pane. Panels are dismissable, reorderable, and persist as part of the
investigation (same as pins).

## Per-archetype detail

### Volume (logs / traces / changes)

**The honesty problem — must solve before this is real.** The search results are
capped at `limit` (60) and ordered by magnitude/recency (`DurationMs DESC`,
`Ts DESC`). You **cannot** bin the returned page into a volume chart — that's a
histogram of "the top 60 slowest traces," not true volume, and the spike you
want to see is exactly the data the cap discarded.

→ **Built** (`HistogramDto`, `GET /api/search/histogram`). Mirrors the
`/api/search` filters per scope but counts every matching row bucketed by time —
*enumerate, computed live* (`analysis-architecture.md`). Shipped shape:

```
GET /api/search/histogram
    ?scope = logs | traces | changes
    &<same facet/filter params as /api/search>   // logs: q,level,template,subsystem…
    &from= &to=          window (default: global telemetry window, shared axis)
    &bucketMs=           explicit width; else auto-snapped to a nice step (~60/window)
-> { scope, window:{start,end}, bucketMs, total, buckets:[{ ts, startMs, count }] }
```

Dense buckets (zeros included) so gaps read honestly; `startMs` is epoch-ms for
the Recharts numeric x-axis, `ts` is the bucket-start ISO for tooltips. Auto
bucket snaps to the nearest of a 1s…1d ladder → ~2 min bars / 64 buckets over
the demo's ~2h window. Verified: logs `total` = 12,670 (full table, not the
capped 60); `level=error` → 1,368; empty changes table → all-zero buckets.
Per-scope filter parity with `/api/search`: traces expose route/status/minMs
only (no subsystem facet — mirrored); changes apply the subsystem facet to
`targetId` and let fleet-wide changes always pass.

### Value (metrics)

Each metric result is a series. Overlay is **unit-safe by construction**: the
metrics scope already filters to a single `metric` facet (default
`latency_p99`), so every row shares units → many rows can share one y-axis.
(Mixing `latency_p99` with `error_rate` would be a dual-axis mess; the
single-metric facet saves us — keep the overlay restricted to one metric per
panel.)

Two gaps to close:

1. **The metric search payload carries no points** — only `ShapeCode / Prose /
   Min / Max / Mean`. "Add to chart wall" must either fetch the series (the
   existing `/api/metrics?subjectId=&subjectKind=` endpoint returns points) or
   we widen the metric `SearchResultDto.payload` to include the raw points.
   Lean: fetch on add (keeps the search response lean).
2. **Which series are plotted is state.** Per our UI rule (URLs are the
   agent↔UI contract, `weaver-ui-rules`), plotted-series belongs in the URL so
   the agent can drive the same gesture — e.g. `&wall=metric:latency_p99:svcA,svcB`.
   Decide URL vs local `useState` before building (URL recommended).

### Events (anomalies)

Lollipops on the time axis: x = `onsetTs`, height = `z`, color/side =
`direction` (up/down), hover = `subjectId metric +deltaPct%`. Composition for
later: anomalies for `latency_p99` could drop onset markers onto a value panel
showing the same metric — v2.

## Where state lives

A chart-wall panel is a small descriptor — `{ archetype, scope, filters,
series?, bucketMs? }` — enough to re-fetch and re-render. Persist it the way
pins persist (part of the investigation / board). If we route it through the URL
(`&wall=...`), the agent can assemble a chart wall the same way a human clicks
the buttons.

## Charting library — Recharts

Locked in: **Recharts** (`recharts` ^3.8.1, installed). Declarative React
components, batteries-included axes/ticks/tooltips/legend, React 19 compatible.
Maps to the three archetypes:

- **value** → `<LineChart>` with N `<Line>`s (overlay; unit-safe because the
  metric facet pins one unit per panel)
- **volume** → `<BarChart>` with `<Bar>` over the histogram buckets
- **events** → `<ScatterChart>` (lollipops composed from a `<Scatter>` + a
  reference line / custom dot — the one shape that isn't a drop-in)

Shared `<XAxis type="number">` over the window (epoch-ms domain) ties all three
to one time axis. Bundle cost ~100KB gzipped, tree-shaken until the chart
component imports it.

## What this needs (build order)

1. ~~**Backend histogram endpoint** (`/api/search/histogram`)~~ — **done.**
   Honest volume for logs/traces/changes (see Volume section above).
2. **Chart component** — one component, x = window, three layer modes
   (volume bars / value lines / event lollipops). Volume mode reads
   `/api/search/histogram`; plot `buckets[].startMs` (x) vs `count` (y).
3. **"Add to chart wall" affordances** — per-card (metrics), per-result-set
   (logs/traces/changes/anomalies), none (services).
4. **Chart wall panel** — peer of the board; dismissable/reorderable panels;
   wall state in the URL.
5. **Metric series fetch-on-add** (or widen the metric payload).

## Open decisions

- Where the chart wall lives — the workbench is already three panes
  (search | board | evidence). A fourth pane is too much; likelier a
  **region/tab inside the board or evidence pane**, or a drawer.
- Plotted-series / wall state in the **URL** (agent-drivable) vs local state?
- Auto-bucket sizing vs a user control on volume charts?
