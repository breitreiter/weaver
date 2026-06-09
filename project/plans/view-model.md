# weaver — the view model (three axes)

A **View** is the unit of everything weaver renders and shares. It is
defined by three orthogonal axes. The CLI verbs and the URL contract are
derivations of this model, not separate designs.

## The three axes

| Axis | The question | What it is |
|---|---|---|
| **Selection** | *What's on screen?* | the subgraph — which services & edges are present. Output of the selector layer. |
| **Projection** | *What does it say?* | the decoration — a **delta vs a chosen base** (default), mapped onto a visual channel (node fill = Δ error rate on a red/blue diverging scale, size = throughput, badge = role, edge weight = call volume). Absolute value is a non-default mode. See "RCA is differential". |
| **Grounding** | *Why should I believe it?* | the evidence as a **comparison**: the **lens** is `(base, subject, signal)` and the projected value is the delta between them; plus the **reservoir** (raw metrics/traces/logs to drill, base & subject overlaid). Time collapses into the choice of base — see "RCA is differential". |

## RCA is differential — so time isn't an axis

For root cause you almost never want "state at time T" — you want a
*comparison*. RCA is differential: before vs after, trajectory vs
baseline. So "project time onto the graph" collapses into "encode a
delta," and a delta is **static**. Time stops needing an axis. This is
the unlock the whole surface is built around.

- **Projection defaults to differential.** Every node/edge is colored by
  how much it *changed* vs a base (red = worse, blue = better — Gregg's
  differential flame graph, lifted onto the DAG), not by absolute value.
  Absolute is the special mode. The entire "what changed?" question
  collapses into one still frame — which is the question RCA actually asks.
- **The topology itself is a diff.** Not just color: a dependency that
  *appeared* since the base (a new call path, a retry/fallback edge) or a
  node that vanished. A new edge since the deploy is often the smoking
  gun. Structural delta + decoration delta, one frame.
- **Grounding becomes comparative.** The lens is a pair —
  `(base, subject, signal)` — and the projected value is the delta. The
  interaction that matters is **choosing the base** (vs pre-deploy /
  vs T-1h / vs same-time-yesterday / vs last-known-good), not dragging a
  playhead. Bases are first-class and *named*, because that's how
  engineers actually ask "what changed since X." The view must always
  announce its base ("vs pre-deploy 14:02") — the base does enormous
  work, and a wrong base lies.
- **It composes with fractal zoom — not by accident.** Gregg's red/blue
  flame graph *is* the interior-of-one-process diff; diffing the DAG is
  that same idea one level up. Zoom into a node, diff its interior span
  tree, and you're back on the original flame graph. Same idea at every
  scale.

## Orthogonality is the interaction loop

The three axes are independent, and the investigation *is* perturbing
one at a time:

- hold Selection, re-**project** → recolor the same subgraph by latency
  instead of errors
- hold Projection, re-**select** → expand to the blast radius
- choose the comparison **base** (pre-deploy / T-1h / yesterday) → the
  whole graph re-diffs against it. *This replaces a time scrubber: the
  diff is the still frame, the base is the control.*

Pick an axis, perturb it, read the result, re-hypothesize.

## Every pixel is accountable to evidence

Because grounding is explicit, every node can answer *"why am I here?"*
(selection provenance: "I'm in the blast radius of X") and every
decoration can answer *"why this color?"* (projection provenance:
"error_rate over window W exceeded baseline p99"). Drill-down is just
following that *because* back to the reservoir.

This is the same trust mechanism as the no-oracle stance — **the graph
never asserts anything it can't ground** — and the deep reason for the
earn-your-pixels rule: a pixel with no evidence behind it has no
business existing.

## A View is a tuple → a URL

A View *is* the tuple `(selection, projection, grounding)` — three
serializable choices. That is exactly what a URL carries (ad-hoc) or
references via a stored View id (durable, Slack/ticket-pasteable). The
CLI verbs become one operation per axis — set/refine **selection**, set
**projection**, set the **grounding** lens + **drill** the reservoir —
plus `pin` to freeze the current tuple into a shareable View URL.

## Layout — a fourth, *presentational* axis

How the selected nodes are arranged. Distinct in kind from the three
evidence axes (it carries no evidence), so kept separate. The
no-force-directed rule means layout can't be "let physics decide" — it
must be *meaningful*: dependency-depth columns, request-path order, or
subsystem grouping. See `web-ui.md`.

## The model is fractal — zoom into a node

A node, zoomed in, is itself a graph: the service's interior span tree
(operations that never exit the node — `db.connection_acquire`,
`serialize_response`). Zoom is just **Selection at a finer
granularity**; Projection and Grounding recurse unchanged, and the URL
still encodes one `(selection, projection, grounding)` tuple whose
selection points *inside* a node.

This is the *same code path as the monolith case* (a monolith is one
node you zoom into — a graph of modules/endpoints from span names), and
it closes the investigation's last hop: from *"it's payments-db"* to
*"it's the `connection_acquire` span inside payments-db."* Data support:
`Span.kind = internal` (see `data-model.md`).

> **⚠ RISK — scope creep.** The developer brain correctly flags this as
> additive surface area on an already-full 5–6h budget. Guardrails:
> build the **inter-service loop first**; zoom rides the monolith path as
> the *last* thing, only if time allows; and we model interior
> **operations, not a codebase** (the moment we invent function call
> graphs, we've lost). **Payoff if it lands is high** — it unifies
> monolith + microservice, reaches interior root causes, and the "deps
> are green, so the cause is inside — zoom" demo beat is very cool.
