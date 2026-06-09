# weaver — the CLI (the agent's surface)

The CLI is how a coding agent (and a human) drives weaver. Its design
goal, above all else: **be the agent's working-memory prosthesis.** The
enemy is not missing data — it's context rot from drowning in it. Every
convention below keeps the investigation holdable in a small, refreshable
model, pulling detail only when a hypothesis demands it.

The CLI is a pure client of `Weaver.Api` (dogfooding — everything the
agent does is reproducible over HTTP; see `agent-workflow.md`). It speaks
only observation + enumerating primitives; it never names or composes a
cause (see `analysis-architecture.md`).

## Output conventions

### Native English, never glyphs

**Never hand the model a glyph to decode** — no Unicode sparklines
(`▇▁▂`), no bit-packed or base64 shape strings. For the same reason you
don't feed a model raw SAX: it must *translate* a non-literal symbol into
meaning, unreliably and at a token cost. Trajectories, levels, and shapes
are encoded in **words and numbers the model reads directly.** Plain
aligned tables are fine (they're text); decode-me encodings are not.

### Trajectory encoding (the sparkline replacement)

Any time series — a metric, an edge error rate, a log-template's rate, a
route's trace-error fraction — is characterized into two fields: a
compact **`shape_code`** and a one-line **prose** summary.

Algorithm, per series over the window:

1. **Normalize.** Scale samples to the series' own range for *shape*;
   separately compute *magnitude* vs the comparison base.
2. **Bucket.** Split the window into N time buckets (default ~8–12;
   `--buckets` to tune).
3. **Bucket stats.** Per bucket: central value, spread (for volatility),
   sample count (for gaps).
4. **Classify level.** Map each bucket to a small ordinal vocabulary:
   `baseline | low | normal | elevated | high | peak`.
5. **Classify slope** vs the previous bucket: `steady | rising | falling`,
   qualified `gradual | sharp`.
6. **Event words.** Detect features: `spike`, `dip`, `plateau`, `ramp`,
   `step-up`, `step-down`, `gap`, `volatile`, `recovery`.
7. **Run-length encode.** Collapse adjacent same-level buckets into one
   segment carrying its time span.
8. **Emit** `shape_code` + prose.

`shape_code` grammar: `level(timespan)` segments, bare event words
between, time anchored relative to incident onset:

```
baseline(0–30m) step-up high(30–60m)
normal(0–20m) volatile elevated(20–45m) spike peak(45–47m) recovery elevated(47–60m)
```

prose carries magnitude + timing + shape in a sentence:

> "Flat at baseline until T+30m, then a sharp step up to a sustained high
> plateau (~14× base) through the end of the window."

Summaries show `shape_code` (terse, scannable); drill-down (`service <id>`)
adds the prose.

### The rest

- **Summaries by default, raw on demand.** No command dumps a series
  unless asked. `--detail` / `--raw` / `--limit` opt into volume.
- **Relative time, not timestamps.** Everything anchored to onset
  (`T+30m`, `+45s`) — shorter and more meaningful than ISO strings.
- **Hierarchical rollup.** `--rollup subsystem|kind` to start coarse
  (9 rows, not 300) and drill the hot region.
- **Count-before-pull.** Large queries report size first
  (`~3,200 rows / ~8k tokens — narrow or --confirm`) so the agent never
  blows its own context by accident.
- **Stable short ids.** `t-9f3` (trace), `sel-2` (selection), `view-abc`
  (View). Reference them later instead of re-fetching or re-describing.
- **`--json` for chaining**, terse aligned tables by default (cheaper to
  parse and to hold than JSON).

## Progressive disclosure — a toolset that teaches itself

The agent should not preload the manual (that's context rot before it
starts). It learns levers as it reaches for them.

- **Consistent grammar so verbs are guessable.** If `metrics <svc>
  --base --window` exists, `logs <svc> --base --window` works the same.
  One shared flag vocabulary everywhere.
- **Layered help.** `weaver` → verbs with one-liners, grouped by the loop.
  `weaver <verb> -h` → terse flags + **one canonical example** (agents
  pattern-match examples faster than prose).
- **Inline "next move" hints (hypermedia for a CLI).** Every output ends
  with the moves it sets up — so the data carries its own navigation and
  the agent follows breadcrumbs instead of memorizing the surface:

  ```
  anomalies (vs pre-deploy): 6 movers
    payments-db    p99 ×14   onset T+30m   baseline(0–30m) step-up high(30–60m)
    payments-api   p99 ×6    onset T+31m   baseline(0–31m) ramp high(31–60m)
    ...
  → next: weaver service payments-db --base pre-deploy   (characterize a mover)
          weaver blast-radius payments-db                (does its downstream cover the alerts?)
          weaver timeline                                (who moved first?)
  ```

- **Dry-run + teaching errors.** `weaver select --explain "<expr>"` shows
  what a selector matches and how many *before* committing; a bad selector
  errors with the valid vocabulary nearby. The grammar is learned by
  cheap probing.

## Shared flag vocabulary

| flag | meaning |
|---|---|
| `--base <name>` | comparison base for the diff (pre-deploy / t-1h / yesterday / last-known-good) |
| `--window <range>` / `--since <rel>` | time scope |
| `--rollup <subsystem\|kind>` | aggregate granularity |
| `--buckets <n>` | trajectory resolution |
| `--limit <n>` | cap rows |
| `--detail` / `--raw` | opt into prose / raw series |
| `--json` | machine output for chaining |

## The common task set (mapped to the loop)

Grouped by phase; this is the verb surface the build targets.

**Observe**
| intent | verb | returns |
|---|---|---|
| orient | `overview --base` | the fingerprint: counts, earliest onset, degraded routes, new log-template count, where alerts fired |
| user-facing symptom | `routes` / `traces --status error` | degraded routes, edge symptoms |
| characterize one thing | `service <id> --base` | one-screen card: deps, Δ across signals (with `shape_code`), new log templates, trace participation |
| qualitative tell | `logs <id> --level error` / `trace <id>` | specific-vs-generic logs; where `self_ms` accrues |

**Correlate (enumerations, never verdicts)**
| intent | verb | returns |
|---|---|---|
| what moved | `anomalies --base` | enumerated deltas + `shape_code` |
| what moved first | `timeline` | onset ordering |
| test a hypothesis | `blast-radius <id>` | downstream reachable set (does it cover the alerts?) |

**Select & share**
| intent | verb | returns |
|---|---|---|
| carve a region | `select "<expr>"` / `select --explain` | a subgraph → becomes a View |
| pin / share | `pin --select … --base … --note …` | a durable View URL |
| resume shared state | `show <view-id>` | load a human's View into the agent's context |

Conspicuously absent: no `solve`, no `root-cause`. The verbs enumerate and
characterize; the conclusion is the agent's to assemble by triangulating
them — which is the demo.

## Coordinating with the human's paneled view

The human has spatial, parallel perception (graph + evidence + timeline
panels at once); the agent has linear context. The **View/URL is the
shared reference frame** (see `view-model.md`, `web-ui.md`).

- **Same ids on both surfaces.** The UI labels nodes/edges/traces with the
  *exact ids* the CLI speaks — the non-negotiable foundation of "are we
  looking at the same thing." "That red node top-left" → they read me its
  id; `payments-db` → they see it highlighted.
- **Bidirectional handoff.** Agent finds something → `pin` → URL → their
  panel jumps there. They're looking at something → hand the agent the URL
  → `show view-abc` loads their exact (selection, projection, base) into
  the agent's context.
- **The View is a shared whiteboard.** Its `narrative` is where the agent
  writes *why* it's showing this; the human reads it in the panel,
  annotates, and the agent reads theirs back. Async and durable
  (Slack/ticket) — no co-presence required.
- **Complementary division of labor:** the agent scans breadth and
  triangulates fast, then pins a tight View; the human brings spatial
  judgment to that frame and pushes back.
