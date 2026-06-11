# weaver — working list

Running list. UX review + pivot on 2026-06-09.

## Pivot: the sensemaking UI (search + board)

See `project/plans/sensemaking-pivot.md`. Two panels — forage (search) left,
sensemake (wall of red string) right. **This replaces the auto-neighborhood
graph** (which read as a hairball — now impossible by construction). The board
is co-built by human *and* Claude.

- [x] **1. Board backend** — writable store + create/pin/link/get. Done.
- [x] **2. CLI lift** — `board` / `pin` / `link` verbs. Done.
- [x] **3. Left panel v1** — command box + pinnable rows. Shipped — but needs a
      real rework (below) before it's "legit search".
- [ ] **4. Right panel** — render the board: pinned nodes + dependency edges +
      red-string edges + evidence; manual placement. *Blocked on Figma.*

### Left panel v2 — make it feel like legit search (rework)

- [ ] **Take ≥50–60% of the screen.** The board is a secondary *evidence anchor*,
      not the main attraction — search is where attention lives most of the time.
      Currently search is a 440px sidebar; flip the weighting.
- [ ] **Structured query**, not just a command box — facets / filters (subsystem,
      kind, signal, level, route, window, threshold) alongside free text.
- [ ] **Rich, type-aware result cards** — render each result for its kind, not a
      flat one-line row.
- [ ] **Every result states its data type** (service / edge / anomaly / log /
      trace / metric).
- [ ] **Traces are a distinct, sparse, rich type** — not a node. A trace result is
      a span breakdown (where `self_ms` went), so it needs its own card shape.
      This whole result-typing problem "needs a lot of love."
- [x] **Board model refactor**: a pin = **node + layered evidence** (interest in
      node x + time t + aspect e), not the v1 item-with-kind. Done 2026-06-10 —
      backend (`BoardNode` + `Evidence`, keyed by serviceId), `/pin` endpoint, CLI
      (`pin <service> [--as K --aspect A]`), and both panels. "node" retired from
      UI copy → "service". See `project/bugs/graph-evidence-ux.md` (#1/#2/#4).
- [ ] **Build the search API** — facets / structured search / node-evidence
      endpoints, specced in `project/plans/search-api.md`. Query-layer only over
      the read-only telemetry; no schema change. Each search result carries its
      `pin` (node + evidence), so it drops straight onto the refactored board.

### Chart wall — temporal viz off the search results

See `project/plans/chart-wall.md`. An "add to chart wall" button drops a
time-series chart into a **peer panel of the board** (not a fixed chart above
results). Granularity differs by scope: **metrics** per-card, **logs / traces /
changes** per-result-set, **anomalies** per-result-set (event lollipops),
**services** no button (no timestamp). One chart component, three layer modes
(volume bars / value lines / event lollipops) on the shared window x-axis.

- [x] **Honest result counts in the UI** — results are "top 60 sorted by
      duration / recency / magnitude," *not* "there happen to be exactly 60."
      Header now shows "top 60 by {duration|recency|magnitude|time|name} — more
      exist" when the cap is hit, and a plain count otherwise (`Workbench.tsx`,
      labels mirror the backend ORDER BY per scope). Also the reason the chart
      wall needs a real histogram endpoint rather than binning the capped page.
- [x] **Backend histogram endpoint** (`/api/search/histogram`) — bucketed counts
      over the *full* matching set (not the capped page), same filters as
      `/api/search` (logs|traces|changes). Auto-snaps to a nice bucket (~60/window)
      or takes `bucketMs`; returns `{scope, window, bucketMs, total, buckets[]}`
      with epoch-ms + ISO per bucket. `HistogramDto` in Contracts. Verified
      against the dataset (logs total = 12,670, not capped). Unblocks volume charts.
- [ ] **Chart component** — x = window, three layer modes.
- [ ] **"Add to chart wall" affordances** + the **chart wall panel** (peer of
      the board; dismissable/reorderable). Wall state in the URL so the agent
      can drive it (`weaver-ui-rules`).
- [ ] **Metric series on add** — metric search payload carries no points; fetch
      via `/api/metrics` on add, or widen the payload.

## Design to lock first

- [ ] **Query language grammar** (the left fire-axe) — biggest unknown, *not*
      blocked on Figma. Unify selector grammar + log FTS + analysis verbs.
- [ ] Board data model — drafted in the plan; confirm pinnable granularity +
      edge kinds (dependency vs red-string).
- [ ] **Graph ontology locked** in `project/plans/graph-model.md` — 2 primitives
      (service node / relationship edge), all data is trans-time evidence on one or
      the other, traces are a path-lens, time selects+labels but never positions.
      Open: edge-evidence schema (`BoardEdge`), trace render (decompose vs lens).
      Read this before touching the graph/panel surface.

## Polish (after the view direction lands)

- [ ] Material icons per node kind; reuse the label row for something useful.
- [ ] Clickable findings/chips (depends-on / depended-on-by → navigate).
- [ ] **Coherent "vs base" periods** — human-recognizable windows, not
      "first 30%". Couples to the data agent's scenario timeline.
- [ ] Clear back-to-home navigation.
- [ ] Particle edges (subtle, not tacky) — replace directional arrows.

## Deferred / blocked

- [ ] Home page spruce-up — dull; deferred.
- [ ] Board / node layout — Joseph noodling in Figma. *Blocked on his direction.*
