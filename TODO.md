# weaver — working list

Running list. UX review + pivot on 2026-06-09.

## Pivot: the sensemaking UI (search + board)

See `project/plans/sensemaking-pivot.md`. Two panels — forage (search) left,
sensemake (wall of red string) right. **This replaces the auto-neighborhood
graph** (which read as a hairball — now impossible by construction). The board
is co-built by human *and* Claude.

- [ ] **1. Board backend** — writable board store (separate from the read-only
      telemetry DB) + API: create board / pin item / add edge / get board.
- [ ] **2. CLI lift** — `pin` / `link` / `board` verbs so the agent forages →
      pins → draws red string → hands over a URL.
- [ ] **3. Left panel** — search/query GUI over the primitives; pinnable results.
- [ ] **4. Right panel** — render the board: pinned nodes + dependency edges +
      red-string edges + evidence; manual placement.

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
