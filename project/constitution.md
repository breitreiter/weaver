# weaver — constitution

The foundational document. What weaver is, who it's for, and the
principles everything else hangs off of. Kept short on purpose.

## Purpose

weaver helps a responder investigate an incident in a complex
microservice system from **observed telemetry** — and turns the
investigation into something *legible and defensible*.

Stored data is pure observation (OTel-shaped: services, deps, metrics,
logs, traces, change events). Everything interpretive — anomalies,
timelines, blast radius — is **computed live** from that raw data,
never stored. weaver hands you facts; the judgment stays the human's.

The synthesis surface is a **co-edited document**, addressed by a board
id, that the responder and Claude write together — a live RCA while it
burns, a post-incident review once it's out, a design note when nothing's
on fire. weaver's thesis: the value isn't a verdict the tool emits — it's
the **argued, grounded write-up** the human and model reach together, every
claim anchored to a pinned fact.

> weaver is a **toolkit over a mystery, not an oracle.** The data holds a
> real, solvable situation, but there is no stored "status" or answer
> label; health is *derived*, never recorded. The conclusion is *written*,
> in prose, in the document — never emitted as a score or a root cause.

## Users

Abstractly: teams of SREs during incident response.

In practice the single audience that matters is **one operator driving
it live and reasoning out loud, with Claude as co-author** — so every
feature must be steerable on demand, explainable in a sentence, and usable
by a tired responder who wants the legwork done so their scarce attention
goes to judgment.

## Core capabilities

Three movements, shared by the CLI and the web UI so they always agree:

- **Forage** — find things: search (anomalies / traces / logs / services /
  metrics / changes), facets, a service's signals, a trace's spans, the
  evidence dossier for a node. Every result carries a **typed id** you can
  pin.
- **Correlate** — relate things, as *enumerations, never a verdict*: what
  moved vs. a base window (anomalies), onset ordering (timeline), who depends
  on a node (blast-radius), the concrete facts between two services
  (relationships). These *ground* a claim; they never crown a cause.
- **Write it up** — pins + the **co-edited document**. Pin a finding by its
  typed id, then cite it in the prose by that same id
  (`@an:checkout:latency_p99`) so every claim points back at the fact that
  grounds it. The human and Claude edit the same document at once.

Everything interpretive is computed live from raw observation; nothing is a
stored verdict, score, or "root cause."

## Stack

- **Backend:** .NET (`Api`, `Cli`, `Core`, `Contracts`) — owns the read-only
  telemetry store, the live analysis primitives (anomalies / timeline /
  blast-radius, computed at query time), and the writable board + document
  store.
- **Frontend:** React — the investigation surface: a forage (search) panel,
  the board of pinned evidence, and the co-edited document.
- **Agent surface:** a CLI on PATH — the model-agnostic contract to the same
  primitives that back the UI.
- One board at a time. No backend services beyond what this needs.

## Interface principles

- **Coordination is plain URLs.** The contract between the agent (CLI /
  coding agent) and the React UI is a URL — nothing fancier. The
  investigation lives in a document addressed by a **board id**
  (`/view?board=…`): clickable from the console, pastable into Slack,
  storable in a ticket. Durable by design (a stable board id), so a link
  still resolves later.
- **Rendering follows meaning; pixels are earned.** What's shown is what's
  been *selected* — pinned evidence, a node's signals, an agent-authored
  chart — density earned by relevance, never decoration. weaver never
  renders the system as a hairball and **never** uses a force-directed
  layout, not even ironically. (The graph-as-centerpiece was retired in the
  document pivot; the anti-hairball stance stays as a durable rule.)
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

## The line we hold

The stance above leans hard on the model; this is its counterweight. Claude
is a full co-author — it forages, pins, drafts a leading read, argues both
sides — but it does **not** unilaterally declare the root cause and does
**not** ship the fix. The human owns *when the analysis is done and what it
says*, and the decisive call (roll back, shed load, page a team) is theirs.
This isn't a limitation to apologize around; it's the design. The tie-break
is frequently out-of-band — what a deploy *meant*, whether a sale was
running — truth the telemetry never held, and a model that "concludes" from
data alone will confidently pick the plausible coincidence over the real
cause. See `plans/agent-role.md`.

## Non-goals

- No auth, users, accounts, or billing.
- **No stored verdicts.** weaver never records health, a score, or a root
  cause; interpretation is computed live and the conclusion is written by
  hand in the document.
- **No graph hairball, ever.** No force-directed layouts, no rendering the
  full system as a node-link diagram — not as an anti-pattern, not as a joke.
  See Interface principles.
- Not a general-purpose observability platform — weaver is one sharp idea,
  demonstrated well. No breadth-for-breadth's-sake features.

Scaling choices we deliberately *don't* make for the demo (SQLite over
ClickHouse, a selector grammar over a search engine, no embeddings) — and
how the production-grade option slots in behind the same interface — are
documented in `plans/demo-vs-production.md`. Knowing when not to reach for
the big hammer is part of the point.

## Open questions

The graph-era questions this section once held — what makes a region
"interesting," which partitioning approach earns its keep, where the example
graph comes from — are **retired**: the graph centerpiece was replaced by the
co-edited document, and the demo scenario (a flash-sale incident on synthetic
OTel telemetry) is settled. Live design work now lives in `plans/` (house
style, board time windows, the demo script) and the working list in
`TODO.md`; this document holds only what's *foundational*, not the backlog.
