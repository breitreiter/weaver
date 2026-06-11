# weaver — CLI as co-researcher (alignment plan)

Update the CLI so Claude (opus/sonnet over bash) shares the user's conceptual
model and the two can converse productively about the evolving board. Supersedes
the View-era verb tables in `cli.md` (output conventions there still stand).

## The working relationship this serves

The user engages Claude Code as a **co-researcher** on the same investigation:

1. **Delegate complex unstructured tasks** — "figure out why checkout is slow,
   put what you find on my board."
2. **Confirm / challenge findings** — "I think the deploy did it — check me."
3. **Delegate tedious tasks** — "find other log entries like this one."

And the user may be mid-incident: tired, distracted, referring to things
elliptically ("the db thing", "that red line", "the spike around 2:30").
**Grace is a design requirement** — Claude spends its interpretation budget on
the user's meaning, so the CLI must never make it also fight the tooling. Every
fuzzy reference should be resolvable in one cheap probe.

## The alignment contract (what "shared conceptual model" cashes out to)

- **Same lens.** Every query the user can run in the left panel has a CLI
  equivalent with the same scopes, facets, caps, and sort order.
- **Same vocabulary.** Every noun on the user's screen has a CLI-speakable
  name: services by id, evidence by kind+aspect, edges by id *and* by endpoint
  pair, search results by their typed id.
- **Same words.** A pinned card is summarized identically on both surfaces —
  one renderer, server-side, so Claude and the user describe a card in the
  same sentence.
- **Symmetric hands.** Anything the user can do to the board (pin, link,
  cross out, prune), Claude can do, and vice versa. Append-only collaborators
  aren't peers.

## Phase 1 — `weaver search`: forage parity *(the big one)*

A `search` verb over `/api/search` + `/api/search/facets`, mirroring the UI
exactly (same six scopes, same facets per scope, same cap + sort):

```
weaver search anomalies --subsystem payments --z 2 --from 14:00 --to 15:00
weaver search logs --grep "pool timeout" --level error --service payments-db
weaver search traces --route checkout --status error
weaver facets                      # what subsystems/levels/routes exist?
```

- Output follows `cli.md` conventions: aligned table, shape codes for metrics,
  honest cap line ("top 60 by duration — more exist", mirroring the UI's
  result-count header), `--json` for raw.
- **Every row prints its typed result id** (`an:payments-db:latency_p99`,
  `tr:9f3a21`, `log:8c…`, `me:…`, `ce:…`, `svc:…`) — the shared handle (Phase 2).
- Inline next-move hints include the pin command for the top row.
- The old per-type verbs (`anomalies`, `logs`, `traces`, …) stay as thin
  aliases into the same code path — muscle-memory friendly, one lens.

## Phase 2 — typed result ids as the shared currency

`pin` accepts a search-result id and resolves the **full typed payload** the
UI would have pinned — no more hand-threading `--evidence '<json>'`, no more
CLI pins that render as blank cards:

```
weaver pin an:payments-db:latency_p99      # pins exactly what the UI card would
weaver pin tr:9f3a21                       # trace: all participants + slice
```

- Implementation: one small API addition, `GET /api/search/resolve?id=<typed-id>`
  → the `SearchResultDto` (ids are self-describing; logs/changes/traces resolve
  by row id, anomalies/metrics by re-running the scoped analysis). The CLI then
  posts the result's own `pin` target, byte-identical to the UI path.
- Bare `pin <service>` and `--as/--aspect/--note` stay for quick manual pins.
- **Coupled UI touch:** show the typed id on each result card (small, mono,
  copyable) and on evidence cards — so the *user* can speak the handle too
  ("pin an:payments-db:latency_p99" pasted into chat just works).

## Phase 3 — board readback with handles (confirm/challenge, part 1)

`board show` becomes the faithful mirror of what the user sees:

- **Print every id**: edge ids next to each red string, evidence ids next to
  each card. (Today `crossout` demands an edge id that no human-readable
  output ever prints — the demo's payoff is unreachable without `--json`
  spelunking.)
- **Same words**: move the per-kind evidence one-liner (`summarize()` in
  `Evidence.tsx`) server-side — a `summary` field on `EvidenceItemDto` that
  both the UI and `board show` print verbatim. One renderer, zero drift.
- `link` / `pin` echo the id they created.

## Phase 4 — grounding verbs (confirm/challenge, part 2)

The verbs Claude needs to *check* a hypothesis rather than assert one:

- `weaver relationships <a> <b>` — the facts between two services
  (dependency / shared-route / temporal precedence), same `/api/relationships`
  the UI's draw-a-line modal leads with. Today the human draws grounded edges
  and Claude links blind; this fixes the asymmetry.
- `weaver evidence <service>` — the node dossier (`/api/nodes/{id}/evidence`):
  signal trajectories, log groups, deploys, trace participation. One screen,
  the same data behind the UI's explore buttons.
- `weaver board review` — for each red string on the board, enumerate the
  facts beneath it (relationships between its endpoints) and flag edges with
  **no recorded relationship** as "asserted, not grounded". Strictly
  enumeration — it lists facts per edge and never scores or crowns
  (`analysis-architecture.md` line holds). This is the one-command "challenge
  my wall" Claude runs when asked "does my story hold up?".

## Phase 5 — symmetric editing

- `weaver unpin <evidence-id>` — drop one card (`DELETE …/evidence/{id}`).
- `weaver unpin <service> --all` — remove the node, its evidence, its strings
  (`DELETE …/nodes/{serviceId}`), mirroring the UI trash cans.
- Convention for the agent briefing (Phase 7): Claude prunes *its own* pins
  freely, asks before removing things the human placed. (Provenance for pins
  isn't stored — only edges carry `drawnBy` — so this stays a behavioral rule;
  adding `pinnedBy` to evidence is a cheap optional follow-up.)

## Phase 6 — the grace layer (tired-user affordances)

- **Pasted URLs as arguments.** Anywhere a board id goes, accept the full URL:
  `weaver board show 'http://…/view?board=ab12cd34'`. The tired user pastes
  one thing — the link in their address bar — and Claude sees their board.
- **Edges addressable by endpoints.** `weaver crossout payments-db checkout-api`
  resolves the edge between the pair (lists candidates if several). Ids still
  work; pairs are what a human actually says.
- **Did-you-mean service resolution.** Case-insensitive; unique
  substring/prefix match auto-resolves with a note ("payments → payments-db");
  ambiguous or unknown ids error with the nearest candidates from `/api/graph`.
  Cheap client-side; no API change.
- **Forgiving time.** `--from 14:30` infers the date from the dataset window;
  `--around <ts> [--span 30m]` mirrors the UI's ±30 min "more like this"
  window. ISO always accepted.
- **Teaching errors + layered help.** `weaver <verb> -h` with flags and one
  canonical example (the `cli.md` convention, still unimplemented — today only
  global help exists). Bad facet values error with the valid vocabulary inline
  (pull from `/api/search/facets`).

## Phase 7 — docs catch up with reality

- Rewrite `cli.md`'s verb tables to the board model (retire `select` /
  `show <view-id>` / View `narrative` — drowned in the pivot). Keep the output
  conventions; they're load-bearing.
- `agent-workflow.md` step 7: "pin it → View URL" → "pin + link on the shared
  board → URL".
- **Agent briefing** (new, co-located with the demo): how Claude behaves as
  co-researcher. Read the board before challenging it; resolve fuzzy
  references by probing (`board show`, `search`, did-you-mean) and *confirm
  the resolution in prose* ("taking 'the db thing' as payments-db — correcting
  if not"); label every agent-drawn edge with its reasoning; never cross out
  or unpin the human's work without asking; when the user sounds rushed,
  answer first, elaborate second. The CLI carries hints; the manner lives here.

## Coupled UI touches (small, same contract)

1. Typed result ids visible on search-result + evidence cards (Phase 2).
2. Consume the server-side evidence `summary` (Phase 3) — delete the TS copy.
3. **Search state into the URL** (scope + facets + free text), so a pasted URL
   reproduces the user's *query*, not just their board — completing
   "URLs are the agent↔UI contract" (`weaver-ui-rules`). Then
   `weaver search --from-url '<paste>'` re-runs exactly what they're looking
   at. Worth doing; can trail the CLI phases.

## Sequencing & effort

| phase | depends on | size |
|---|---|---|
| 1 search parity | — | M |
| 2 result-id currency | 1 (+1 small API endpoint) | S–M |
| 3 board readback | — (summary field touches API+UI) | S |
| 4 grounding verbs | — | S |
| 5 symmetric editing | — | S |
| 6 grace layer | 1 (time/facet plumbing) | S–M |
| 7 docs + briefing | 1–6 settled | S |

1→2 is the spine (shared lens, then shared handles). 3+4 unlock
confirm/challenge and can land in parallel with 2. 5 and 6 are independent
small wins. Nothing here is Figma-gated.

## Non-goals

- No natural-language query verb — Claude *is* the NL layer; the CLI stays a
  crisp, guessable grammar.
- No verdict or ranking verbs (`root-cause`, edge scoring) — the
  enumerate/discriminate line is the architecture.
- No CLI-side board rendering/layout — positions are the UI's computed
  concern; the CLI speaks structure, not pixels.
