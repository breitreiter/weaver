# weaver — time windows as tracked board elements

> Design note (no build steps). Grows out of the hover-sync / global-filter
> contemplation on 2026-07-04. **Supersedes `agent-sql-charts.md` decision 2**
> (snapshot-not-live) *for charts*: charts now **re-derive** against the selected
> window; the pin-time snapshot demotes to a default-window cache. Couples to that
> plan's deferred "canonical view catalog" (`demo-vs-production.md`).

One line: **a board tracks a small set of named time windows — the moments and
spans an incident actually turns on — and every chart locks to the currently-
selected one.** Selecting a window re-scopes the evidence panel to that slice of
time. The board stops being a pile of timestamped evidence and gains a **time
spine**: an explicit, pivotable sequence of the instants that matter.

## Why (the payoff)

- Kills the "wait — what period is *this* chart from?" thrash. Every chart on
  screen shares one named, visible window.
- Surfaces the investigation's temporal structure overtly: *here are the three
  moments that matter — deploy → onset → saturation — pivot between them.* That
  structure is latent in the data today and lives only in the responder's head.
- A window becomes an addressable object — `@`-referenceable in the document
  ("by `@t:saturation` the pool was exhausted"), the way typed finding-ids
  anchor facts. The narrative gains **time anchors**.
- Makes synchronized hover nearly free: the selected window *is* the shared time
  domain the cross-chart cursor needed. Locked charts + one domain → the cursor
  sync falls out (see "sibling threads").

## The three locked decisions

### 1. Selection is *view* state; the window *set* is board content. Claude authors windows, for now.

The tracked windows are shared board data. But *which* window you're looking at is
**per-viewer** — it lives in the URL (`?window=<id>`), surfaced as a **drop-down in
a sticky header atop the evidence panel**. So the agent pivoting to the onset never
yanks the human's screen; each navigates the shared set independently. Same contract
as `?focus=<service>`: shared content, private viewport.

Windows are **authored by Claude only** in this pass — exactly like the agent SQL
charts (`agent-sql-charts.md`). The agent proposes the moments; the human reads and
adjudicates. A real *human* authoring affordance is deferred and explicitly **hard**:
it can't be a lone drop-down — it has to integrate cleanly with search (drag-select a
range?), the evidence drawer, and the narrative account as one coherent time gesture.
Out of scope here; named, not forgotten.

**Windows are `@`-markable; double-cite when the window is load-bearing.** A window is
a first-class citeable object in the document — prefix **`t:`** (time), *colon*-
separated to match the existing id grammar (`an:` / `ch:` / `tr:`), **not** dotted:
`@t:first-sign-of-trouble`. When a claim's truth depends on *when* you look, compose
the two refs: *"`@ch:web-gateway:latency` at `@t:first-sign-of-trouble`."* This is how
citation stays grounded under re-derivation (decision 2): a bare `@ch:` is window-
*relative by design* — it renders at the current selection — and binding it to a `@t:`
pins the moment the claim was true. The **author** carries that judgment: bind the
window when it matters, leave it bare when it doesn't — consistent with the human
owning synthesis. This promotes the doc→window pivot from "open question" to
**needed**: clicking a `@t:` ref selects that window, which re-scopes the charts.

### 2. Charts re-derive against the selected window (path a) — not scene-swapped (path b).

Picking a window re-runs each time-scoped chart bound to that window, so *the same
chart* shows any moment. That's the pivot magic, and it keeps the evidence unified
rather than fragmenting into per-moment dashboards — the rejected alternative **(b)**,
where a window *is* a scene owning its own charts. (b) was snapshot-safe but
splintered the evidence and multiplied authoring; (a) wins for a unified board.

The cost, recorded honestly: **(a) reverses `agent-sql-charts.md` decision 2.** A
chart's live source becomes its stored `sql` (re-run on window change, sandbox-
bounded, cached per `(chart, window)`); the pin-time snapshot demotes to the
default-window render / fallback. Crucially, re-derivation fires on the **deliberate
act of selecting a window**, *not* on the 2 s board poll — so arbitrary SQL still
never rides the poll hot path, which was the whole reason for the snapshot rule. That
invariant holds.

### 2a. The frame contract, simplified by chopping: every chart is a time-series.

Re-deriving a chart for a new window means the runtime must know *how* the window
applies. Rather than handle the awkward cases with cleverness, we **chop** them
(resolved 2026-07-04). The contract is uniform and **mandatory** — no per-chart
opt-out, so every chart responds to the drop-down predictably (no "why didn't *this*
one move?"):

- **x is always time.** No categorical-x charts. Categorical data moves from the
  *axis* to *series* — "avg p99 by service" becomes "p99 over time, one line per
  service" — which is usually the better incident viz anyway (trajectory + onset, not
  just a magnitude ranking).
- **Windowing rides the Grafana macro idiom, not a bespoke convention.** Every chart's
  SQL uses `$__timeFilter(ts)` / `$__timeGroup(ts, interval)`; the sandbox expands
  them against the selected window at exec time. This is a syntax Claude already knows
  cold (see "Prior art"), and it covers both the plain filter and the bucketed-
  aggregate cases with no special-casing.
- **No time axis ⇒ not authorable (for now).** A chart that can't put time on x simply
  can't be made until the deferred **stacked / grouped-bar** work — which is *still*
  time-x (time buckets on x, category as the stack), so it's a render mode, not an
  axis-model exception. The categorical "L" is smaller than it looks: you lose
  categorical *axes*, not categorical *data*.

This retires the earlier "declare a time column / outer-wrap-and-clip / window-
invariant" trichotomy — there is now **one shape**, which is what makes "every viz
must accept a time window" enforceable instead of a nest of strategies.

**Consequence, recorded honestly:** this **deprecates the categorical bar shipped in
`agent-sql-charts.md`** (the demoed "p99 by service" bar). Not removed today — but
under this design new charts are time-x only, and that bar becomes legacy/deferred.

> **The mechanism is Grafana's, not ours.** See "Prior art": `$__timeFilter` /
> `$__timeGroup` is the decade-old idiom; borrow it whole. The residual risk isn't the
> mechanism, it's blast radius — but chopping to time-x-only *is* the mitigation.

### 3. Windows are seeded explicitly and kept few — an AI-curation discipline.

Windows do **not** auto-populate from every timestamp. Claude seeds them deliberately,
each grounded in a fact already on the board — an anomaly's onset, a deploy/change
event, the span between two of them. The restraint is the whole point: *"here are the
three key moments,"* never *"I made twenty views from twenty log lines."* A window
earns its place only by being a pivot the investigation actually turns on. This is the
same rule as the board itself (`weaver-board-is-model-not-history`): windows are the
moments that matter *now*, pruned like any stale line — not accreted as history.

### 4. Liveness reversal for charts — accepted with eyes open.

Adopting the Grafana macro idiom means adopting its **live semantics** for charts: a
chart's truth is "as of the selected window," re-run on selection. That is a
deliberate reversal of the snapshot promise *for charts* — the snapshot demotes to a
default-window cache. Every *other* evidence kind stays snapshot. Citation stability is
recovered not by immutability but by **double-citing** (decision 1) — a claim that
depends on the moment names its `@t:` window.

### 5. Under-engineer the backend for the demo — lean on the hardware.

The re-derive cost (N charts × a sandbox exec per pivot, worst under rapid
window-scrubbing) is **explicitly punted** for the demo. A monster box — FLOPs,
64 GB RAM, M.2 SSD — against a hella-smol dataset absorbs re-exec-per-pivot with no
cache tier and no debounce cleverness. The honest caveat: `(chart, window)` caching +
invalidation becomes real the day telemetry actually ingests (data stops being
immutable). A `demo-vs-production.md` line, not a today problem.

## The window, as an entity

A tracked board element:

- **typed id** `t:<label>` — `@`-markable in the document. Prefix `t:` (time), chosen
  over `win:` — shorter for something cited often in prose. **Colon**-separated, like
  every other id; not dotted.
- a **label** — "onset", "saturation", "the leak" — is the id body (`t:onset`).
- a **range** `[from, to]` — a *moment* is a narrow/padded span, a *span* is a wide
  one. One shape, not two kinds.
- optional **grounding** — the evidence/event it was seeded from (an onset ts, a
  `ce:` change), so a window is anchored to a fact, not a floating timestamp.

Selection state is **not** on the entity — it's the viewer's URL.

## Degrades to the simple case (don't tax n = 1)

A board with exactly one window *is* the plain global time-filter from the earlier
contemplation. A fresh board carries one default window — the full telemetry range,
or the range of the pinned evidence — and the sticky-header drop-down stays quiet
until the investigation grows a second window. Tracked-windows is the **generalization
of the global filter, not a competing feature**: the global filter is its n = 1.

## The trade we're consciously making

A single selected window can't show a slow-burn trend (mem leak, hours) and an acute
spike (crash, seconds) *at the same time* — different timescales want different
windows. We accept it: you **pivot** between the leak-span and the crash-moment rather
than juxtaposing them. The story that spans both — "the leak caused the crash" — is
told in the **document's prose**, grounded by `@t:leak` and `@t:crash`, not forced
onto one axis. Simultaneous multi-timescale display is a non-goal.

## Prior art — the data model is a known pattern, not a hack

The instinct that "declare a time column and inject the window into arbitrary SQL"
feels hacky is worth checking — and the check is reassuring. It's the **exact model
every SQL-backed dashboard tool has shipped for a decade.** Adopt it; don't reinvent.

- **Grafana SQL datasources — `$__timeFilter(col)` / `$__timeGroup(col, interval)`.**
  You write arbitrary SQL and mark the time column; at query time Grafana expands the
  macro into `WHERE col BETWEEN <from> AND <to>` (and `$__timeGroup` into a bucketed
  `GROUP BY` bound to the range). This **is** decision 2a — and it dissolves the
  awkward two-mechanism split cleanly: **`$__timeFilter` is the clip case,
  `$__timeGroup` is the aggregation-pushdown case.** Not two hacks — two named macros
  every Grafana user already knows.
- **Superset — `get_time_filter` (Jinja) + the `is_dttm` column flag.** Same shape: a
  column is *declared* as the datetime axis (`is_dttm=1`), and a macro yields
  `from_expr` / `to_expr` / `time_range` to drop into the query. "Declare the time
  column" is literally how Superset marks a windowable axis.
- **Shared time range as the dashboard default.** "All panels respect the time
  selection; the data in every panel reflects that window" is the *industry default*,
  not a novelty. Decision 1 (one selected window locks all charts) is the worn path.

**Recommendation:** adopt the **Grafana macro idiom** (`$__timeFilter` /
`$__timeGroup`, or the Superset equivalent) as the frame contract instead of a bespoke
weaver outer-wrap. Three reasons it de-risks us:
1. proven at enormous scale;
2. it natively covers *both* the clip and the aggregation-pushdown cases (the thing a
   naive outer-wrap-and-clip can't);
3. **charts are agent-authored, and Claude knows Grafana macros cold** from training —
   so the agent writes a syntax it's fluent in, not a weaver invention it will fumble.
   Lower error rate for free.
The sandbox still owns expansion (macro → bound SQL → read-only exec + row/time caps);
we borrow the *idiom*, not a live DB connection.

**The one limit that's fundamental** (so our scoping is *right*, not lazy): Vega-Lite
— the most principled multi-view tool there is — documents that shared-scale sync
**breaks when the domain fields differ**. So "only time-typed charts on a normalized
domain participate in the shared window/cursor" isn't a weaver compromise; it's a wall
even grammar-of-graphics hits. Heterogeneous x-axes can't be synchronized by anyone.
Scope to a normalized time domain and stop worrying about it.

## Resolved 2026-07-04 (was: open risks)

- **Frame-contract blast radius** → resolved by *chopping* to time-x-only + mandatory
  windowing (2a). One shape, so there's no trichotomy to size. The categorical case is
  deferred to stacked/grouped-bars (still time-x).
- **Citation stability under re-derivation** → resolved by making windows `@t:`-markable
  and double-citing (`@ch:… at @t:…`, decision 1). A bare chart-cite is window-relative
  by design.
- **Re-derive latency & caching** → punted for the demo; hardware absorbs it (decision
  5). A real concern only once telemetry ingests.
- **Doc → window pivot** → promoted from "maybe later" to **needed** (decision 1): a
  `@t:` click selects the window.
- **Non-chart evidence does NOT scope to the window** (resolved 2026-07-04). Only
  charts re-derive. This draws a clean conceptual line: **charts are *continuous*
  series — a window is a lens over them; instant events (logs / anomalies / traces /
  changes) are *discrete facts* — they either fall inside a window or they don't, and
  nothing about them "re-derives."** Future escape hatch (not a blocker): cross-link
  instant events to the windows they fall within — pure display/annotation
  (highlight the pins inside the selection), never re-execution. Keeps the snapshot
  model intact for everything that isn't a chart.

## Still open

- **Human authoring.** The one deferred hard problem — a human time gesture integrated
  across search + evidence drawer + narrative. Not a drop-down. Unpacked below.

## The deferred human-authoring problem (why it's hard, not just unbuilt)

For the demo, Claude authors windows (like charts) and the human curates by selecting
/ pruning. A real *human* create-a-window affordance is deferred because it isn't one
missing button — it's genuinely cross-cutting:

- **A window needs two things at once that no single control captures: a *range* (two
  timestamps) and a *grounding* (the fact that makes the moment meaningful).** Claude
  does both in one reasoning step — reads an onset, decides the span, names it, links
  the evidence. A human clicking has to find the moment, set *both* boundaries, name
  it, and ideally tie it to a fact — a multi-step gesture with no natural single home.
- **The three surfaces that each have a claim only hold half of it.** *Search* has the
  timestamps but no time axis to brush on (it's a list). The *evidence drawer* owns the
  selected window but not the motivating data. The *document* can cite `@t:` windows but
  can't define a time range from a text cursor. A good affordance has to stitch one
  coherent "select a span, name it, ground it" gesture across all three — or crown one
  as canonical and route the rest to it.
- **Chicken-and-egg.** The gesture that would make it natural — *brush-select on a
  shared time axis* — only exists *because* this feature introduces the shared axis. So
  human-authoring is downstream of the very thing being built.
- **Restraint doesn't come for free.** Claude keeps windows few and meaningful by
  judgment (decision 3). A "new window" button invites the 20-junk-windows mess that
  decision 3 exists to prevent; a human UI would have to actively encourage
  few-good-windows.

**The likely reframe when it's picked up:** the human's real action is probably
*curation, not authoring* — rename / delete / nudge-the-boundaries on Claude's seeded
windows (a light edit) rather than net-new creation from scratch (heavy). That fits
weaver's human-drives-Claude-multiplies grain and shrinks the UI problem
considerably. Worth trying that framing before building a full authoring surface.

## Relationship to sibling threads

- **Synchronized hover cursors** — subsumed as a near-free follow-on: the selected
  window is the shared domain; locked charts + one domain → the cross-chart cursor.
  The established names for the interactions: Vega-Lite's `bind: "scales"` (shared-
  scale sync across views) and the **"overview + detail" brush** (drag-select on one
  chart drives the others' domain) — i.e. the earlier "drag a spike to narrow" pro is
  a named, standard pattern, not something we're inventing.
- **Global time-range filter** — subsumed as the n = 1 case.
- **`agent-sql-charts.md`** — decision 2 (snapshot-not-live) superseded for charts;
  the frame contract (2a) couples to that plan's deferred canonical view catalog.
