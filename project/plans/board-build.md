# weaver — board build plan

The board (right pane / wall of red string), built on **React Flow**
(`@xyflow/react`, now a dependency). Manual placement, **never force-directed**.
Co-built by human + agent via the shared board id in the URL. Peer panel to the
chart wall (`chart-wall.md`). Interaction model is in `sensemaking-pivot.md`.

## Why React Flow

Custom nodes (the node control), Handles (attach points), drag-to-connect
(`onConnect`), styled/animated edges, edge mouse events + `EdgeLabelRenderer`
(rollover + interactive labels), pan/zoom/selection — all native.

## Layout — auto-tidy, not manual (for now)

No manual drag-to-rearrange yet. Instead:

- **Auto-tidy on add** — every pin re-runs a layout pass that makes a solid
  effort at a readable arrangement (no overlaps, minimal crossings).
- **A "tidy" button** re-runs it on demand ("this is messy — fix it").
- Layout is **deterministic and structured, never force-directed** — a layered
  arrangement by dependency direction (left→right cause-ish→effect, the old
  layered-view instinct over the curated board subset).
- Positions are **computed, not stored** — the backend keeps no x/y.
- Manual reordering is a real future case, **deliberately deferred**.

Implementation: dagre / elkjs (standard React Flow auto-layout companions —
layered, not force) or a hand-rolled layered pass (viable at curated-board node
counts).

## Prerequisite — board model refactor (backend, NOT Figma-blocked) — DONE 2026-06-10

✅ The v1 `BoardItem{kind, ref, evidence}` conflation is gone — store, API, CLI,
and both panels now run the node+evidence model below (nodes keyed by serviceId;
edges connect services; `kind` = evidence kind only; "node" retired from UI copy).
What shipped vs. the original sketch: positions are *not* stored (layout computed,
as planned); a pinned trace ensures all participant nodes with evidence on the
subject; node/evidence `DELETE` endpoints were deferred (no UI consumes them yet).
Original spec retained below for reference.

The v1 store conflates a pin as `BoardItem{kind, ref, evidence}`. Refactor to the
node+evidence model (`sensemaking-pivot.md`):

- **BoardNode** `{ id, boardId, serviceId, label? }` — no stored position; the
  board auto-tidies, so layout is computed, never persisted.
- **Evidence** `{ id, boardId, nodeId, kind, aspect, at, payload }`
- **BoardEdge** `{ id, boardId, from, to, kind, label?, drawnBy }` (exists; add a
  `crossedOut` flag — see below)
- Endpoints: `POST /boards/:id/pin` (ensure a node for `serviceId`, append
  evidence), `POST /boards/:id/edges`, `GET /boards/:id` (nodes-with-evidence +
  edges), `DELETE` for node/edge/evidence. No position endpoint — layout is
  computed (see Layout).
- Migrate the `pin`/`link` CLI verbs and the search-result `pin {nodeIds,
  evidence}` mapping → ensure-node + append-evidence. A pinned **trace** ensures
  *all* participant nodes + the dependency edges among them (a slice).

## Phases

1. **Backend refactor** (above). Unblocks everything below.
2. **React Flow scaffold** — render a board's nodes + edges from `GET /boards/:id`
   with a *placeholder* custom node; pan/zoom; read-only first.
3. **Interactions** (`sensemaking-pivot.md`):
   - add → a pin lands a node (server-side); the board **auto-tidies** to place
     it (no manual placement). A "tidy" button re-runs the layout on demand.
   - drag-to-connect → `onConnect` → relationship modal → `POST` edge.
   - hover node → preview **factual** candidate linkages (dependency edges from
     `/api/graph` between on-board nodes; faint). **Never causal.**
4. **Edges** — dependency (neutral, thin, solid) vs red string (causal/temporal/
   custom: bold, colored, curved; particle-flow option). Edge labels + rollover
   via `onEdgeMouseEnter` + `EdgeLabelRenderer` → show relationship + provenance
   (`drawnBy`) + controls (edit / delete / **cross out**).
   - **Cross-out**: a `crossedOut` edge state (struck-through styling). This is
     the demo's payoff — the operator crosses out the red string to the v2.4.1
     deploy after the out-of-band exoneration.
5. **Node-evidence drawer** — click a node → `GET /api/nodes/:id/evidence` dossier
   (signal trajectories, log groups, deploys-on-this-node, trace participation);
   pin slices from it.
6. **Live sync** — poll `GET /boards/:id` (~2–3s) so the agent's pins/edges appear
   live: the "watch Claude build the wall" moment. (SSE/push is a later upgrade.)
7. **Figma node visual** — replace the placeholder custom node with the designed
   control (label in/out, kind icon, attach points). **The only Figma-gated piece.**

## Figma-gated vs not

- **Not blocked:** phases 1–6 — all buildable behind a placeholder node.
- **Blocked:** the final node *visual* (phase 7). Build behind a placeholder;
  swap in when the layout lands.

## Open questions

- Live-sync cadence (poll interval) vs SSE later.
- Tidy layout: dagre / elkjs (layered, not force) vs a hand-rolled layered pass.
  Attach points follow the layout (left/right Handles for a left→right tidy).
- Edge overlap: offset/curve parallel edges between the same pair.
