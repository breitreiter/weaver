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

### Charts — agent-authored SQL charts (superseded the chart wall)

The human "add to chart wall" builder (`chart-wall.md`) was **superseded by
agent-authored SQL charts** (`agent-sql-charts.md`): Claude writes read-only SQL,
snapshots the result, pins it as `chart` evidence — Recharts in the web, a prose
table in the CLI. Charting decisions locked in `board-time-windows.md`.

Built (2026-07):

- [x] **Hardened read-only SQL sandbox + `/api/charts/exec`** — single-SELECT,
      `Mode=ReadOnly`, native progress-handler wall-clock cancel, row cap (24 tests).
- [x] **`chart` evidence kind + `ch:` typed id**, end to end (entity → DTO →
      resolve → `IsTypedId`). A chart is *authored*, not resolved from telemetry.
- [x] **`weaver chart` CLI verb** — prose table + the `ch:` id; `--pin <service>`
      snapshots it onto the board.
- [x] **Recharts renderer** for pinned chart evidence + `MetricSparkline` re-skinned
      onto Recharts (one styling surface); sparkline hover tooltip.
- [x] **Honest result counts in the UI** — "top 60 by {duration|recency|magnitude|
      time|name} — more exist" when capped, else a plain count (`Workbench.tsx`).
- [x] **Backend histogram endpoint** (`/api/search/histogram`) — bucketed counts
      over the *full* matching set (not the capped page); `HistogramDto`. **Backs the
      count-bar shape.**

Charting decisions (locked — `board-time-windows.md` §2a/2b):

- **Every chart is time-x.** No categorical x-axis; categorical data becomes
  *series*, not the axis.
- **Canonical render set: `line` + `count-bar`** (PerfStack-derived). `area` = a
  `line` fill. **`scatter` is CUT** (numeric×numeric, no time axis). Claude picks
  the render spec from query shape + intent — no chart-type menu (the design stance).
- **Deferred shapes:** `state-line` (alert/status — needs a live-derived state
  series, since weaver stores no status), `stacked`/`grouped bar`, `heat-line`.

Follow-ups:

- [x] **Retired `scatter`** to match the time-x-only decision — dropped from the API
      type guard (`line|bar|area` only), the CLI help, and the web renderer (legacy
      scatter snapshots fall through to the line renderer). `bar` is now framed as
      count-bar (counts per time bucket); hard time-x enforcement arrives with windows.
- [ ] **Board time windows** — a tracked set of named `t:` windows; charts lock to
      the selected one (re-derive via the Grafana `$__timeFilter`/`$__timeGroup`
      idiom); the window is the shared time domain that unlocks synchronized hover.
      **Design complete, not built** — see `board-time-windows.md`.

## Knowledge snippets — the bag of factoids (planned, not built)

A new stored data source: blended chunks of docs / runbooks / prior-incident
write-ups / prior board text, each attached to one service, FTS-searchable,
timeless (no timestamp → exempt from every window filter). Chunks are smol
(one concept, 1–3 ¶) with `doc_ref`/`seq` lineage — a coherent doc is a chain
of chunks, walked via a keep-reading affordance. Titles must self-situate
(hand-made Contextual Retrieval — see `demo-vs-production.md`). New
`knowledge` search scope + `kn:` typed id + a dossier section. The hard part
is authoring discipline (no answer labels; decoys, staleness, and the odd
context-poor chunk are the texture). Plan:
`project/plans/knowledge-snippets.md`.

- [x] datagen: `knowledge_snippets` table + FTS5 + scenario-spec passthrough.
      **Built 2026-07-04** (branch `knowledge-snippets`). Starter snippet set laid
      into `topology-flashsale.yaml` — deliberately small + mostly-boring, one
      clean demo-beat snippet (pool-sizing rationale). ⚠ The full load-bearing +
      decoy authoring is a flagged follow-up (see below).
- [x] Core/Contracts/API: entity + guards, DTO, `knowledge` scope, resolve,
      facets, result builder (`kn:` id, evidence kind `knowledge`, `At = null`).
- [x] Dossier + CLI: `NodeEvidenceDto.knowledge` (un-windowed) + `evidence`
      section + `weaver snippet <kn:id>` drill-in with keep-reading affordance.
- [x] Web: `knowledge` scope + `--source` facet + result card + `knowledge`
      evidence-kind card.
- [x] **Author the real flash-sale snippet set** — **Built 2026-07-04.** 34
      snippets covering all 28 services. Tier A hand-authored + ground-truth-
      coupled (payments-db pool runbook, INC-2411 rhyme, the promo-api "growth
      shipped something" decoy, payments-api release-hygiene exoneration); Tier B
      per-service background drafted by free local models (imp-qchat/imp-qcoder
      via minrouter, one profile per team for voice), ground-truth-isolated and
      reviewed. The out-of-band "was a sale running?" fact is held OUT of the bag
      by design. Leak-grepped clean; verified live (search/FTS/dossier/decoys).
- [ ] End-to-end verify against a restarted API + regenerated db (Joseph runs the
      servers) — CLI `search knowledge` / `snippet` / `evidence`, web scope + pin.

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
- [ ] **Correlate verbs honour `--metric`.** `anomalies` / `timeline` currently
      ignore `--metric` (only `search <scope>` filters) — so metric-scoped onset
      questions must route through `search anomalies --metric`. Inclined to make
      them filter (esp. `timeline --metric error_rate` — "error-rate onset order"
      is the real incident question), but not fighting it now. Locus:
      `AnalysisQuery` reads only `split`/`z`/`min-pct` (`src/Weaver.Cli/Program.cs`
      L895-902); `SearchQuery` (L459) is the one that reads `--metric`. Distinct
      from the silent-swallow bug (`project/bugs/cli-swallows-unknown-flags.md`),
      which should land first — once unknown flags error, this decides whether
      `--metric` on a correlate verb filters vs. errors. Surfaced calibrating the
      eval ladder (`project/plans/agent-evals.md`, rung 6).
- [ ] **Bump SQLite — good-citizen dependency hygiene.** NU1903 high-severity
      advisory on `SQLitePCLRaw.lib.e_sqlite3 2.1.11`, transitive via EF Core
      10.0.8 (surfaced building the SQL sandbox, `project/plans/agent-sql-charts.md`).
      Still a demo/PoC so not urgent, but no reason to ship a known advisory —
      follow `project/plans/sqlite-vulnerability-remediation.md`.
