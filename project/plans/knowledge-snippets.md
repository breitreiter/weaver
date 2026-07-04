# weaver — knowledge snippets (the bag of factoids)

**Status: plan only — nothing built.** A new stored data source: short prose
chunks about the system under management. Documentation excerpts, runbook
fragments, prior-incident write-ups, prior board text — deliberately one
blended bag of chunks, not a document store with per-source schemas.

## Why

The design docs keep pointing at the same gap: the tie-break in an
investigation is frequently **out-of-band truth the telemetry never held**
(`agent-role.md`, `case-log.md:73`, `co-edit-document.md:139`) — what a deploy
*meant*, what happened last time, which pool size someone chose on purpose.
Knowledge snippets put the *recorded* portion of that truth in-band as observed
artifacts: a runbook exists in the world the same way a log line does. The
*live* human context (was a sale running?) stays out-of-band by design — this
feature narrows the gap, it doesn't close it, and it shouldn't.

The observation principle holds unchanged: a snippet is a stored **artifact**
(someone wrote that doc; we observed it), not a derived judgment. The mystery
discipline moves into the snippets' *content* — see Authoring below, which is
where this feature's difficulty actually lives.

## Locked decisions (v1)

1. **One blended bag.** A single `knowledge_snippets` table, one shape for
   every source. No doc-vs-incident sub-schemas.
2. **Provenance is a tag, not a type.** `source` enum: `doc | runbook |
   incident | board`, plus an optional free-text `source_ref` (`INC-2412`,
   `wiki/payments/pooling`) for flavor and citation.
3. **Every snippet attaches to exactly one service.** Required FK. This is
   what scaffolds snippets into `evidence <service>` for free and gives a pin
   its home node. A cross-cutting doc attaches to the service it's *most
   about*, or gets split into per-service chunks (chunking is the point of the
   bag). Multi-service / edge attachment: deferred.
4. **No timestamp.** Snippets are timeless with respect to the telemetry
   window: exempt from `--from/--to`, the evidence-window filter, `timeline`,
   `anomalies`, and the histogram endpoint. A date that matters narratively
   ("during the March incident") lives in the body prose, where the
   investigator can weigh it. This deliberately dodges every "what does a time
   filter mean for a doc" question — revisit only if a real need shows up.
5. **Lives in `weaver.db`, emitted by datagen, read-only like everything
   else.** Snippets are never written at runtime — the co-edited document is
   where *new* knowledge lands. (A later harvest loop — finished board →
   snippet for the next dataset — is adjacent to `case-log.md`; out of scope
   here.)
6. **Search is FTS5.** Same engine as logs, no vectors, no new dependency.
7. **Chunks are smol: one concept, 1–3 paragraphs.** The chunk is the unit of
   storage, search, pinning, and reading. This matches weaver's read idiom
   (cheap rows → drill-in for the expensive read, like traces → `trace <id>`),
   keeps a pinned snippet a readable card instead of a scroll region, and makes
   knowledge pay-as-you-go for an agent's context. Long coherent documents
   aren't lost — they're a *chain* of chunks (see lineage below); author the
   doc, split it at concept boundaries.
8. **Enrichment is an authoring convention, not a pipeline.** Every chunk's
   title must carry its situating keywords (`payments-db connection pooling —
   sizing rationale, post-INC-2412`). FTS indexes title + body, so a
   well-titled chunk is a contextually-enriched chunk — Anthropic's Contextual
   Retrieval (2024) done by hand, which we can do because the chunks are
   authored, not mechanically split from pre-existing docs. The production
   story lives in `demo-vs-production.md`.

## Schema

`knowledge_snippets` (datagen `build_schema`, next to the other tables):

| field | type | notes |
|---|---|---|
| `id` | slug | stable, e.g. `kn-payments-pool-sizing` |
| `service_id` | slug | **required** → `services.id` |
| `source` | enum | `doc \| runbook \| incident \| board` |
| `source_ref` | string? | citation flavor: `INC-2412`, `wiki/…` |
| `title` | string | headline for result rows — **must self-situate** (see Authoring) |
| `body` | text | the chunk — one concept, 1–3 paragraphs, markdown-ish |
| `doc_ref` | slug? | parent-document identity for multi-chunk docs (`wiki/payments/pooling`, `INC-2412`) |
| `seq` | int? | position within `doc_ref`; both null for a true loose factoid |

`doc_ref`/`seq` is the whole "document" model — there is no documents table.
A coherent doc is just chunks that share a `doc_ref`, ordered by `seq`; the
bag stays a bag, some marbles know their neighbors. Chunks of one doc may
attach to different services (that's the sanctioned split for a cross-cutting
doc).

Plus `knowledge_snippets_fts` — FTS5 over `title` + `body`, mirroring the
`log_events_fts` pattern (`generate.py:182`).

C# side: `KnowledgeSnippetEntity` in `Entities.cs` + mapping in
`WeaverDbContext.cs`, with queries guarded like `ChangeEventEntity` so the API
keeps working against a pre-regeneration DB.

## Wiring — every touchpoint

The `changes` scope is the precedent groove (a later-added stored kind with its
own scope); snippets follow it, with a required service and no time axis.

- **Typed id: `kn:<id>`.** Prefix is free (current set: `svc log tr an me ce
  ch`). Wire through the CLI allowlist (`Cli/Program.cs:600`) and
  `/api/search/resolve` (`Api/Program.cs:407`), so `pin kn:<id>` resolves
  through the same builder as the UI card — byte-identical pins, as always.
- **Search scope `knowledge`** (the set becomes 7): new case in the
  `/api/search` switch (`Api/Program.cs:314`) + CLI `scopes`/`sortBy` lists
  (`Cli/Program.cs:19,22`). Filters that apply: `q` → FTS over title/body;
  `--service`; `--subsystem/--kind/--team` via the attached service (the
  existing `pass(sid)` closure); new `--source` facet. `--from/--to` are
  ignored for this scope — say so in help text rather than erroring.
  Histogram: excluded (no time axis; the guard already whitelists scopes).
  Neutral ordering: service id, then title — never relevance-to-the-incident.
- **Result builder** `KnowledgeResult`: title = snippet title, subtitle =
  `source · service_id`, payload = full body + provenance. `PinTarget`:
  `nodeIds = [service_id]`, evidence kind **`knowledge`**, aspect =
  `source_ref ?? source`, **`At = null`** — `EvidenceRefDto.At` and
  `EvidenceEntity.At` are already nullable, so a timeless pin needs no shape
  change; the evidence card simply renders without a time chip. Dedup key
  `(board, service, kind, aspect, at)` works as-is.
- **Facets**: add `knowledgeSources: string[]` (`SELECT DISTINCT source`) to
  `FacetsDto` — the vocabulary line for the new scope.
- **Evidence dossier** (`GET /api/nodes/{id}/evidence`): new `knowledge` list
  on `NodeEvidenceDto` — **not** window-filtered (precedent:
  `TracesParticipated` already ignores the window). Rows carry `kn:` id +
  source + title + a short excerpt. CLI `evidence <svc>` prints the section
  after changes.
- **Web**: the left panel gains the scope + a result card (title, source
  badge, body excerpt, the `kn:` id, pin button — same anatomy as the other
  cards); `Evidence.tsx` gains a renderer for the `knowledge` evidence kind.
  Note the web has **no node-dossier surface today** (the right rail renders
  *pinned* evidence off the board) — the dossier's knowledge section becomes
  visible in the web only when/if that endpoint grows a web consumer; no need
  to build one for this.
- **Read-full drill-in**: search rows truncate and a body is paragraphs, so add
  one small verb in the `trace <id>` mold — `weaver snippet <kn:id>` prints the
  whole snippet + provenance. When the chunk has a `doc_ref`, it also prints
  the Lucene-style **keep-reading affordance**: `part 2 of 5 of
  wiki/payments/pooling — prev: kn:… next: kn:…` — so the agent (or human) can
  walk a document chunk by chunk, paying context only for what it reads.
  (Naming open, below.)

## Authoring — where the difficulty lives

The generator copies snippets **verbatim from the scenario spec** (a
`knowledge:` list in the topology JSON) — these are authored prose, not
procedural output. The mystery rules, applied to prose:

- **No answer labels.** No snippet may state the current incident's cause.
  The `ground-truth.md` rule extends here: per-snippet signal/decoy notes go
  in `project/private/`, never in the data.
- **Prior incidents rhyme; they don't repeat.** A prior-incident snippet sits
  one abstraction away — same *mechanism family*, different service or
  trigger, its own details. Mapping it onto today is the investigator's move,
  not the snippet's.
- **Titles self-situate — that's the retrieval design.** The default rule:
  a chunk's title carries the keywords that locate it (service, topic,
  incident ref), because title+body is what FTS matches. This is the hand-made
  equivalent of Contextual Retrieval's prepended context blurb.
- **…with deliberate exceptions.** Don't make *every* chunk perfectly
  self-situating. A couple of context-poor chunks — "bumped it to 200 after
  last time, should hold through Q4" under a vague title — are realistic, and
  reachable via the keep-reading affordance (land on the well-titled sibling,
  walk forward). Enrichment is the default; context-poverty is authored decoy
  texture, not an accident.
- **Staleness is texture.** An outdated runbook, a doc describing a dependency
  that has since changed, a pool-size rationale that predates a traffic
  doubling — realistic, and exactly the decoy material that keeps search from
  being an oracle.
- **Mostly boring, on purpose.** The majority of the bag is background:
  architecture notes, ownership, SLO statements, on-call trivia. If every
  snippet is load-bearing, `search knowledge` becomes a verdict route.
- Sizing instinct for the flash-sale set: a few snippets on each load-bearing
  service, one or two on most others — roughly 2–4× the service count total,
  signal-bearing ones in the clear minority. Authoring shape: a handful of
  multi-chunk docs (a runbook, a post-mortem, an architecture note — 3–6
  chunks each) plus loose single-chunk factoids, rather than forty orphan
  paragraphs invented independently.

### Authoring pipeline (how the prose gets made)

- **Models are an authoring-time tool, never part of `generate.py`.** The
  pipeline: model-assisted drafting → frozen `knowledge:` list in the
  checked-in scenario spec → `generate.py` copies verbatim. Regeneration stays
  deterministic and key-free, and generated prose passes a review gate before
  it becomes canon — the failure that matters is a generated chunk leaking an
  answer label or contradicting the planted mechanism.
- **Two tiers.** The load-bearing chunks (~a dozen: signal-bearers, decoys,
  the rhyming prior incident, the stale runbook) are authored by hand —
  they're puzzle construction, precisely coupled to the topology and the
  private ground truth. The boring background majority is drafted by models
  against per-doc briefs.
- **Voice diversity is the point of delegating the filler.** One author's
  voice across a whole wiki reads synthetic; real internal docs are many hands
  in many moods. Assign a different model per fictional author/team. Flatter
  prose from smaller models is acceptable — mediocre internal documentation
  *is* realism — but keep post-mortem-grade docs on a stronger model, since a
  limp post-mortem reads as generator artifact rather than texture.
- **Ground-truth isolation as a leak guard.** Filler models receive the
  service card and a doc outline, never the ground truth — they *can't* leak
  what they never saw. Hand-authored chunks are reviewed against the
  no-answer-label rule instead.
- **No tooling yet.** Drive the drafting interactively for the first dataset;
  if re-authoring recurs per dataset, a small `tools/datagen/`
  authoring helper that takes briefs and emits the `knowledge:` YAML earns
  its keep then — not before.

## The demo beat — one clean hit, no confusion theater

Clock discipline: on stage, knowledge gets **one beat, ~30 seconds, zero
ambiguity** — a `search knowledge` that surfaces the out-of-band fact the
telemetry never held (the pool-size rationale, the rhyming prior incident),
pin it, cite its `@kn:` id in the document. Knowledge-as-tie-break,
demonstrated once, cleanly.

Explicitly **not** a demo beat: "watch Claude get misled by the stale runbook
and self-correct." Tempting — it shows off real reasoning — but it eats
minutes and rewrites the audience's takeaway into "so this was about Claude
reasoning over conflicting text?" The decoys and stale docs stay in the
dataset (realism, and the honest answer to "what keeps knowledge search from
being an oracle") — the staged path just doesn't step on them. They're there
for the grilling, not the show.

## Deliberate v1 exclusions

- Timestamps, and everything they'd drag in (timeline/anomaly/histogram
  participation, window semantics for docs).
- Multi-service or edge attachment.
- Surfacing snippets in `relationships <a> <b>` when one mentions both
  services — tempting, later.
- Semantic / embedding search.
- Runtime snippet authoring or board→snippet harvesting.
- Cross-scope blended search (unchanged v1 stance from `search-api.md`).

## Build order

1. **datagen**: table + FTS + `knowledge:` passthrough from the scenario spec;
   author the flash-sale snippet set; regenerate.
2. **Core**: entity + mapping + missing-table guards.
3. **Contracts + API**: `KnowledgeSnippetDto`, search scope, resolve, facets,
   result builder.
4. **Dossier**: `NodeEvidenceDto.knowledge` + CLI `evidence` section.
5. **Web**: scope + result card + `knowledge` evidence-kind card.
6. **Docs**: scope lists in `cli.md` + `CLAUDE.md`, scope table in
   `search-api.md`, entity in `data-model.md`.

## Open questions

- Scope name: `knowledge` vs `docs` vs `notes`? (`knowledge` is honest about
  the blend; `docs` undersells incidents and board text.)
- Drill-in verb name: `snippet` vs `knowledge <id>` vs a generic `show
  <typed-id>` (the generic is attractive but bigger than this feature).
- Is the four-value `source` enum enough? (`chat` — a pasted Slack thread — is
  the obvious fifth; resist until a dataset wants it.)
- Whether `--grep` FTS should also match `source_ref` (probably yes, cheap).
