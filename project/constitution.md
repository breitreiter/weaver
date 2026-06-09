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
