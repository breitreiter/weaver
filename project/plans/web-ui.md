# weaver — web UI

Starting doc. The single-screen React app that renders curated Views.
This captures what we know and the open questions; layout/visual detail
is deliberately left to iterate on later.

## Governing rules (from the constitution)

- **URLs are the contract.** The UI is driven by URLs the agent/CLI
  emits — clickable from the console, pastable to Slack, storable in a
  ticket. The UI is, first, a renderer of a View addressed by URL.
- **Pixels are earned. Never the hairball. Never force-directed.** The UI
  renders only a *selected* subgraph. There is no "show everything" view.

## Scope & routes

- **Vite + React.** One screen.
- `/view/:id` — the workhorse. Fetches a resolved View from
  `GET /api/views/:id` and renders it: the selected subgraph, decorated,
  with the grounding evidence available to drill.
- `/` — an **entry point, not a graph**: a selector/search box (type a
  selection → mint a View) plus recent Views. The graph only ever appears
  once a selection exists. (No landing hairball — that would violate the
  rules on the very first screen.)

## Rendering model = the three axes

The UI is a direct rendering of the view model (`view-model.md`):

- **Selection** → which nodes & edges are drawn.
- **Projection** → **differential by default** (Δ vs a chosen base, not
  absolute value — see `view-model.md`, "RCA is differential"). Visual
  channels:

  | channel | encodes (example) |
  |---|---|
  | node fill | **Δ vs base** for the chosen signal, red/blue diverging (red = worse) |
  | node border | anomaly / **new since base** |
  | node size | throughput |
  | node badge | role: suspect / blast-radius / healthy; **+ new / vanished** |
  | edge thickness | call volume |
  | edge color | **Δ** edge error/latency; **edges new since base highlighted** |

  Absolute value is a toggle, not the default. The canvas is a still
  frame of *what changed*.

- **Grounding** → the **evidence panel**: click a node/edge to open its
  metrics/traces/logs (the reservoir) — **base & subject overlaid** so the
  trajectory change is visible — plus provenance: *why am I here?*
  (selection) and *why this color?* (the delta and its base).
- **Comparison base** → a first-class, always-visible control (a named
  picker: pre-deploy / T-1h / same-time-yesterday / last-known-good). The
  view always announces its base. This is the primary temporal control —
  there is no scrubber.

## Layout (the presentational axis)

No force-directed, ever. Layout is deterministic and *meaningful*.
Default proposal: **layered by dependency depth from the user-facing
entry** — user-facing/upstream on one side, dependencies on the other,
so the eye reads cause-and-effect direction. Alternative groupings:
subsystem, or request-path order for a trace-centric view. Because
selections are small (earn-your-pixels), we don't need a heavy engine.

## Interaction

Each manipulation is an axis perturbation that updates the URL (so every
state is shareable):

- re-select — expand/contract, jump to blast radius, follow a path
- re-project — recolor by a different signal (still a delta)
- choose the **comparison base** — pre-deploy / T-1h / yesterday; the
  whole graph re-diffs against it. (Replaces a timeline scrubber.)
- **pin** — freeze the current `(selection, projection, grounding)` into
  a durable View URL

## Fractal zoom into a node

Click to zoom into a node → render its interior span graph as a View at
finer grain, in the *same component* (selection points inside the node).
Same path as the monolith case.

> **⚠ RISK — scope creep.** Additive UI surface on a tight budget. Build
> the inter-service view first; zoom is the last thing, only if time
> allows. High payoff if it lands. See `view-model.md`.

## Open questions

- Layout engine: hand-roll the layered DAG layout vs. adopt
  `dagre`/`elkjs`. (Adopting a dep is a judgment call we make later.)
- How much projection control lives in the UI vs. is CLI-driven.
- Which named bases to support out of the box, and how the generated
  exemplar exposes a clean "pre-deploy / last-known-good" anchor.
- Delta normalization per signal: absolute (Δ ms) vs relative (% change),
  and the diverging color scale's midpoint/clamp.
- (Resolved) No timeline scrubber — the comparison-base picker is the
  temporal control; absolute time-series lives in the drill-down panel.
- Is the FE a pure renderer of server-side Views, or does it compute
  selection client-side? Lean: renderer + light steering via URL; the
  selector/query logic stays server-side.
