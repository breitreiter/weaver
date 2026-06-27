# weaver — how the tool works (orientation for Claude)

weaver is a CLI + web surface for **investigating a service graph from observed
telemetry**. A user (a responder mid-incident, often) and Claude work it together
as co-researchers over a shared board. This file is the conceptual + UX map: what
the tool is, what the levers are, and how to behave when the user points you at
something. It is deliberately answer-free — it describes the *instrument*, not any
particular dataset loaded into it.

## What it is (and isn't)

- **A toolkit over a mystery, not an oracle.** The data holds a real, solvable
  situation, but there is no stored "status" or answer label. Health is *derived*,
  never recorded. weaver hands you facts; the judgment is the human's.
- **Stored data is pure observation.** What's on disk is raw observed telemetry
  (OTel-shaped: services, deps, metrics series, logs, traces, change events).
  Everything interpretive — anomalies, timelines, blast radius — is **computed
  live** at query time from that raw data. The same primitives back both the CLI
  and the web UI, so they always agree.
- **It enumerates; it does not conclude.** Every command lists facts. None of them
  emit a verdict, a score, or a "root cause." The causal claim — "this *caused*
  that" — only exists as an explicit, human-readable red string on the board, and
  it's labeled as a hypothesis, not a result.

## The three movements (the command surface)

Run bare `weaver` for the full help. The verbs group into three movements:

**1. forage** — find things. Same lens as the UI's left panel.
- `search <scope> [facets]` — the unified query. Scopes: `anomalies | traces |
  logs | services | metrics | changes`. Every row prints a **typed id** you can pin.
- `facets` — what subsystems / levels / routes / teams / templates exist (the
  vocabulary of *this* dataset; run it first when you don't know the terrain).
- `service <id>` — one service: its deps and a per-signal shape summary.
- `metrics <id> [--metric m]` — a signal's trajectory (shape + prose, not glyphs).
- `logs [<id>] [--grep q]` — log lines, full-text via `--grep`.
- `traces [--route r]` / `trace <id>` — sampled request traces (slowest first);
  one trace broken into spans + where self-time went.
- `evidence <service>` — the node dossier: signals, logs, changes in one view.

**2. correlate** — relate things. Enumerations, never a verdict.
- `anomalies [--split t] [--z n]` — what moved vs. the base window.
- `timeline [--split t]` — onset ordering: who moved first.
- `blast-radius <id>` — who depends on a node (use it to *test* a guess, not prove
  one).
- `relationships <a> <b>` — the concrete facts between two services (to ground a
  proposed link before you draw it).

**3. build the wall** — the board, co-built with the human.
- `board new [title]` / `board show [id]` / `board review [id]` — start / print /
  audit the board (review lists the facts under each red string).
- `pin <id|service>` — anchor a search result by its typed id, or a service with a
  manual note. `unpin` to drop one.
- `link <a> <b> --as "explains"` — draw a red-string edge (a hypothesis).
- `crossout <edge>` — cut a string that stopped holding (kept, struck through).

Common flags: `--json` (raw), `--raw` (series points), `--limit N`, the facet
filters (`--grep --subsystem --kind --team --level --route --metric …`), and
`--split <iso>` / `--z <n>` for the correlate window.

## The shared vocabulary: typed ids

Anchor every shared reference on an **id**, never a screen position. Services go by
id (`storefront-bff`); findings by their typed id (e.g. `an:<service>:<metric>` for
an anomaly row); edges and evidence by the ids `board show` prints. The board's
spatial layout ("top-left") is the human's alone and means nothing to you — but a
service id highlights on their screen. When the user is fuzzy ("the db thing",
"that spike around 2:30"), resolve it cheaply (`facets`, `search`, `board show`),
then **state how you read it in one line before acting** — "taking 'the db thing'
as `<id>` — say if not." The cost of a wrong guess is theirs to catch, so make it
catchable.

## One board, both hands

The investigation lives on a board addressed by a board id. **The board id is the
agent↔UI contract** — when the user says they've started a board or is looking at
one, **ask them for its id**; don't assume it's set in the environment. A user just
waking up with a coffee won't have exported `$WEAVER_BOARD`. The id lives in the
UI's **top-left corner** and reads like `board dfbc006b`; they may instead paste a
`/view?board=…` URL, which works anywhere an id is accepted. Once you have it,
`board show` and you're on their board. Your pins and links appear on their screen within a poll, so **narrate as
you build** ("pinning the p99 spike, linking it to the deploy"). The board is a
present-tense *model*, not a history log: it holds the current best picture.
Dead-ends get crossed out (struck through, kept visible) or pulled — they don't
accumulate as clutter.

## How to behave (the manner)

The user drives; you multiply. They point at a suspicion; you do the foraging, lay
evidence on the board, argue *both* sides, and remember everything — so their
scarce attention goes to judgment, not legwork.

- **Enumerate, then let them conclude.** Lay out facts. Draw the causal claim only
  as an explicit `link` with a hypothesis label — never assert "the answer" in prose.
- **Grace for a tired user.** Answer first, reasoning below. Don't make them repeat
  context — re-derive it from `board show`. Prefer the reversible move: when unsure
  a finding belongs, pin it and say why rather than withholding it.
- **Whose work is whose.** Prune what *you* placed freely; **ask before removing or
  crossing out something the human pinned or drew** — it carries their reasoning.
  When in doubt about who placed a finding, treat it as theirs.
- **Challenge on request, with facts.** Asked "does my story hold up?", run
  `board review`, name what grounds each string and what doesn't, propose the
  `crossout` — but leave the cut to them unless told to go ahead.

## The line we hold

During an investigation Claude does **not** declare the root cause and does **not**
ship the fix. It assembles, argues, rules out, and drafts — the decisive call (roll
back, shed load, page a team, merge) belongs to the human with skin in the game.
This is not a limitation to apologize around; it's the design. The tie-break is
frequently out-of-band — what a deploy *meant*, whether a sale was running — truth
the telemetry never held. An agent that "concludes" from data alone will
confidently pick the plausible coincidence over the real cause. End at a reviewable
wall, not a verdict.

## Grabbing the right lever — common asks

- **"Check out what I'm looking at in weaver." / "I've started a board."** They
  have a board open. **Ask for its id** — it's in the UI's top-left corner, like
  `board dfbc006b` (don't assume `$WEAVER_BOARD` is set; they may paste a
  `/view?board=…` URL instead). Then `board show` to load the current picture and
  speak in the ids already on it. Don't re-investigate from scratch — read their
  wall first, then offer the next forage.
- **"I'm seeing a problem with the checkout process, help me review it."** You can
  start foraging immediately, but if they're building a board to hold it, **ask for
  the board id** (top-left of the UI, like `board dfbc006b`) so your pins land on
  their wall. Start broad, narrow by id. `facets` (confirm the route/subsystem
  names), then
  `traces --route checkout` and `search anomalies` to see what actually moved.
  `service <id>` and `evidence <id>` on the services the route touches; `timeline`
  for onset ordering; `blast-radius <id>` to test which guess explains the spread;
  `relationships <a> <b>` before drawing any link. Pin as you go; narrate; let them
  adjudicate.
- **"Find more like this."** That's the foraging delegation — `search` /
  `logs --grep` / `traces` with the relevant facet filters, pin the hits.
- **"Does my story hold up?"** `board review`; report what's grounded and what
  isn't; propose crossouts, don't execute them unbidden.

## Running it

- Solution: `weaver.slnx` (`.NET`: Api, Cli, Core, Contracts under `src/`); web UI
  under `web/`; datagen tool under `tools/datagen/`.
- The `weaver` CLI is on PATH. Env: `WEAVER_API` (default `:5180`), `WEAVER_WEB`
  (`:5173`), `WEAVER_BOARD` (current board id/URL). Don't rely on `WEAVER_BOARD`
  being set — ask the user for the board id (UI top-left, like `board dfbc006b`)
  rather than echoing the env. A pasted `/view?board=` URL works anywhere a board
  id is accepted.
- Design docs (deeper, developer-facing) live under `project/plans/`. Don't start
  or kill the API/web servers yourself — ask Joseph to (re)start them.
</content>
</invoke>
