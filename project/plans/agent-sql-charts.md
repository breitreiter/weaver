# weaver — agent-authored SQL charts

> **Supersedes the deferred `chart-wall.md`.** That doc cut the human
> "add to chart wall" builder and folded charts into node-tied evidence —
> both decisions stand here. What's new: the charts are **authored by the
> agent from raw SQL**, not assembled by a human from archetype buttons.

The feature in one line: **Claude writes a SQL query against the read-only
telemetry DB, snapshots the result, and pins it to the board as a chart
that renders in Recharts on the web and as a prose table in the CLI.** No
human UI for building complex charts — humans still pin simple metrics.

## The three decisions (locked)

1. **Raw SQL, kept loose — not a query-builder abstraction.** We trade a
   battle-hardened view catalog for open-ended reach, because we don't yet
   know what explanations the agent will invent. The named risk (agent
   writes inaccurate SQL → misleading chart) is logged in
   `demo-vs-production.md` as an *accepted* demo tradeoff; the canonical
   view catalog is deferred to post-trial (3–6 months). Not today.
2. **Snapshot, not re-run-live.** The result rows are captured into the
   pinned evidence `Payload` at pin time (matching how every other pin
   snapshots its justifying payload). Keeps arbitrary SQL off the
   board-poll hot path. Re-run only on an explicit refresh.
3. **Everything moves to Recharts.** One styling surface. The existing
   hand-rolled `MetricSparkline` (and, where cheap, `TraceMini`) are
   re-skinned onto Recharts so pinned simple metrics and agent charts
   share one visual language. This is a deliberate reversal of
   `graph-redesign.md`'s "toward hand-rolled SVG" direction, made because
   the demo values a single consistent chart aesthetic over avoiding the
   dependency.

## Scope boundaries

- **Web only.** The CLI never renders a visual chart — the no-glyph rule
  (`cli.md`) stands. CLI shows the chart's result as a small prose table +
  the `ch:` id so the agent can sanity-check the numbers. CLI = the
  numbers, web = the visual. (Same split the trajectory encoding already
  uses.)
- **No human chart builder.** Humans pin *simple metrics* (one gesture,
  already exists → renders as a Recharts mini-line). Complex/novel charts
  arrive *only* via the agent's SQL path. This is already the design
  direction, not new work — just a constraint to honor.

## The security sandbox (the risk-bearing piece — build & test first)

Raw SQLite has a real but bounded DoS/abuse surface. Close it with a
dedicated hardened exec path — a well-trodden "read-only SQL console"
pattern:

| Risk | Guard |
|---|---|
| Writes / DDL | dedicated `Microsoft.Data.Sqlite` connection, `Mode=ReadOnly` + `PRAGMA query_only=ON` |
| `ATTACH` (e.g. to `boards.db`), multi-statement tricks, `PRAGMA` | **single-statement gate**: exactly one `SELECT`/`WITH`, reject stray `;` — kills ATTACH/PRAGMA/multi-stmt by construction |
| `load_extension` | leave disabled (Microsoft.Data.Sqlite default) |
| runaway scan / recursive-CTE bomb (the real DoS) | **wall-clock cancel**: timer → `SqliteCommand.Cancel()` (`sqlite3_interrupt`) at ~2s |
| unbounded result rows → memory | read at most N rows (~5,000), then stop |

Output is `{ columns, rows }`. Nothing interpretive — pure enumeration,
consistent with `analysis-architecture.md`.

## The chart artifact

A chart is a new **evidence kind**, reusing the entire pin → board →
`@`-ref → document pipeline (no new plumbing):

- **Kind** `chart`, hung on a node like any evidence (anomalies already
  hang on a subject). Cross-node/fleet charts attach to the most relevant
  subject the agent picks.
- **Typed id** `ch:<...>` — needs a result-builder + `/api/search/resolve`
  case in `Program.cs`, and a prefix in the CLI's `IsTypedId`.
- **Payload** (`EvidenceEntity.Payload`, JSON snapshot): `{ sql, title,
  type: line|bar|area|scatter, xColumn, yColumns[], columns, rows }`.
  `sql` is kept for provenance/refresh; `rows` is the snapshot that
  renders.
- **Rendered** in `web/src/Evidence.tsx` alongside the (now Recharts)
  metric card, keyed off `type`. `@`-referenceable in the doc exactly like
  any finding — the autocomplete is already generic over `refId`.

## Command surface

- **CLI** `weaver chart --sql "<q>" --title "<t>" [--type line|bar|…]
  [--x <col>] [--y <col,col>] [--pin <service>]` → runs via the sandbox
  endpoint, prints the result as a prose table + the `ch:` id, and (with
  `--pin`) snapshots it onto the board. Mirrors the existing `pin` flow.
- **API** `POST /api/charts/exec` (sql, title, render spec) → sandbox →
  `{ columns, rows }`. Pin lands through the existing `POST
  /api/boards/{id}/pin` with kind `chart`.

## Files touched

- `src/Weaver.Api/Program.cs` — sandbox exec path, `/api/charts/exec`,
  `ch:` result-builder + `/resolve` case.
- `src/Weaver.Core/BoardEntities.cs` — `chart` kind (no schema change if it
  rides `Kind`/`Payload`; confirm no enum constraint blocks it).
- `src/Weaver.Contracts/Dtos.cs` — chart exec req/resp + render-spec shape.
- `src/Weaver.Cli/Program.cs` — `chart` verb, `IsTypedId` `ch:` prefix,
  prose-table render.
- `web/src/api.ts` — `charts.exec` binding (+ the already-unused
  `histogram` one if we want volume charts).
- `web/src/Evidence.tsx` — Recharts renderer; re-skin `MetricSparkline`.
- `web/package.json` — Recharts `^3.8.1` already present; just import it.

## Build order & status

1. Recharts wired in; `MetricSparkline` re-skinned onto it (one styling
   surface). **S** — *not started.*
2. ✅ **DONE** (commit `05b69f8`) — hardened SQL sandbox + `/api/charts/exec`.
   See "Status: where we are" below.
3. ✅ **DONE** (uncommitted — held per odd-hours rule) — `chart` evidence kind +
   `ch:` typed id, end to end (entity → DTO → resolve → IsTypedId). Verified live.
   See "Status" below.
4. ✅ **DONE** (commit `0029a6a`) — `weaver chart` CLI verb (prose table +
   `--pin`). Verified live. See "Status" below.
5. ✅ **DONE** — Recharts renderer for pinned chart evidence in the web +
   `MetricSparkline` re-skinned onto Recharts. Verified in-browser. See "Status".

**The feature is complete** — agent authors SQL → CLI table + `ch:` pin → web
Recharts render, end to end.

## Status: COMPLETE — all five steps landed and verified

The full feature works end to end: the agent writes SQL → `weaver chart` runs it
through the read-only sandbox and prints a prose table + the `ch:` id → `--pin`
snapshots it onto the board → the web renders it in Recharts, `@`-referenceable in
the doc like any finding. Steps 2–4 are committed (`05b69f8`, `0029a6a`); step 5's
web changes are the last piece. What remains is all in "Deferred / out of scope"
below — nothing blocking.

**Step 2** — shipped:
- `src/Weaver.Core/SqlSandbox.cs` — `SqlSandbox.Run(sql, dbPath?, timeoutMs, maxRows)`
  → `SqlResult(Columns, Rows, Truncated)`; throws `SqlSandboxException` on any
  rejected/failed query. All five guards in place.
- `tests/Weaver.Core.Tests/SqlSandboxTests.cs` — 24 tests, green in ~560ms.
- `POST /api/charts/exec` (`Program.cs`, after the histogram endpoint) —
  `ChartExecReq{ sql, timeoutMs?, maxRows? }` → `ChartExecDto{ columns, rows,
  rowCount, truncated }`; bounds clamped to the sandbox limits. DTOs in
  `Dtos.cs` (just above the node-evidence dossier records).
- Verified live vs `data/weaver.db`: happy-path GROUP BY over `metric_samples`
  ok; `DROP`/multi-statement → 400; runaway recursive-CTE cancelled in ~0.8s
  at an 800ms budget.

**Gotcha banked (don't relearn it):** `SqliteCommand.Cancel()` is a **no-op**
in Microsoft.Data.Sqlite and `CommandTimeout` only bounds lock-waits — neither
stops a running scan. The wall-clock cancel MUST use the native
`sqlite3_progress_handler` (runs on the query thread). A first cut using
`Cancel()` spun a CPU core for 15 min. When running tests that can execute
arbitrary SQL, wrap the run in `timeout --signal=KILL <n>` as a backstop.

**Loose end (not ours, worth a bump):** NU1903 high-severity advisory on
`SQLitePCLRaw.lib.e_sqlite3 2.1.11`, transitive via EF Core 10.0.8.

### Step 3 — DONE (both open questions resolved)
Both open questions are answered, and the resolution shifted one thing from the
original sketch — **a chart is authored, not found**, so it's minted at exec time,
not rebuilt by `/resolve` from telemetry:
- **Id shape (Q1 resolved):** `ch:<serviceId>:<title-slug>` — mirrors
  `an:<subject>:<aspect>` exactly, so it rides the existing pin dedup
  (service+kind+aspect+at) and stays human-`@`-referenceable. Slug = title
  lowercased, non-alphanumeric→`-`, collapsed, capped at 40. (Not a hash — hashes
  aren't referenceable in prose.)
- **Subject node (Q2 resolved):** the agent picks it explicitly (`--pin <service>`
  / `subject` on the exec req). No auto-derivation. Fleet charts fall back to
  `(fleet)` in the id with empty `nodeIds` (the pin endpoint already defaults
  empty→`(fleet)`).
- **Where it's minted:** `POST /api/charts/exec` now takes an optional render spec
  (`title, subject, type, xColumn, yColumns`); when `title` is present it builds a
  pinnable `Result` (`SearchResultDto`) via `ChartResult(...)` — byte-identical to
  what a future UI "author chart" gesture pins. No title → `Result: null`
  (backward-compatible with step 2). This is the seam a chart is born at.
- **`/resolve` case:** `ch:` returns a teaching 400 ("authored, not resolved — use
  `weaver chart …`"), NOT a telemetry lookup — there's no row to rebuild from and
  we snapshot rather than re-run (decision 2). This corrects the original
  "needs a result-builder + resolve case" sketch: the builder lives at exec, and
  resolve only teaches.
- **Files touched:** `Dtos.cs` (`ChartExecReq` +spec fields, `ChartExecDto`
  +`Result?`); `Program.cs` (`ChartResult`+`Slug` builders, exec wiring, type
  guard, `ch:` resolve case, `EvidenceSummary` `chart` case); `Cli/Program.cs`
  (`ch` in `IsTypedId`). No schema change — `chart` rides the plain `Kind` string.
- **Verified live** (board `672c2494`, since abandoned): mint→pin→`board show`
  prints `chart … <title> · bar · 5 rows  @ch:checkout:samples-by-service`;
  `pin ch:…` and `/resolve` both hit the teaching error; bad type → 400; `@`-ref
  lands in the doc.

### Step 4 — DONE (`weaver chart` CLI verb)
- **Verb** `weaver chart --sql "<q>" --title "<t>" [--type line|bar|area|scatter]
  [--x <col>] [--y <col,col>] [--pin <service>] [--json]` — dispatch case + `Chart()`
  in `Cli/Program.cs` (next to `Pin()`), help entry added.
- **Flow, as built:** POST `{sql, title, subject:--pin, type, xColumn:--x,
  yColumns:--y}` to `/api/charts/exec`; print the table via a new `PrintTable`
  (numbers right-aligned, cells capped at 40, `truncated` noted) + the `ch:` id from
  `Result.id`. `--pin` does double duty — it's the subject node AND the save intent;
  it POSTs `Result.pin` to `/api/boards/{id}/pin` (same shape as `Pin()`). Board is
  resolved up-front when pinning so a missing board fails fast. Without `--pin` it's a
  dry-run: table + a `ch:(fleet):<slug>` preview id, nothing saved.
- **Grace:** `--pin` soft-resolves a typo'd service (did-you-mean, like `pin`); a
  cross-node subject that isn't a real service still passes through.
- **Cells** arrive as `JsonElement` (raw SQLite number/string/null) → `Cell()`
  renders them literally; a column is right-aligned iff every cell is a Number.
- **Verified live** (board `8e3e496e`): missing-`--sql`/`--title` guards; dry-run
  table + id; `--pin` → snapshot → `board show` shows `chart … · bar · 5 rows` +
  `@ch:…`; bad type + write-SQL both surface the server/sandbox 400; `@`-ref lands in
  the doc. Full suite green (31 tests). No new API/DTO work — CLI is a thin relay.

### Step 5 — DONE (Recharts web renderer)
Web-only, no backend touch — the snapshot already reached the client
(`EvidenceItem.payload`, `web/src/api.ts:43`).
- **Render hook:** `web/src/Evidence.tsx` — sibling to the metric sparkline:
  `{ev.kind === 'chart' && <ChartEvidence payload={ev.payload} />}`.
- **`ChartEvidence`:** reads `type` (line|bar|area|scatter), `xColumn`, `yColumns`,
  `columns`, `rows`; maps rows→`{col: value}` records; renders the matching Recharts
  primitive in a `ResponsiveContainer` (card-sized, 160px). Nothing computed — the
  rows are the query's. y defaults to every non-x **numeric** column when the agent
  named none (a column is numeric iff every non-null cell is a number).
- **Colours:** the dataviz skill's validated categorical palette (blue/aqua/yellow/
  green/violet/red), validated for CVD on the dark card surface `#232734` (worst
  adjacent ΔE 10.3 — floor band, mitigated by the always-on legend for ≥2 series;
  the app's own kind hues FAILED the check — log-gray reads gray). One shared y-axis
  (never dual), recessive dashed grid, hover tooltip, legend only for ≥2 series
  (single series → the card title names it). Card border `--k` = series-1 blue
  (`#3987e5`), new `kind-chart` token in `App.css`.
- **`MetricSparkline` re-skinned** (`Evidence.tsx`) from the hand-rolled SVG polyline
  onto a Recharts mini `LineChart` — decision 3, one styling surface. Deliberate
  reversal of `graph-redesign.md`'s hand-rolled-SVG direction, for the demo.
- **`TraceMini` NOT re-skinned** — it lives outside the pinned-chart path (search
  results, different data shape), so "where cheap" (decision 3) didn't apply; left
  as hand-rolled SVG. Noted so it's a choice, not an oversight.
- **Verified in-browser** (board `b49432ac`, headless Chrome): a single-series bar
  (checkout-api p99) and a 2-series line with legend (orders-api p50 vs p99) both
  render clean — axes, grid, tooltip, legend, no collisions. `tsc` + `vite build`
  green. The larger JS bundle (Recharts) is the accepted decision-3 dependency cost.

## Deferred / out of scope (named, not forgotten)

- **Canonical view catalog** — the production hardening for the inaccurate
  -SQL risk; revisit after real user trials (`demo-vs-production.md`). **Note:**
  `board-time-windows.md` may be the forcing function that pulls this forward —
  making charts re-derivable per selected window (its decision 2/2a) bends the
  "any SQL at runtime" model toward a windowable **frame contract**, which is a
  step toward this catalog.
- **Snapshot-not-live (decision 2) — SUPERSEDED for charts by
  `board-time-windows.md`.** Charts there re-derive against the board's selected
  time window (re-run on window *selection*, not on the poll); the pin-time
  snapshot demotes to a default-window cache. The snapshot rule still holds for
  every other evidence kind.
- **Render set narrowed by `board-time-windows.md` to time-x-only.** The shipped
  `line | bar | area | scatter` becomes the PerfStack-derived canonical set:
  **line + count-bar** (area = a line fill). **`scatter` is CUT** (numeric×numeric
  has no time axis — violates x-is-always-time) alongside the categorical bar
  (→ count *by time bucket*, not by category). `state-line`, `stacked-bar`, and
  `heat-line` are deferred. See that plan's "render-mode set (2b)".
- **Re-run-live charts / refresh-on-poll** — snapshot only for now.
- **Cross-node "chart wall" as a fourth pane** — charts stay node-tied
  evidence (`chart-wall.md`'s folding decision holds).
