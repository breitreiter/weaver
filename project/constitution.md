# weaver — constitution

The foundational document. What weaver is, who it's for, and the
principles everything else hangs off of. Kept short on purpose.

## Purpose

weaver helps SREs troubleshoot incidents in complex microservice
architectures by turning the service dependency graph into something
*legible*. Services are nodes, connections are edges — and weaver's
real job is to **cleave that graph into "interesting" regions that
tell a story** about where an incident lives and how it propagates.

A raw dependency graph of a real system is a hairball; staring at it
tells you nothing. The thesis of weaver is that the value isn't the
graph — it's the *partitioning*: a principled way to surface the
subgraph that matters right now.

> The narrative angle / "gripping purchase" — what specific story the
> partitioning tells, and the scenario we demo it on — is still open.
> See Open Questions.

## Users

Abstractly: teams of SREs during incident response.

In practice the single audience that matters is **one operator
driving it live and reasoning out loud** — so every feature must be
steerable on demand and explainable in a sentence.

## Core capabilities

- Visualize a service dependency graph (nodes = services, edges =
  connections).
- Partition the graph into coherent, "interesting" regions rather
  than showing the whole hairball at once.
- Let the operator steer: focus a region, expand/collapse, follow a
  propagation path.
- Back the partitioning with logic the operator can explain and
  defend, not a black box.

## Stack

- **Backend:** .NET — owns the graph model and partitioning logic.
- **Frontend:** React — single-screen interactive graph view.
- One screen. No backend services beyond what this view needs.

## Interface principles

- **Coordination is plain URLs.** The contract between the agent (CLI /
  coding agent) and the React UI is a URL — nothing fancier. Every
  shareable artifact (a curated View) *is* a URL: clickable from the
  console, pastable into Slack, storable in a ticket. Durable by design
  (a stable View id), so a link in a ticket still resolves later.
- **Pixels are earned.** The UI **never** renders the full graph, and
  **never** uses a force-directed layout — not even ironically. A view
  exists only once a filter has narrowed the system to something
  *meaningful*; rendering follows from selection, never the reverse.
  This is why the filter/selection layer (the "power-tool") is core, not
  a convenience — see `plans/data-model.md`.
- **The agent surface is a CLI over bash.** Any bash-capable coding agent
  drives weaver (we demo on Claude Code); the CLI is a model-agnostic
  contract, not an MCP/bespoke integration. The full stance — weaver as a
  toolkit a reasoning agent investigates with, never an oracle — is the
  brief's graded deliverable: see `plans/agent-workflow.md`.

## Design stance — decision support first, lean on the model

The north star is **amazing decision support**: weaver exists to make a
responder's judgment faster and better, not to be a complete tool with
every knob exposed.

We build on a standing assumption: **there is a smart, tool-using model on
tap.** That changes the cost structure of a feature. The usual reason to
cut a powerful capability is that *configuring* it needs a complex UI — a
**UX swamp dungeon** you can't get locked-in and buttery-smooth in the time
you have. (We hit exactly this with time-window curation: merging and
splitting spans *by meaning* is a direct-manipulation nightmare — see
`plans/board-time-windows.md`.)

So the bias: **when a capability clearly improves decision support and is
obvious to *use*, but *configuring* it takes advanced wizarding, include it
anyway and dump the complex configuration on the model.** The human gets the
simple surface (select, view, ask in prose); Claude does the hard authoring
in the background and narrates it.

Two guardrails keep this a stance, not a licence to punt every hard UI:

- **AX has to be reasonably good.** AX = agent experience, the model's UX:
  the model needs a clean, legible contract to drive (the CLI, window CRUD,
  the read-only SQL sandbox) the same way a human needs good UX. A capability
  we offload but can't give the model a decent handle on is not shipped.
- **Prove Claude can hack it.** We demonstrate the model actually solves the
  configuration problem — authors the chart, splits the window at the right
  boundary — before we lean on it. Bias-toward-include, not assume-it-works.

The worked examples: the human never builds a chart or drags a window
boundary; they say what they want, Claude authors it against a small tool
surface, and the UI stays for selecting, viewing, and pinning. Division of
labor — the human brings the meaning, the model brings the mechanics.

## Non-goals

- No auth, users, accounts, or billing.
- No screens beyond the single graph view.
- Not a general-purpose observability platform — weaver is one sharp
  idea, demonstrated well.
- No breadth-for-breadth's-sake features.
- No force-directed graph layouts. No rendering of the full graph / the
  hairball, under any circumstance — not as an anti-pattern, not as a
  joke. See Interface principles.

Scaling choices we deliberately *don't* make for the demo (SQLite over
ClickHouse, a selector grammar over a search engine, no embeddings) — and
how the production-grade option slots in behind the same interface — are
documented in `plans/demo-vs-production.md`. Knowing when not to reach for
the big hammer is part of the point.

## Open questions

- **The narrative angle.** What makes a region "interesting"? What
  incident story does the partition tell, and on what example data do
  we demo it? This is the crux and is being worked out next.
- What graph-partitioning approach earns its keep here (community
  detection, blast-radius from a failing node, latency/error-weighted
  cuts, …) and why.
- Where the demo's example graph comes from (synthetic vs. a known
  reference topology).
