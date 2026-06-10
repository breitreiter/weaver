# weaver — the sensemaking pivot (search + board)

A pivot in the UI (2026-06-09). The single screen becomes **two panels**:

- **Left — search: the fire axe.** The universal lever to ask questions of the
  telemetry and get raw-ish answers. You forage. When something looks
  interesting, you **pin** it to…
- **Right — the board: the wall of red string.** You assemble pinned findings
  into a *narrative* — nodes decorated with their evidence, connected by edges
  you draw. The graph is **constructed**, never auto-rendered.

This supersedes the auto-neighborhood graph view (it read as a hairball — the
exact thing we banned). The hairball is now impossible *by construction*:
nothing is on the board that someone didn't deliberately pin.

## The theory: Pirolli & Card's sensemaking loop, split across the screen

- **Left = foraging** (search → filter → read → the *shoebox* of maybe-relevant
  findings).
- **Right = sensemaking** (arrange snippets into a *schema* / hypothesis — the
  red string).
- **Pinning is the bridge** between the two loops. It is the most important
  interaction in the whole UI.

**The red string is the discriminate step.** The left panel *enumerates* facts
(anomalies, blast-radius, logs); the right panel is where a reasoner
*discriminates* — drawing "this explains that." The tool supplies dependency
edges (facts); the investigator draws the causal/temporal string (judgment). The
enumerate/discriminate line (`analysis-architecture.md`) becomes a physical
left/right split. The tool never draws the red string.

## Why this is right

- **No hairball, by construction.** The board holds only what was pinned.
- **It scales to the brief.** "How does this work at several-hundred services?"
  You never draw the hundreds — you *forage* the hundreds (left) and *narrate*
  the ~ten that matter (right). Search scales to any N; the board stays
  human-sized.
- **It realizes the coordination contract.** The board is a URL-addressable
  artifact: the agent pins a wall of red string and hands the human the link.

## The agent co-builds the board (the "CLI lift")

The board is **co-constructed by human and agent on the same board**. Claude
forages with the CLI (existing verbs), then **pins findings and draws edges**
into a shared board the human is watching. So the CLI must gain write verbs:

- `pin <thing>` — add a foraged finding to the board (carries its evidence).
- `link <a> <b> --as "explains|precedes|…"` — draw a red-string edge.
- `board [show|new] ` / `open` — create/get the board, print its URL.

Human and agent share the board via its id/URL; either can pin, link, arrange.
The agent's job ends at a *reviewable* wall of red string — the human
adjudicates. (Same surface, human or agent — parity holds.)

## Components

### Left — search / query language *(biggest open design piece)*

One "ask" box returning pinnable rows. Unify what we already have:

- selector grammar — `anomalous`, `blast-radius(x)`, `subsystem:payments`,
  `downstream-of(x)`
- free-text log search (FTS5)
- the analysis verbs — `anomalies`, `timeline`, metrics/traces queries

Results are *raw-ish enumerations* (facts, never verdicts). Any row — or a whole
result set — is pinnable.

### Right — the board (data model)

- **Board** `{ id, title, createdAt, items[], edges[], notes? }`
- **BoardItem** (a pin) `{ id, kind: service|edge|log|anomaly|trace|metric|note,
  ref, evidence (snapshot that justified the pin: a shape_code / log line /
  anomaly delta), x?, y? (placement), label? }`
- **BoardEdge** `{ id, from, to, kind: dependency|causal|temporal|custom, label?,
  drawnBy: human|agent }` — dependency edges are tool-supplied facts;
  causal/temporal are the red string.
- Every pin carries its **provenance** — the board is self-justifying (every
  pixel accountable to evidence).

### Persistence — a separate, writable store

The board is **writable user content**, so it can NOT live in the read-only
telemetry DB (`weaver.db` stays `Mode=ReadOnly`, pure observation). Boards get
their own writable store (a `boards.db` SQLite, or JSON-per-board). Server-side
so CLI writes and UI reads see the same board.

## Build plan (phased)

1. **Board backend** — writable board store + API: create board, pin item, add
   edge, get board. (Not blocked.)
2. **CLI lift** — `pin` / `link` / `board` verbs writing to the board API. Agent
   can forage → pin → link → hand over a URL. (Depends on 1.)
3. **Left panel** — the search/query GUI over the primitives; results list with
   pin affordance. (Query-language design not blocked on Figma.)
4. **Right panel** — render the board: pinned nodes + dependency edges +
   red-string edges + evidence decoration; manual placement. (Layout per
   Joseph's Figma pass.)
5. **Polish** — Material icons, particle edges, coherent base periods, home nav,
   home page. (After the view direction lands; some couples to the data agent's
   scenario timeline.)

## Open questions

- The query grammar (the big one) — exact surface and result shape.
- Pinnable granularity (a row vs a whole result set).
- Board layout: manual drag-to-place (very on-metaphor) vs assisted.
- Persistence choice: `boards.db` vs JSON vs client-only + URL.
- Live sync: does the human's board poll for the agent's pins, or reload on a
  shared id? (Poll for the demo "watch Claude build the wall" moment.)
