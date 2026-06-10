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
- [ ] **Board model refactor**: a pin = **node + layered evidence** (interest in
      node x + time t + aspect e), not the v1 item-with-kind. Touches the backend
      (BoardNode + Evidence), the pin verbs/UI, and the board render. Do it with
      the rework. See `project/plans/sensemaking-pivot.md`.
- [ ] **Build the search API** — facets / structured search / node-evidence
      endpoints, specced in `project/plans/search-api.md`. Query-layer only over
      the read-only telemetry; no schema change. Each search result carries its
      `pin` (node + evidence), so it drops straight onto the refactored board.

## Design to lock first

- [ ] **Query language grammar** (the left fire-axe) — biggest unknown, *not*
      blocked on Figma. Unify selector grammar + log FTS + analysis verbs.
- [ ] Board data model — drafted in the plan; confirm pinnable granularity +
      edge kinds (dependency vs red-string).

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
