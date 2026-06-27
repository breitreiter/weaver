# weaver — graph redesign (passive reflection, edges by name)

A build-plan for the middle-pane graph and the edge UX. The throughline: **the
graph stops being an instrument and becomes a reflection.** It renders board
state and never originates an edit. Every place the human used to act
*spatially* on the graph, they now act *by id* in the drawer — which is the
co-researcher symmetry the rest of the project already runs on (`weaver link
x y --as z` is the human gesture too, now). Coordinate with `graph-model.md`
(ontology) and `board-build.md` (the board surface); this is the "graph visual"
piece that doc flagged as overlapping the panel redesign.

Driven by two demo scars:
1. **User-drawn edges didn't land.** Dragging node→node to assert a hypothesis
   read as fiddly and unclear.
2. **The graph panel felt tiny — "keep zooming out to see the nodes."** A
   fit-to-viewport problem made worse by a layered layout that spreads.

## The principle

The graph has exactly **two affordances**, both passive:
- **Click a dot → jump to evidence** (the existing `?focus=` flow — keep it).
- **Receive highlight** — from drawer hover, and from clicking a drawer edge.

It **originates** nothing: no drawing, no node drag, no edge-delete-on-graph.
Creation and deletion of everything live in the drawer. The graph reflects what
the board holds: dependency edges (real topology) + red strings (hypotheses,
from either hand). This is consistent with `graph-model.md`'s atemporal, fixed-
geometry stance — a passive graph is what makes the future time-cursor ("lighting
over a stationary map") possible.

## Current state (what we're changing)

- **`web/src/Board.tsx`** — React Flow (`@xyflow/react`). `nodesConnectable`
  on (`:147`); drag → `onConnect` → `RelationshipModal` (`:71`, `:110`);
  `onNodeClick` → `onFocus` (`:97`); edge click → `EdgeToolbar` delete
  (`:75`, `:107`); `ServiceNode` renders a panel with evidence **chips**
  (`:157`); `buildGraph`/`layout` compute a deterministic top→bottom layered
  DAG (`:284`, `:330`).
- **`web/src/Evidence.tsx`** — the drawer. Services are section headers; edges
  hang as "red string" cards **nested under their source service** (`ev-strings`,
  `:119`), outgoing-only, keyed by `e.from` in `groupByService` (`:147`).
  `onDeleteEdge` already lives here (`:129`) — **edge deletion is already off
  the graph.** Click a service → `onFocus` (`:65`).
- **`web/src/Workbench.tsx`** — owns `focus` as a URL param (`?focus=`,
  `:62`), polls the board every 2.5s, passes one shared copy to both panels.

The key enabling fact: **selection is URL/prop-driven, not graph-internal.**
Removing the graph's `onConnect`/`onEdgeClick`/`onNodeClick` breaks nothing
downstream — the drawer already drives focus and already deletes edges.

## Target

### 1. Drop React Flow → hand-rolled SVG

Once the graph is passive and edges aren't drawn on it, React Flow (an
interactive node-editor framework) earns nothing — its handles, drag, and
connection machinery are all features we're deleting, and we'd fight its
defaults on the sparse look and on sizing. Replace it with a hand-rolled SVG,
the same no-charting-lib idiom already used by `MetricSparkline`
(`Evidence.tsx:233`) and TraceMini.

- **Reuse `layout()` as-is** (`Board.tsx:330`) — it already produces
  deterministic depth-layered positions and stays within the no-force-directed
  rule. It returns a `Map<id, {x,y}>`; feed those straight into `<circle>` +
  `<line>` placement.
- **Compute the `viewBox` from node bounds** so the graph *always* fits its
  panel by construction. This is the structural fix for the "zoom out" scar —
  no `fitView` call to remember to fire on data change; the content defines the
  frame. (Optional: keep pan/zoom as a CSS transform on a `<g>` if we want it;
  it's no longer load-bearing.)
- Edges become `<line>`/`<path>`: hairline grey for `dependency`/`route`,
  red string (the `var(--up)` styling from `Board.tsx:319`) for the rest.
  `isRedString` (`Board.tsx:31`) and the dedupe in `buildGraph` (`:288`) port
  over unchanged.

Removes the `@xyflow/react` dependency and `dist/style.css`.

### 2. Thin the node; move chips to the drawer

- Node becomes a **dot** (small `<circle>`), not a panel. The evidence **chips**
  that hang on the node today (`ServiceNode`, `Board.tsx:165`) move into the
  drawer — they're already there in fuller form as the per-service evidence
  cards, so this is consolidation, not loss.
- **Labels:** always-on for *every* node — board/pinned bright, ambient ones
  **dimmed** (typographically quiet), never hover-to-reveal. Recede by hierarchy,
  not by concealment: persistent-or-quiet, never hidden (house rule — hover may
  *emphasize*, never *reveal*). Thinning the node and richening the drawer are the
  same move.

### 3. Hover-highlight (drawer → graph) — the keystone (and the craft-heavy piece)

This is what lets the graph go passive without going *dead*: it replaces
graph-origin navigation with drawer-origin navigation. It is also the
**highest-craft effect in the UI** — rolling the mouse down the evidence panel
and watching the relevant subgraph light up is the moment that should feel
magical. Expect reps; "solid" is defined below so the reps have a goalpost.

**Mechanism.** A transient `hoveredId` (+ its kind) in `Workbench`, passed to
`Board`. Ephemeral UI state — *not* the URL, *not* the polled board (routing
through `?focus=` thrashes history and fights the 2.5s poll). Plain React state,
prop-drilled (no store, by rule). Hover state must **survive a board poll without
flashing** — keep the graph's node/edge identity memo-stable so a poll re-render
doesn't remount and kill the in-flight CSS transition.

**What lights — a small, consistent highlight vocabulary** (consistency is a
principle, `design.md` — the user *learns to read the lights*). The lit subgraph
is **kind-aware**, not always one node:
- **anomaly / metric / log / change** → its service node.
- **trace** → the whole **path** it traversed — nodes *and* the edges along the
  route. This is `graph-model.md`'s **path-lens**, and it's the showpiece case:
  roll over a trace, watch its route through the system illuminate. **Build the
  mechanism path-shaped from day one** (a *set* of node-ids + edge-ids to light),
  never node-at-a-time with paths retrofitted — that retrofit is where this
  effect dies.
- **red-string edge** → both endpoints + the string (shared with #4's click case).
- *(later, optional)* a service → its dependency neighborhood (blast-radius cone).

**How it reads — spotlight, not paint.** Magical ≈ *dim the rest so the subgraph
emerges from a quieted field*, not "turn the node yellow." Honor the house rule:
dimming is **recession, not concealment** — the rest stays legible, just
recessive (persistent-or-quiet, never hidden). Layer a few restrained cues over a
single colour flip — dim ambient labels promote dim→bright (ties to #2's label
decision), stroke brightens, maybe a hair more weight. Rationed, per `design.md`.

**Where the reps go (what "solid" means):**
- **CSS-driven, not React-re-render-driven.** Set `hoveredId` once; drive the
  lit/dimmed states in CSS (`[data-lit] .node`, attr selectors by id) so the
  compositor handles every mousemove, not React. A whole-tree re-render per hover
  janks a large graph.
- **Compositor-friendly transitions only** — `opacity`, `stroke`/`color`; avoid
  animating width/layout. A `filter: drop-shadow` glow is pretty but a perf trap
  across many edges — test before committing to it.
- **Asymmetric easing** — fast in (~120–150ms), slower out (~250–300ms). Instant
  on/off feels twitchy and cheap.
- **Anti-flicker on rapid traversal** — sweeping down the card list must read as a
  smooth hand-off between subgraphs, not a strobe. A small enter-delay or a
  crossfade. **This is the #1 jank source and will eat the most reps.**

### 4. Edges as formal objects, created by naming

Drag-to-connect is gone. The human creates a relationship the same way the agent
does: name x, name y, describe z.

- **A relationships section in the drawer**, peer to the pins — not nested under
  the source service as today (`Evidence.tsx:119`). An edge reads as a *thing*
  ("storefront-bff → orders-db · explains the p99 spike"), with its
  `drawnBy: human|agent` provenance shown (the field already rides on every
  edge). Keep `onDeleteEdge` here.
- **A "+ relationship" dialog**, reusing `RelationshipModal`
  (`Board.tsx:202`) decoupled from the drag trigger:
  - Pick **x** and **y** from a picker **constrained to board members** (an edge
    to a non-pinned service has nowhere to render and no evidence beside it; this
    keeps graph and drawer in lockstep).
  - **Keep the grounding step.** The modal already queries `/api/relationships`
    and leads with the real facts between the two (`Board.tsx:211`) before
    offering a freeform assertion — the UI mirror of `weaver relationships a b`,
    and the one bit of friction that stops a plausible coincidence from being
    drawn. Per CLAUDE.md: "ground a proposed link before you draw it."
  - Then describe z. Submit → `api.link(..., drawnBy: 'human')` (`Board.tsx:84`),
    unchanged.
- **Click a drawer edge → highlight both endpoints** on the graph (+ the string
  between them). Natural pair to #3; same `hoveredId`-style mechanism, but on
  click rather than hover, against a pair of ids.

### 5. Remove graph interactivity (last)

Once #3 and #4 exist, strip the graph's edit surface: drop `onConnect`,
`onEdgeClick`, the `EdgeToolbar`, `RelationshipModal`'s drag trigger, and
`nodesConnectable`. Keep `onNodeClick → onFocus` (the one surviving affordance).
This is the *removal*, safe only after its replacements are in.

## Build order

The order is a dependency chain — each step de-risks the next:

1. **Hover-highlight state** (#3). Keystone; makes passivity safe. Small,
   additive, breaks nothing.
2. **Thin node + chips to drawer** (#2). Visual; the drawer already holds the
   evidence.
3. **Relationships section + "+ relationship" dialog** (#4). The new human
   creation path; reuses the existing modal + API.
4. **Drop React Flow → SVG** (#1). The bigger swap, but `layout()` and the edge
   styling port directly; the graph is now simple enough to own.
5. **Remove graph edit surface** (#5). Pure deletion, last.

(2 and 4 can merge — the SVG node *is* the thinned dot. Listed apart so the chip
migration can land first against React Flow if we want a smaller first diff.)

## Settled / open

| Piece | State |
|---|---|
| Graph passive: click→focus + receive highlight only | ✅ settled (this plan) |
| Kill user-drawn (drag) edges; human creates by id in drawer | ✅ settled |
| Red strings survive; agent-drawn is normal, human via dialog | ✅ settled |
| Edges = formal drawer objects, own section, `drawnBy` shown | ✅ settled |
| Endpoint picker constrained to board members | ✅ settled |
| Keep `/api/relationships` grounding step in the dialog | ✅ settled |
| Labels: always-on; board bright, ambient dimmed (never hover-to-reveal) | ✅ settled |
| Drop React Flow for hand-rolled SVG + computed viewBox | ✅ settled (direction) |
| Hover-highlight = transient state, not URL | ✅ settled |
| Highlight vocabulary (kind→subgraph; trace = path-lens) | ✅ settled (shape) |
| Highlight motion spec (easing, anti-flicker, perf) | ⬜ open — rep-heavy; "solid" defined in §3 |
| Pan/zoom kept or dropped once viewBox auto-fits | ⬜ open — decide in build |
| Edge-evidence rendering (per `graph-model.md`) | ⬜ open — separate; not this plan |

The line we keep: the graph enumerates and reflects; it never concludes and never
edits. Causal claims exist only as labeled red string, created by name — by
either hand, the same way.
