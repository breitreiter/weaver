# weaver

Investigate a microservice system from observed telemetry — with an AI agent as
your research partner.

weaver is a small CLI, an HTTP API, and a web workspace for working through an
incident in a microservice system: you forage through metrics, logs, and traces,
pin the findings that matter, and write up what's happening in a single **living
document** — co-edited, in real time, with an AI agent. The document *is* the
investigation, and it flexes to the moment: an RCA while it's burning, a
post-incident review once it's out, a design note when nothing's on fire. You and
Claude (or Cursor, or whatever you use) work the same document together — one as
the writer of record, one as the tireless forager and drafting partner.

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
- **It enumerates; you conclude.** Every command lists facts; none emit a verdict,
  a score, or an "AI root cause." The causal claim — "this *caused* that" — is
  written, in prose, in the shared document. The agent can draft and argue it, but
  the conclusion is reached through the back-and-forth between you, and *you* own
  when the analysis is final and what it says. The tool is a toolkit over a
  mystery, not an oracle — the judgment stays with the person who has skin in the
  game.

## The intended usage loop

The agent does **not** live inside weaver. It lives in whatever harness it already
likes — a coding agent in your terminal, an editor assistant, a chat — and it
reaches weaver through the **CLI as a tool surface**. You keep the web workspace
open; the agent drives the CLI; you both see the same document.

A session looks like this:

1. **You point.** "Something's wrong with checkout" / "what's that spike around
   2:30?" / "the db thing."
2. **The agent forages.** It runs `weaver search`, `traces`, `service`,
   `evidence` — pulling the tedious, parallelizable legwork — and narrates what it
   finds in the ids you both can see.
3. **You both write it up.** Findings get `pin`ned by their typed id, and you and
   the agent co-edit the document — it appends findings, drafts the timeline, and
   proposes a read; you correct, reframe, and decide. Findings are referenced in
   the prose by that same typed id (`@an:checkout:latency_p99`), so every claim can
   point back at the fact that grounds it. Edits from either side land live, merged
   line-by-line.
4. **You decide it's done.** The agent assembles and argues both sides, but the
   conclusion emerges from the conversation in the document — and the call to roll
   back, page a team, or close the incident is yours.

The agent is a *support role*: a perfect-recall forager and drafting partner that
never loses the thread, not the investigator of record. The decisive facts in a
real incident are often out-of-band — what a deploy *meant*, whether a sale was
running — and those live with people, not in telemetry. An agent that concluded
from the data alone would confidently pick the plausible coincidence; weaver is
built so the human brings the tie-break.

The contract between the agent and your screen is **URLs**: the document is
addressed by a `/view?board=…` URL that works anywhere a board id is accepted.
Paste it to the agent and you're both on the same document.

## Surfaces

- **CLI** (`src/Weaver.Cli`) — the agent's hands and yours. Run bare `weaver` for
  the full help. Verbs group into three movements:
  - *forage* — `search`, `facets`, `service`, `metrics`, `logs`, `traces`,
    `trace`, `evidence`
  - *correlate* — `anomalies`, `timeline`, `blast-radius`, `relationships`
  - *write it up* — `board`, `pin`, `unpin`, `doc show`, `doc edit`, `doc append`
- **HTTP API** (`src/Weaver.Api`) — serves the same computed primitives and owns
  the documents (boards). Default `http://localhost:5180`.
- **Web workspace** (`web/`) — a React/Vite surface: the forage panel, the
  co-edited document (CodeMirror) at its centre, and a rail of pinned evidence.
  Default `http://localhost:5173`.

## Getting started

**Prerequisites:** .NET SDK (for the API + CLI) and Node.js (for the web workspace).
Python 3 only if you want to regenerate datasets.

```bash
# 1. API (boards + computed telemetry primitives) — http://localhost:5180
dotnet run --project src/Weaver.Api

# 2. Web workspace — http://localhost:5173
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
| `WEAVER_WEB` | `http://localhost:5173` | web workspace base (for printed URLs) |
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
  Weaver.Core       the analysis primitives + the document 3-way merge
  Weaver.Contracts  shared DTOs
web/                React/Vite workspace (forage + co-edited document)
tests/              unit tests (e.g. the document merge)
tools/datagen/      synthetic dataset generator
data/               SQLite databases + topology definitions
project/            design docs, plans, and constitution
```

## Status & limitations

This is a working prototype with sharp edges and a deliberately narrow scope:

- runs only against the bundled synthetic datasets; no real ingestion path
- single-node, local-first; no auth, no multi-tenant, no scale story
- the document model and CLI are still settling — verbs and ids may shift
- it is a study of an *interaction*, not a complete observability product

If you take one thing from it, take the interaction model: a human and an agent
co-writing a shared, evidence-grounded, never-concludes-for-you document — not a
tool that hands you an answer.
</content>
