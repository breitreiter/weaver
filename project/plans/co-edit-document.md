# co-edit-document — the document-centric pivot (locked)

_Locked 2026-06-29. Supersedes the graph as weaver's synthesis surface._

weaver's centerpiece stops being a node-edge **graph** and becomes a single
**living document** that a human and Claude co-edit. The graph was only ever one
impoverished view of what language does natively — language is semantically richer
and encodes far more complex relationships than a red string between two dots. We
keep the sensemaking loop (Pirolli & Card were right); we drop the assumption that
the _output_ of sensemaking is a graph. The output is prose.

## The pivot in one line

`forage (search) → pin (the shoebox) → **document** (synthesis)` — the document
replaces the board graph as the third stage. Pins stay; `@`-references are the
grounded tether from prose back to the real finding.

## What the document is

One document per board (linking documents is deferred). It morphs by **state**,
not by being a different artifact:

- **active outage →** a live RCA / investigation scratchpad
- **incident resolved →** a post-incident review (PIR) + action plan
- **nothing on fire →** a design/plan for a new service

## Locked decisions (the whole thread)

| Decision | Resolution |
|---|---|
| Centerpiece | A co-edited markdown **document**, not a graph |
| Graph / relationships / `BoardEdge` / `link` / `crossout` / RelationshipModal | **Removed** |
| Sensemaking loop | **Kept** — forage → pin → synthesize |
| Pins + evidence (the shoebox) | **Kept**; `@`-references resolve to pinned findings |
| Editor | **CodeMirror 6** (single always-legible view, custom `@` decorations, autocomplete; no edit/preview toggle) |
| `@` scope | Pinned evidence + services on the board only (`@` never mutates the board) |
| Concurrency substrate | **Architecture C** — version-checked diff/patch + server-side 3-way merge; markdown stays canonical |
| Graduation path | Keep `{id, text, version}` read shape → swap to Yjs CRDT later (`ydotnet`, no Node sidecar) as an internal change, not an API break |
| Co-editing posture | **Attributed direct edits** — Claude edits anywhere; every AI edit renders two-tone (persistent provenance); its last turn is one-click revertible. No suggestion gating (provenance-gated suggestions deferred). |
| Claude's hands | The **`weaver` CLI** (reliable proxy for MCP) — submits context-anchored patches |
| Document structure | A six-slot **scaffold**, freeform (not enforced) |
| Timeline | Made first-class — but as **prose + `@`-refs**, not a new widget |

## The substrate (Architecture C)

The document is a markdown **string + integer `version`** in `boards.db`.

- Read → `{ id, text, version }`.
- Write → `{ baseVersion, newText }`.
- Server check is **atomic in one SQLite transaction**: if `baseVersion ==
  current` → accept, bump version; else **3-way merge** (base / server-current /
  incoming) via DiffPlex or a diff-match-patch C# port → clean merge accepts;
  conflicting hunks bounce back with current state.
- Replaces the 2.5s poll for the document (version field tells clients when to
  refetch; SSE optional later).

**CLI agent discipline (non-negotiable — from the concurrency research):**
- anchor edits to **text context** (search-and-replace), never line/char offsets;
- make **small, localized** edits, not whole-document rewrites (before/after
  diffing merges worse than real keystrokes — keep the blast radius small);
- on conflict, **refetch and re-diff** — never blind-retry (a blind retry
  re-clobbers).

Failure modes to engineer around: lost-update unless the version-check + write are
one transaction; fuzzy-patch can silently misplace a hunk (keep edits small;
surface conflicts to the human rather than auto-resolving); diff3 is syntactic, not
semantic (a clean auto-merge can still be a wrong merge — agent edits stay
reviewable).

## The co-editing model (attributed direct edits)

The dominant real-world failure is **regenerate-and-replace** — AI generating a
fresh block and swapping it for the old one, silently nuking human text (Canvas /
Word Copilot / Cursor all documented doing this). weaver avoids it structurally:
edits are **span-anchored patches**, never block regeneration.

- **Direct, fluid edits everywhere.** Claude enriches/corrects any region directly.
  No accept-gate in the common path — this is what "edit anywhere" means.
- **Change-highlight of Claude's unseen edits** (recency, _not_ permanent
  authorship). Color marks what Claude has changed **since the human last caught
  up** — so the human can _find the newest info_, not so words are forever
  color-coded. Rationale is the asymmetry: Claude can mechanically diff to catch up
  on the human's edits, but the human scans by eye and will miss a quiet correction.
  Both **new content** and **corrections to existing (incl. human-written) text**
  light up — a correction to your own words is the thing you'd most hate to miss.
  The highlight **fades once caught up**: the human's baseline advances as they
  work (so their own edits are never "new"), and Claude's delta clears on review
  (default: on next open/focus, or explicit dismiss — tunable). Cheap: **no
  permanent authorship map** — just a per-human "last-seen" marker + a diff to
  current (the mirror of the diff Claude already runs to find the human's edits).
  Optional two-shade: added (green) vs. changed (amber), borrowing diff semantics.
- **One-click revert of Claude's last turn.** Store the agent's diff; revert =
  reverse-apply it onto current text (3-way), scoped so it can't drop human edits
  made since; conflict → surface rather than force.
- **AI presence.** "Claude is editing §Timeline" — narrate-as-you-build made
  literal (weaver already has this ethos).
- **Where "final say" actually lives (not an editing partition).** Claude is a
  _symmetric_ co-author — full editing access, may draft anywhere, may propose
  conclusions. The human's final say is **not** a restricted panel or a protected
  slot. It is two things: (1) the human owns the document's
  **completion/finalization** — Claude cannot unilaterally mark the analysis done or
  final (the state header is the locus); and (2) conclusions are reached through
  **active dialogue**, both sides sharing findings and expertise. The flash-sale
  case is _why_ this is epistemic, not mechanical: Claude literally lacks the
  out-of-band data (a sale was running) to reach the right answer from telemetry
  alone, so a Claude that concludes from data only will confidently pick the
  plausible coincidence. The fix is conversation, not access control — so the agent
  contract steers Claude to **propose-and-converse** and to **flag what it can't
  see** ("this reads like X, but I have no business context — was anything unusual
  running?"), never to decree a verdict and never to declare the document finished.

Every surveyed competitor (Rootly, incident.io AI SRE, Atlassian Rovo) hands the
human a finished probable-root-cause to approve. weaver's difference is **not** that
Claude refuses to propose one — it's that the conclusion emerges from human↔Claude
dialogue with the human owning the final call, because the telemetry alone is
insufficient. That's the differentiator; keep it.

## The document skeleton (a scaffold, not a schema)

Six slots defined by their **role in reasoning**, so one skeleton morphs across all
three modes without feeling forced. Offered as a starting scaffold; the doc is free
markdown (Google's "write it however makes sense" posture).

| # | Slot (stable function) | Live RCA | Resolved PIR | New-service plan |
|---|---|---|---|---|
| 0 | **State header** (mode selector) | INVESTIGATING · sev · roles | RESOLVED · duration · impact | DRAFT / DISCUSSION |
| 1 | **Summary** | what's happening now | what happened & fix | what we propose |
| 2 | **Context** | impact + what we know | impact + leadup | motivation + goals/non-goals |
| 3 | **Timeline-or-Plan** | live timestamped log | frozen timeline | rollout milestones |
| 4 | **Evidence** | the dossier (pins / `@`-refs) | data behind each claim | constraints, prior art |
| 5 | **Hypotheses-or-Decisions** | red strings, labeled, for/against | contributors + mitigators | alternatives considered |
| 6 | **Actions** | next steps / owners | follow-ups + lessons (well/wrong/got-lucky) | open questions + work items |

- **Slot 5 is the keystone.** "Working hypotheses" / "contributing factors" /
  "alternatives considered" are the same cognitive object: competing claims, argued
  both ways, resolving to a decision. That slot _is_ the red string, now in prose.
  Claude proposes competing claims here freely (it is _not_ off-limits); resolution
  comes through dialogue, with the human's final say informed by out-of-band
  knowledge Claude lacks.
- **Slot 4 = the pins**, woven in by `@`-reference (the grounding tether).
- **Slot 3 (timeline) becomes first-class** — the one structural critique from the
  research is that weaver was hypothesis-centric and timeline-light, while every
  incident tool treats the UTC timeline as the spine that flows into the
  postmortem. It stays _language_ (a prose section Claude maintains, grounded by
  `@`-refs to timestamped evidence), consistent with "language replaces the graph."

## Codebase impact

**Delete:** `web/src/Board.tsx` (SVG graph), `web/src/RelationshipModal.tsx`, the
relationships section in `Evidence.tsx`; the `BoardEdge` entity + edge endpoints
(create/delete/crossout) + the `link` / `crossout` / `relationships`-draw CLI
verbs; `isRedString` and edge-kind taxonomy.

**Keep:** forage/search (left panel), `BoardNode` + `Evidence` (pins), the board-id
contract, `@`-autocomplete sourced from pins, the hover→focus path-lens (re-aimed at
`@`-refs in prose).

**Add:**
- `BoardEntity.Doc` (markdown `string`) + `DocVersion` (`int`) — and a per-human
  **last-seen marker** (version/snapshot) to compute the unseen-change highlight (no
  permanent authorship overlay).
- `GET` returns `{ …, doc, docVersion }`; `PUT /api/boards/{id}/doc`
  (atomic, version-checked, 3-way merge); revert endpoint for the last AI turn.
- `web/src/Document.tsx` — CodeMirror 6 editor as the centerpiece; markdown +
  custom `@` decorations (hover→path-lens, click→focus); `@`-autocomplete from
  pins; focus/dirty-aware sync + debounced autosave; change-highlight of Claude's
  unseen edits (diff from the human's last-seen marker), fading on review.
- CLI `weaver doc show <board>` (prints markdown + version) and `weaver doc edit
  <board>` (context-anchored patch; re-diff on conflict).
- Rewritten `CLAUDE.md` agent contract: **full co-author** (edit anywhere, may
  propose conclusions); reach conclusions through **active dialogue**; surface what
  telemetry can't show (out-of-band context, e.g. a flash sale) and invite the
  human's expertise; **the human owns finalization** — Claude never marks the
  document done. Narrate edits.
- Human-owned **finalization / state** control (the slot-0 header): only the human
  advances the document to a final/published state. This is the mechanical home of
  "final say" — not any editing restriction on Claude.

**Layout:** forage (left) · document (dominant center) · evidence locker (slim
right rail).

## Phases

- **P0 — substrate + data.** `Doc`/`DocVersion` on the board; read shape; `PUT
  /doc` with atomic version-check + 3-way merge (DiffPlex); revert endpoint.
  Unit-test the merge + conflict bounce.
- **P1 — CLI.** `weaver doc show` / `weaver doc edit` (context-anchored patch,
  re-diff on conflict); retire `link` / `crossout`; rewrite `CLAUDE.md`.
- **P2 — editor.** `Document.tsx` (CodeMirror 6) as centerpiece; `@` decorations +
  autocomplete + path-lens; debounced version-checked autosave; delete graph +
  modal + rel section; relayout to forage · document · locker.
- **P3 — change-highlight + revert.** Per-human last-seen marker; diff-based
  highlight of Claude's unseen edits (added vs. changed), fading on review;
  one-click revert of Claude's last turn; AI presence indicator.
- **P4 — scaffold & polish.** Six-slot starting scaffold seeded by state header;
  timeline section conventions; mode transitions (INVESTIGATING→RESOLVED).

## Open / deferred

- **`@`-ref hover cross-reference (deferred).** Hovering an `@`-ref in the document
  should highlight the matching finding in the evidence rail (and likely the
  reverse) — a cheap, legible way to cross-reference prose against the shoebox. This
  is the intended replacement for the current click→focus gesture, which reads as
  fiddly (subtle feedback, no-op on same-ref re-click). The forage→write direction
  is already covered by the evidence rail's "cite @-ref" button.
- **Stability / heat overlay (promising — parked for post-core).** A _shared_
  "settled vs churning" signal, distinct from the per-human catch-up highlight.
  Maturity is **emergent from edit activity, not authorship** — a region cools as it
  survives untouched (cold = settled/locked, hot = churning), which sidesteps the
  fact that shared text blenderizes into consensus and "whose words" becomes
  unanswerable. Strongest "settled" signal: survived edits from **both hands** then
  went quiet (needs only **coarse per-edit author tags** — which version was whose,
  which we already keep for revert — never per-character authorship). Cheap and
  fuzz-tolerant: derivable on demand by replaying recent diffs over current text to
  accumulate decaying heat; an approximate heatmap is still useful. This is the prose
  form of weaver's "present-tense model" (firm where confident, provisional where
  not) and maps onto slot 5 (churning = still hypothesising; cooled = consensus),
  sitting as a soft gradient _below_ the hard human-owned "mark final" switch.
- Graduation to a real Yjs CRDT (`ydotnet`) for true char-level concurrent merge.
- Linking multiple documents / boards.
- A structured (non-prose) timeline widget, if prose proves insufficient.

## Research basis

Three parallel research passes (2026-06-29) grounded these decisions:

1. **Concurrency substrate.** Production editors split: incumbents keep
   server-authoritative OT (Google Docs, Figma-hybrid, Linear); library-based
   editors default to Yjs (CRDT). For a timeboxed build with markdown-canonical +
   a CLI text agent, version-checked diff/patch + 3-way merge is the right weight;
   Yjs is the graduation target (`ydotnet` = .NET-native, no Node sidecar). The
   "AI as a server-side Yjs peer" pattern (Electric, 2026) is the proof the CRDT
   path works when we want it. Agent rule: context-anchored edits, re-diff on
   conflict.
2. **Co-editing UX/trust.** We took the _engineering_ lessons — dominant failure is
   regenerate-and-replace; antidotes are span-anchored edits, cheap revert, and
   transparency about whose words (two-tone). We **rejected** the research's
   access-restriction reading (margin-only AI / suggestion-gating): in weaver
   "final say" is epistemic (human owns _done_ + the conversation), not a mechanical
   limit on Claude's panel. Sources incl. Draxler "AI Ghostwriter Effect"
   (arXiv:2303.03283), HaLLMark (CHI 2024), AnchoredAI (arXiv:2509.16128), Horvitz
   mixed-initiative.
3. **Document structure.** Postmortem templates (Google SRE, Atlassian, PagerDuty,
   incident.io, Rootly, FireHydrant) converge on Summary → Impact → Timeline →
   Cause-analysis → Actions (+ retrospective). The live incident doc is the _same
   artifact, earlier_ (Google's "living incident document"). Design-doc/RFC
   templates share Goals/Non-goals + Alternatives-considered + lifecycle-state.
   Slot 5 (hypotheses / contributors / alternatives) is the same object across all
   three modes — the keystone that makes one skeleton viable. Every incident vendor
   does "AI drafts, human edits/decides"; weaver alone declines to emit a verdict.
