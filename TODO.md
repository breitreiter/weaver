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

- [ ] **Balance the three panels to equal thirds (1:1:1).** Supersedes the
      original "search ≥50–60%" call: the board (red-string wall) and the evidence
      narrative have matured into full co-researcher surfaces that now carry as
      much of the session as search. Currently weighted 1.5 : 1.1 : 1
      (`App.css:7-12`). See `project/plans/ux-cleanup.md` #5 (Phase 0).
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

## CLI ↔ UI alignment (the co-researcher lift)

See `project/plans/cli-co-researcher.md` (2026-06-10 review: board contract is
solid; foraging surfaces diverged, CLI is append-only, ids unprintable).
**Built 2026-06-10** — phases 1–7 landed; tested end-to-end against the
flash-sale dataset. Remaining polish noted below.

- [x] **1. `weaver search` + `facets`** — forage parity with the UI's six
      scopes/facets/caps/sort; every row prints its typed id. (Old per-type
      verbs kept as-is alongside, not yet folded into aliases.)
- [x] **2. Typed result ids as shared currency** — `pin <typed-id>` resolves
      via `GET /api/search/resolve` (shared builders → byte-identical to the UI
      pin); typed id shown + copyable on each UI result card.
- [x] **3. Board readback with handles** — edge + evidence ids in `board show`;
      evidence `summary` computed server-side, consumed by CLI *and* UI (the
      `Evidence.tsx` summarize() copy retired).
- [x] **4. Grounding verbs** — `relationships <a> <b>`, `evidence <svc>`,
      `board review` (facts under each red string; flags ungrounded; enumerate only).
- [x] **5. Symmetric editing** — `unpin <evidence-id>` / `unpin <svc> --all`.
      Also: `link` now ensures both endpoints land on the wall (no dangling edge
      the UI would drop).
- [x] **6. Grace layer** — pasted-URL board args, `crossout <a b>` by pair,
      did-you-mean service resolution, forgiving `--from 14:30` time.
- [x] **7. Docs** — `cli.md` verb tables + coordination section → board model;
      `agent-workflow.md` step 7; new `agent-briefing.md` (co-researcher manner).

  Polish still open:
  - [ ] Per-verb help (`weaver <verb> -h`) + teaching errors on bad facet values.
  - [ ] Search state in the URL so `search --from-url` reproduces the human's query.
  - [ ] Fold the per-type verbs (`anomalies`/`logs`/`traces`) into `search` aliases.
  - [ ] Optional `pinnedBy` on evidence so the agent knows whose pin it is.

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

## House style — a distinct visual identity

Answers to `project/plans/design.md` (the design principles: who the user is,
the Pirolli & Card cost-structure, density-warranted, visual-search, one-finding-
one-identity, honest-over-clean). Identity is the *expression* of those; when a
brand instinct and a principle disagree, the principle wins.

The UI is currently vibe-coded bland — functional, no point of view. Needs a
deliberate house style: typography, colour system (beyond the current kind
palette), spacing/density, component vocabulary, the dark-instrument feel a
mid-incident tool should have. Cross-cutting, not a one-screen tweak.

- [ ] **Define the house style** — a small design language doc + token set, then
      apply it across search / board / evidence. Pairs naturally with the graph
      redesign (`project/plans/graph-redesign.md`): the hand-rolled SVG graph is a
      blank canvas to set the aesthetic on, rather than inheriting React Flow's
      defaults. *Likely Figma-led — coordinate with Joseph's noodling.*

- [ ] **Semantic token layer (not appearance-named).** A library's real value is
      *enforced grammar* — one token = one affordance everywhere (mui-blue =
      "clickable"). We're DIY (plain React + bespoke CSS, no component lib), so we
      own that consistency. Today's tokens (`index.css`) are appearance-named
      (`--up`/`--down`/`--accent`) and have drifted: **`--up` (a red) now means
      error AND "metric rose" AND danger/delete AND "red-string hypothesis"** —
      four concepts, one colour. Re-name tokens by *meaning* (`--interactive`,
      `--danger`, `--hypothesis`, `--metric-rise/-fall`, the evidence-kind palette
      — currently raw hex repeated inline, tokenized nowhere) and have components
      reference only those. Decide first: semantic-token layer over bespoke CSS
      (lean) vs. headless primitives (Radix-style).

- [ ] **Mechanical consistency pass** — sweep every colour/affordance usage to one
      semantic token. Acceptance is grep-lintable, not vibes: zero raw hex in
      component CSS (`grep -rE '#[0-9a-fA-F]{3,8}' web/src/*.css`), no bare
      appearance-token used in a component, every clickable thing shares the
      interactive signal. Wire the greps as a build check so drift fails CI.

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
- [ ] **Bump SQLite — good-citizen dependency hygiene.** NU1903 high-severity
      advisory on `SQLitePCLRaw.lib.e_sqlite3 2.1.11`, transitive via EF Core
      10.0.8 (surfaced building the SQL sandbox, `project/plans/agent-sql-charts.md`).
      Still a demo/PoC so not urgent, but no reason to ship a known advisory —
      bump when convenient.
