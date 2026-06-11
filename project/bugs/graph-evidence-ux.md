# Graph chip + evidence-panel UX problems

Captured 2026-06-10 from a UX pass over the board chips and evidence panel.
These are interrelated (all rooted in the incoherent "node" concept) but we'll
work them one at a time. Listed roughly inner-cause → outer-symptom.

Code anchors: `web/src/Board.tsx` (`KIND_ICON` L22-25, `ServiceNode` L160-181),
`web/src/Evidence.tsx` (explore buttons L22-28, node label L62, evidence badges
L70), `web/src/Icon.tsx`. Related TODO entries: "Board model refactor: a pin =
node + layered evidence" and "Material icons per node kind" in `TODO.md`.

> **Update 2026-06-10:** the board model refactor (BoardNode + layered Evidence,
> keyed by serviceId; "node" retired from UI copy → "service") landed and
> **resolves #1, #2, #4**. #5 is structurally addressed (one section + one button
> row per service — the model can no longer repeat a service). #3 (the three-part
> visual stack) is now *coherent* but its layout is untouched — still open. #6
> (search time filter) untouched. See `project/plans/board-build.md`.

## 1. "node" is not a real entity type  — ✅ resolved (model refactor)

There is no coherent notion of a *node* as a formal object type anywhere in the
project. The only "nodes" are ReactFlow graph nodes — present purely because
it's a graph. We've borrowed graph-vocabulary "node" and leaked it into the
domain, where it means nothing.

## 2. Graph nodes are actually any object type (just evidence)

What sits on the graph is any collected bit of evidence — metric, log, trace,
anomaly, change, service. Calling a *metric* a "node" is incoherent; you
wouldn't think of a metric as a node. (Deferring the deeper question of whether
heterogeneous evidence belongs on one graph at all — flagged, not solved here.)

## 3. Three-part label is confusing (service / type chip / actual name)

Each item shows, top to bottom: the **service** as the top-level label, then a
**type chip** (the Material icon + kind), then the **actual name** of the
anomaly/metric/etc. Reading order and hierarchy are unclear — the thing the user
actually cares about (the name) is buried under two layers of categorization.

## 4. "node" wording in the evidence panel is meaningless to the user

The evidence panel uses the term "node," but there's no formal concept of a node
to refer to. The user has no reason to map "node" → "thing that can be placed on
the graph." It's internal jargon surfaced as UI copy.

## 5. Evidence-panel buttons duplicate per-item but only filter on the service

The explore buttons on each evidence item flow through to filter on the
**service** only. So when several items share a service, the same service name is
listed ~8 times with the same set of ~8 buttons repeated under each — pure
duplication. The buttons key off the service, not the item, so they should be
deduplicated/lifted to the service level rather than repeated per evidence row.

## 6. Search panel has no time filter input (critical)

(Different surface — the left search panel, not the board/evidence — but part of
the same working list.) Search has **no notion of time as an input filter**.
For telemetry this is critical: you almost always want to scope a query to a
window (incident window, last N minutes, vs-base period). Right now time is only
an output dimension (chart wall / histogram), never an input constraint. Note
`TODO.md` "Left panel v2" lists `window` among the intended facets — this is the
concrete, must-have instance of that.
