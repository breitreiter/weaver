# weaver

Investigate a service graph from observed telemetry — with an AI agent as your
research partner.

weaver is a small CLI, an HTTP API, and a web "board" for working through an
incident in a microservice system: you forage through metrics, logs, and traces,
correlate what moved against what, and pin your findings to a shared wall with
red strings between them. The twist is that it's built to be driven *with an
agent* — you and Claude (or Cursor, or whatever you use) work the same board
together, one as the investigator, one as the tireless forager.

> **Maturity, honestly.** This is an **interface concept / prototype**, not a
> production observability tool. It does not ingest your real telemetry, connect
> to OpenTelemetry collectors, scale, or persist anything you'd trust on call. It
> runs against small, **synthetic, authored datasets** and exists to explore one
> question: *what does an observability surface look like when it's designed,
> from the ground up, to be operated by a human and an agent together?* Treat it
> as a design study you can actually run, not a tool you'd adopt.

## The idea

Most observability tools are built to answer for you — a dashboard, an alert, an
"anomaly score," increasingly an "AI root cause." weaver deliberately doesn't.

- **The stored data is pure observation.** What's on disk is raw, OTel-shaped
  telemetry: services, dependencies, metric series, logs, traces, change events.
  There is no stored "status," no health column, no answer label. Health is
  *derived* at read time, never recorded.
- **Everything interpretive is computed live.** Anomalies, onset timelines, blast
  radius — all calculated on the fly from the raw data, by shared code that backs
  both the CLI and the web UI, so the two never disagree.
- **It enumerates; it never concludes.** Every command lists facts. The causal
  claim — "this *caused* that" — exists only as an explicit, human-drawn red
  string on the board, labeled as a hypothesis. The tool is a toolkit over a
  mystery, not an oracle. The judgment stays with the person who has skin in the
  game.

## The intended usage loop

The agent does **not** live inside weaver. It lives in whatever harness it already
likes — a coding agent in your terminal, an editor assistant, a chat — and it
reaches weaver through the **CLI as a tool surface**. You keep the web board open;
the agent drives the CLI; you both see the same wall.

A session looks like this:

1. **You point.** "Something's wrong with checkout" / "what's that spike around
   2:30?" / "the db thing."
2. **The agent forages.** It runs `weaver search`, `traces`, `service`,
   `evidence` — pulling the tedious, parallelizable legwork — and narrates what it
   finds in the ids you both can see.
3. **You both build the wall.** Findings get `pin`ned by their typed id; proposed
   causes get drawn as `link`s (red strings) with hypothesis labels. The agent's
   pins show up on your screen within a poll.
4. **You adjudicate.** The agent argues both sides, runs `board review` to show
   what each string is grounded on, and proposes crossouts — but the call to roll
   back, page a team, or close the incident is yours.

The agent is a *support role*: a perfect-recall forager and rubber-duck that never
loses the thread, not the investigator of record. The decisive facts in a real
incident are often out-of-band — what a deploy *meant*, whether a sale was running
— and those live with people, not in telemetry. weaver is honest about that line.

The contract between the agent and your screen is **URLs**: a board is addressed by
a `/view?board=…` URL that works anywhere a board id is accepted. Paste it to the
agent and you're both on the same board.

## Surfaces

- **CLI** (`src/Weaver.Cli`) — the agent's hands and yours. Run bare `weaver` for
  the full help. Verbs group into three movements:
  - *forage* — `search`, `facets`, `service`, `metrics`, `logs`, `traces`,
    `trace`, `evidence`
  - *correlate* — `anomalies`, `timeline`, `blast-radius`, `relationships`
  - *build the wall* — `board`, `pin`, `unpin`, `link`, `crossout`
- **HTTP API** (`src/Weaver.Api`) — serves the same computed primitives and owns
  the boards. Default `http://localhost:5180`.
- **Web board** (`web/`) — a React/Vite surface for viewing the graph and the
  pinned wall. Default `http://localhost:5173`.

## Getting started

**Prerequisites:** .NET SDK (for the API + CLI) and Node.js (for the web board).
Python 3 only if you want to regenerate datasets.

```bash
# 1. API (boards + computed telemetry primitives) — http://localhost:5180
dotnet run --project src/Weaver.Api

# 2. Web board — http://localhost:5173
cd web && npm install && npm run dev

# 3. CLI — run against the API
dotnet run --project src/Weaver.Cli -- search anomalies
dotnet run --project src/Weaver.Cli -- facets
```

For convenience you can drop a wrapper on your PATH so the agent (and you) can
just type `weaver …`:

```bash
# ~/.local/bin/weaver
#!/usr/bin/env bash
exec dotnet run --project /ABSOLUTE/PATH/TO/weaver/src/Weaver.Cli --no-build -- "$@"
```

Configuration is via environment variables:

| Var | Default | Meaning |
| --- | --- | --- |
| `WEAVER_API` | `http://localhost:5180` | API the CLI talks to |
| `WEAVER_WEB` | `http://localhost:5173` | web board base (for printed URLs) |
| `WEAVER_BOARD` | _(unset)_ | current board id/URL, so you can omit `--board` |

## Data

The datasets are **synthetic and authored** — a small e-commerce topology
(services, dependencies, request routes) emitted as raw telemetry over a time
window. They live in `data/` as SQLite databases alongside the topology
definitions. New datasets are generated by `tools/datagen/generate.py` from a
topology YAML. Nothing here is real production data.

## Project layout

```
src/
  Weaver.Cli        the command surface (the agent's tool)
  Weaver.Api        HTTP API: boards + live-computed primitives
  Weaver.Core       the analysis primitives, shared by CLI + API
  Weaver.Contracts  shared DTOs
web/                React/Vite board UI
tools/datagen/      synthetic dataset generator
data/               SQLite databases + topology definitions
project/            design docs, plans, and constitution
```

## Status & limitations

This is a working prototype with sharp edges and a deliberately narrow scope:

- runs only against the bundled synthetic datasets; no real ingestion path
- single-node, local-first; no auth, no multi-tenant, no scale story
- the board model and CLI are still settling — verbs and ids may shift
- it is a study of an *interaction*, not a complete observability product

If you take one thing from it, take the interaction model: a human and an agent
investigating a shared, enumerated, never-concludes-for-you board — not a tool
that hands you an answer.
</content>
