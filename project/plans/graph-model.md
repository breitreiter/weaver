# weaver — the graph model (ontology)

Not a build-plan — a **shared mental model**. Several agents touch the board/search
surface concurrently; this is the ontology they should all align to so their
rendering and query decisions converge. Nobody builds directly from this doc, which
is exactly why it's safe to write. Each section marks **settled** vs **open**.

## The closed ontology

> **Two primitives — service (node) and relationship (edge). All observed data is
> trans-time evidence layered onto one or the other. A trace is a path-shaped read
> across both. Time selects and labels; it never positions.**

- **Node = a service.** Data *about a service* (anomaly, metric, log, single-service
  deploy) hangs on it. **Settled — shipped** (`BoardNode` + layered `Evidence`,
  keyed by serviceId; see `board-build.md`).
- **Edge = a relationship between two services.** Data *about their interaction*
  (link latency / error-rate, an RPC, a trace hop) hangs on it. The relationship
  *is* the edge; the interaction is *evidence on* the edge — the same shape as a
  node and its evidence. **Open — schema delta:** `BoardEdge` needs to carry
  layered evidence, mirroring `BoardNode`. Not built.
- **Nothing else is a graph object.** Not anomalies, not logs, not traces, not
  deploys. They are all attributes of a node or an edge.

The three intuitive categories ("data about a service / an interaction / a
relationship") collapse into the two primitives: relationship = the edge,
interaction = evidence on the edge.

## Traces — a path-lens, not a vertex (settled in model, open in UX)

The data already decomposes onto the two primitives. `SpanEntity`
(`Entities.cs:76`) tags every span with a `ServiceId` **or** an `EdgeId`, plus
`SelfMs`: self-work spans are node-scoped, cross-dependency spans are edge-scoped.
So a trace *is* a set of node-scoped + edge-scoped timed segments — no coercion
needed to place it.

Therefore a trace is **never a node**. Two equivalent UX renderings, both inside
the ontology (this is the only open call here):
- **Decompose** — render each span as evidence on its service/edge.
- **Path-lens** — a saved highlight that lights up the nodes + edges the trace
  touches; the trace is a *view over* the graph, not an object on it.

Leaning path-lens (matches how a trace is actually read — a route through the
system). Either way, multi-hop never becomes a third primitive.

## Time is a selector and a label — never an axis (settled)

The graph is **atemporal**: fixed topology, fixed geometry. Time enters in exactly
three places, none of them graph geometry:

1. **Search** — the filter that decides *which* evidence you locate and pull onto
   the board. (Time-filter agent's surface.)
2. **`at` on each piece of evidence** — a label on the pinned fact, not a position.
   Two anomalies a week apart sit on the same node, told apart only by `at`.
3. **A human-drawn "precedes" edge** — if temporal order matters to the narrative,
   the operator *asserts* it as red string. Judgment, not geometry.

Even at the lowest level the data agrees: a span's time is `StartOffsetMs` /
`DurationMs` — relative to its trace, never wall-clock.

**Topology-versioning** ("service didn't exist at T") — **elided.** The topology is
fixed across the demo window; take that luxury. If it ever matters it's one more
attribute, not a new dimension.

## The payoff this buys (open / future — the seed, not a commitment)

Fixed geometry + `at`-stamped evidence = **state-over-time as lighting changes over
a stationary map.** A future time-cursor scrubs the window; which evidence is
"active" on each node/edge changes; nothing moves. "Fine → gorked → fine again"
falls straight out — the same edge holding facts at t1/t2/t3, its visual state
bound to where the cursor sits (and `MetricSamples` gives the continuous
recovery, not just paired events). A force-directed or time-positioned graph
*couldn't* do this — the geometry would thrash. The timeless call is what makes
time animatable later. **TBD on UX; the substrate is in place.**

## Status summary

| Piece | State |
|---|---|
| Node = service + layered evidence | ✅ shipped |
| Edge = relationship + layered evidence | ⬜ open — `BoardEdge` evidence schema |
| Trace placement (decompose vs path-lens) | ⬜ open — UX call; model settled |
| Time as selector/label, not axis | ✅ settled (rule) |
| Topology-versioning | 🚫 elided by decision |
| Temporal-state replay ("fine→gorked→fine") | 🔮 future seed; substrate ready |

The graph *visual* (how an edge shows evidence, how a path lights up) is the
Figma-gated piece and overlaps the panel-redesign work — coordinate there.
