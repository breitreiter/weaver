# weaver — UX cleanup (grounding the gripes in current → refined)

A punch-list from a demo/review, now **grounded against the code**: each item
pairs the current state (with `file:line`) to the refined target and the *why*.
It's a surface-polish list, not a redesign — several items fold into larger
efforts and say so. Out of scope, owned elsewhere:

- the **middle graph pane** → `graph-redesign.md` (passive SVG, red-string wall);
- the **chart wall** → `chart-wall.md`;
- the **house-style identity / semantic-token layer** → `TODO.md` → house style.

Where an item is really a slice of one of those, it's marked **(house style)** /
**(search-api)** so it lands with that pass rather than as a one-off.

Two decisions taken during this rewrite move past simple polish:
**equal-thirds panels override `TODO.md`'s search-dominant weighting** (reconciled
here), and the **UTC dropdown becomes a real timezone selector** (backend-adjacent).
Both are flagged inline and in *Coordination* at the end.

---

## Overall

### 1. Field-label contrast is too low

- **Current.** Body text is `--text: #c9d1de` on `--bg: #14161c`
  (`index.css:5-7`). Facet labels render in `--text-dim: #8a93a6`
  *and* at 11px *and* uppercase with letter-spacing (`App.css:37`) — small + dim +
  all-caps is a triple hit on legibility, worst exactly where the user reads the
  controls.
- **Refined.** Lift the label tier: a brighter "dim", and/or drop the uppercase,
  and/or bump the size. This is a **token-level** change (`--text-dim` and the
  `.facets label` rule), so it belongs in the semantic-token pass **(house
  style)** — but contrast is table-stakes legibility *below* the encoding layer,
  so it can land early without waiting on the full identity work. Ties to
  `design.md` #2 (encode for the eye) only loosely; this is "can you read it at
  all," not "does colour mean something."

### 2. Drop the "· N pinned" suffix from the header

- **Current.** The board-status line reads `` board ${boardId} · ${pinned.size}
  pinned `` (`Workbench.tsx:184-186`). `pinned` is a **session-local `Set`**
  (`Workbench.tsx:58`) that only grows via `pin()` and is *deliberately not
  reconciled* against the board (`Workbench.tsx:126-127`). So the count is wrong
  after a reload, and blind to anything the **agent** pins — it counts "things I
  clicked this session," not "things on the wall."
- **Refined.** Drop the `· N pinned` suffix; keep **`board <id>`**. The id is the
  agent↔UI contract and must stay visible (CLAUDE.md — top-left, like
  `board dfbc006b`). The honest pinned count already lives, accurate, in the
  evidence head: `` `${groups.length} services · ${evidenceCount} evidence` ``
  (`Evidence.tsx:54`). One true count beats one true + one misleading.

### 3. A product mark beside the wordmark

- **Current.** `.brand` is bare text `weaver` (`Workbench.tsx:183`,
  `App.css:17`) — no mark.
- **Refined.** A small logotype glyph beside the wordmark; even a lifted Material
  symbol as a placeholder is enough to stop it reading as unstyled. Pure
  identity — **(house style)**; the slot is here, the look is downstream.

### 4. Search results should *read as* the evidence they become

- **Current.** A single finding is rendered in **two different visual languages**.
  As a search **ResultCard** (`Workbench.tsx:296-316`): a coloured
  `badge badge-${type}` pill (`App.css:82-90`) plus a left-border direction tint
  (`App.css:64-67`). As an **evidence item** (`Evidence.tsx:93-115`): a
  `kind-${ev.kind}` coloured left border with the kind icon promoted to the lead.
  Same anomaly, two looks — so pinning reads as a re-skin, not as *the same object
  moving to the board*.
- **Refined.** Make the search card **echo the evidence card**: same kind-colour
  as a left border, same lead kind-icon, same one-line summary. This is
  `design.md` #3 (one finding, one identity across every surface) applied to the
  pixels — the visual half of the parity the CLI already shipped (one renderer,
  one vocabulary — `cli-co-researcher.md`).
- **"except services."** A service is a **subject/node**, not evidence — in the
  evidence panel it's a section *header*, not a kind-coloured card
  (`Evidence.tsx:66-76`), and it carries no kind colour. So the **service** result
  card stays in its node idiom; only the five evidence kinds (anomaly / log /
  trace / metric / change) adopt the evidence-card look.

### 5. Three-panel balance → equal thirds  ⚠ overrides `TODO.md`

- **Current.** `search-pane flex: 1.5`, `board-pane flex: 1.1`,
  `evidence-pane flex: 1` (`App.css:7-12`) — search ≈ 41% of the width, the two
  right panels visibly unequal.
- **Decision (this rewrite): equal thirds, `1 : 1 : 1`.**
- **⚠ This overrides `TODO.md`.** The left-panel-v2 item ("Take ≥50–60% of the
  screen … search is where attention lives most of the time") calls for search to
  dominate. **`design.md` does *not*** — it mandates full-viewport density
  (principle #1) and a forage-left / sensemake-right split, but no proportions, so
  equal thirds is consistent with it. The rationale for overriding TODO.md: the
  board (red-string wall) and the evidence panel (the narrative) have matured into
  full co-researcher surfaces and now carry as much of the session as search does;
  the "search is the main attraction" weighting predates them. The `TODO.md`
  left-panel-v2 item is **reconciled to match in this pass** (see *Coordination*).

---

## Search

### 6. The pin icon is stale — `graph_3` → a pushpin

- **Current.** The pin button uses `<Icon name="graph_3" …>`
  (`Workbench.tsx:307`) — a node-graph glyph, apt back when the board *was* a
  node-graph.
- **Refined.** The board is now a **red-string wall**, not a node-graph
  (`graph-redesign.md`). Swap `graph_3` for a **pushpin / keep** glyph
  (Material `push_pin` / `keep`) so the affordance matches the mental model.
  Trivial; do it with the icon/house-style pass **(house style)**.

### 7. UTC badge → a real timezone selector  ⚠ backend-adjacent

- **Current.** A static, label-less `<span className="time-tz">UTC</span>`
  (`Workbench.tsx:221`). The whole stack is **UTC-naive**: trace cards render UTC
  via `fmtTime` (`Workbench.tsx:286-291`), the `datetime-local` inputs are
  timezone-naive over the UTC window bounds (`facets.window`,
  `Workbench.tsx:224/229`), the backend filters by **raw `string.Compare` on ISO
  text** (`Program.cs:374-375`, `385-386`), and anomaly onsets parse as UTC
  (`ParseUtc`, `Program.cs:355-356`). The badge also sits misaligned: it's a
  label-less span (`App.css:77-78`) dropped among flex-column `.facets label`
  controls (`App.css:37`), so it doesn't line up with its neighbours.
- **Decision (this rewrite): a real timezone dropdown** that converts both
  display and query bounds — *not* cosmetic. Scope:
  - **Convert at the API boundary, keep the backend UTC-naive.** The string
    compare (`Program.cs:374-375`) is correct *only* because every timestamp is in
    one frame. So the UI converts the chosen zone → UTC before it hits
    `/api/search`, and the backend is untouched. (Server-side conversion would
    mean parsing every compare — avoid.)
  - **Display.** Route every rendered timestamp through one tz-aware formatter
    instead of `toISOString().slice(…)` (`Workbench.tsx:290`) — trace card times
    (`Workbench.tsx:295/311`), evidence summaries, etc.
  - **Inputs.** `from`/`to` are entered in the chosen zone and converted to UTC
    before the query; the `datetime-local` `min`/`max` (`facets.window`,
    `Workbench.tsx:224/229`) shift into the chosen zone too.
  - **Default to UTC** (the responder's incident frame is usually UTC); offer
    browser-local + a couple of common zones.
  - **Fixes the alignment gripe for free** — the badge becomes a real labelled
    control that sits in the facets row like the others.

### 8. A real range picker, with accelerators

- **Current.** Two bare `datetime-local` inputs (`from`/`to`,
  `Workbench.tsx:222-231`) bounded to `facets.window`, plus a clear button
  (`Workbench.tsx:232-237`). No presets — every window is typed by hand.
- **Refined.** A purpose-built range control with **accelerators**: "full
  window," "last 15m / 1h," and crucially **"around the incident" / centre-on-a-
  timestamp**. The ±30-minute centring already exists — `aroundWindow`
  (`Evidence.tsx:202-208`) computes exactly this for evidence "find more like
  this"; surface the same idiom as a picker preset. The control reads/writes the
  same `from`/`to` facets, so nothing downstream changes. Couples to the
  "coherent vs-base periods" polish item (`TODO.md`) — human-recognisable windows,
  not raw clock entry. **Bundle with #7** — both rewrite the time controls.

### 9. No-items state should name what's filtering the view

- **Current.** Empty results render `"No results. Try a different scope or loosen
  the facets."` (`Workbench.tsx:254`) — generic, says nothing about *what's
  currently constraining the view*.
- **Refined.** Name the active constraints — scope, the set facets, and the time
  window if any — so the empty state reads as "you're looking at a filtered
  slice," e.g. *"No anomalies in subsystem `storefront`, 14:00–14:30 UTC, z≥3.
  Loosen a facet or widen the window."* All of it is state the component already
  holds (`f` + `scope` + the window, `Workbench.tsx:54`), so this is a render, not
  new plumbing. Pairs with #10. Ties to `design.md` #4 (honest over clean — show
  the workings).

### 10. Cross-filter the facet lists to stop search-bricking  ⚠ search-api

- **Current.** `opts(k)` (`Workbench.tsx:170-177`) returns the **full** facet
  vocabulary from `/api/search/facets` (`Program.cs:297-309`) regardless of the
  other selected facets. So `subsystem=A` + `team=B` that share no service yields
  a dead view with no hint why — "search-bricking."
- **Refined.** Two paths:
  - **(a) Honest fix** — cross-filter the dropdowns so each only offers values
    still *reachable* given the others. Needs a facets endpoint that takes the
    current filter set (or a richer payload narrowed client-side). This is the
    `design.md` #4 answer: don't offer a lever that does nothing. **Costs a
    backend change** — fold into the next search-API pass (`search-api.md`).
  - **(b) Stopgap, now** — keep the full lists, lean on the improved empty state
    (#9) to explain the dead combo.
  - Recommend **(b) now, (a) with `search-api.md`.** This is the one item that
    needs a backend decision before the honest version ships.

### Logs — the 500 (correctness, not polish)

### 11. FTS5 query injection — `no such column: api`

- **Current.** The logs scope builds the FTS5 query as
  `WHERE log_events_fts MATCH {q}` with the user's text passed straight through
  (`Program.cs:367-370`; the **same pattern repeats** in `/api/logs`,
  `Program.cs:103-106`). `FromSqlInterpolated` binds `q` as a *parameter*, but
  FTS5 still parses the **value** as a query expression — so any FTS5
  metacharacter in the user's text is *interpreted*, not matched. A query with a
  colon (a log search like `api: timeout`) makes FTS5 read `api` as a **column
  filter** → `SQLite Error 1: 'no such column: api'`, surfaced as the reported
  500.
  > The exact trigger string from the demo should be confirmed, but the class is
  > unambiguous: unsanitised FTS5 input. Colon, parens, and the bare words
  > `AND`/`OR`/`NOT`/`NEAR` are all live syntax today.
- **Refined.** Sanitise the user text into a **safe FTS5 query** before `MATCH`:
  wrap each token as a quoted phrase (`"api" "timeout"`), doubling any embedded
  double-quote, so colons/parens/booleans are literal text rather than syntax.
  Fix **both** call sites (`Program.cs:103-106` and `367-370` are the same bug).
  If column-scoped or boolean search is ever wanted, expose it *deliberately*, not
  by accident. **This is a correctness bug — top of the build order.**

---

## Evidence

### 12. Canned searches read as tabs — unify into one "search for X" affordance

- **Current.** Under each service header sits a row of five buttons (`.ev-explore`
  — metrics / anomalies / logs / traces / changes, `Evidence.tsx:78-88`), and each
  evidence item carries its own "find more like this" buttons (`.ev-item-search`,
  `Evidence.tsx:99-109`); both share `.ev-explore-btn` styling
  (`App.css:266-269`). Sitting directly under the service title, the row reads as
  **tabs or filters on the service**, not as "run these searches." The same
  ambiguity hits the `TraceMini` hop buttons on the search card
  (`Workbench.tsx:330-336`), which also launch searches.
- **Refined.** One **uniform "search for X" affordance**, reused everywhere a
  control launches a left-panel search — a `search:` prefix or a magnifier-led
  pill — visually distinct from a tab/toggle so the gesture reads as "this runs a
  query." Defining that single affordance is **(house style)** (`TODO.md`
  semantic tokens — "every clickable thing shares the interactive signal"); the
  payoff is the operator learns it once and reads it the same on the search card,
  the service header, and the evidence item. Ties to `design.md` #2 (one stable
  visual = one meaning).

---

## Build-out — sequencing across plans

This work spans three plans: **this doc** (left pane + backend),
**`graph-redesign.md`** (middle pane + the evidence drawer), and the **house-style
pass** (`TODO.md`). Sequenced naïvely they rework each other. The throw-away risk
is narrow — it lives in exactly three couplings:

- the **evidence drawer is restructured by the graph plan** (chips migrate in from
  the node; the relationships section is promoted out of per-service nesting) — so
  anything that styles *against* the drawer waits for that;
- **#4 and #12 are cross-surface visual contracts** (search card ↔ evidence card;
  the one "search for X" affordance on `.ev-explore-btn` + `TraceMini`) — built
  before the drawer settles, they get built twice;
- the **house-style pass wants the hand-rolled SVG graph as its canvas** (`TODO.md`
  — set the aesthetic there, not on React Flow's defaults), which is where the
  identity-sensitive items naturally converge.

**Not a blank-center-pane episode.** `graph-redesign.md` lands in place and
additively — hover-highlight breaks nothing, the edit-surface removal is *last*
(after its drawer replacement exists), and the live graph keeps its click→focus
role throughout. Blanking it would discard a working surface for no gain.

**Not strictly graph-first either.** Most of this doc is left-pane / backend and
is independent of the graph; it shouldn't wait. The classic waste — polishing the
React Flow graph that's about to be deleted — is already avoided: `ux-cleanup`
scopes the middle pane out from the top.

### Phase 0 — correctness + mechanical (now; parallel-safe with Phase 1)

Touches only search / backend / layout; nothing here is reworked by the graph or
house-style work.

1. **#11 logs FTS 500** — a live crash; ship first.
2. **#2** drop "· N pinned", **#9** no-items text, **#10b** facet stopgap,
   **#5** equal-thirds flex, **#7/#8** time controls.
   - #7/#8: the *behaviour* (tz conversion at the API boundary, range presets) is
     the substance and is safe now; only the *look* gets a touch-up in Phase 2.
   - #1 (contrast) and #6 (pin icon) are cheap enough to pull forward here if
     desired, but they're token/icon choices the house-style pass owns — default
     is Phase 2.

### Phase 1 — graph + drawer redesign (`graph-redesign.md`, in place)

Follows its own internal order (hover-highlight → thin node + chips to drawer →
relationships section → SVG swap → remove edit surface). Produces the final drawer
structure and the SVG canvas. Can overlap Phase 0 — different panes.

### Phase 2 — house-style sweep (`TODO.md`)

Now has its finished canvas (SVG graph) and settled drawer to set identity
against. Absorbs the identity-sensitive items in one coherent pass: **#1**
(contrast tokens), **#3** (logotype), **#4** (search ↔ evidence card parity),
**#6** (pin icon), **#12** (one "search for X" affordance), plus the **#7/#8**
time-control restyle — and **#10a** (honest facet cross-filter) once the
`search-api.md` pass has landed.

**Why this avoids rework:** the React Flow graph is never polished; #4 / #12 are
styled once, against the final drawer; and equal-thirds (Phase 0) never fights the
SVG swap (Phase 1) because the redesign's computed `viewBox` auto-fits any pane
width by construction.

## Coordination (docs that must move with this)

- **`TODO.md` (left-panel-v2)** — the equal-thirds decision (#5) overrides the
  "search ≥50–60%" item; **reconciled in this pass.** `design.md` was checked and
  needs no change (it mandates full-viewport density, not panel proportions).
- **`search-api.md`** — facet cross-filtering (#10a) needs a filter-aware facets
  endpoint.
- **House-style / semantic-token pass (`TODO.md`)** — owns the final form of #1,
  #3, #4, #6, #12; this doc only marks the slots and the intent.
- **Backend stays UTC-naive (#7)** — the timezone selector converts at the API
  boundary; do **not** push tz parsing into `Program.cs`'s string compares.

## Settled / open

| Item | State |
|---|---|
| #1 Lift field-label contrast (token tier) | ✅ direction; lands with house style |
| #2 Drop "· N pinned"; keep `board <id>` | ✅ settled |
| #3 Logotype mark beside the wordmark | ✅ slot; look downstream (house style) |
| #4 Search cards echo evidence cards (services excepted) | ✅ settled |
| #5 Equal-thirds panels (1:1:1) | ✅ decided — overrides `TODO.md` (reconciled) |
| #6 Pin icon `graph_3` → pushpin | ✅ settled |
| #7 UTC → real timezone selector, convert at API boundary | ✅ decided — backend-adjacent |
| #8 Real range picker with accelerators | ✅ settled; bundle with #7 |
| #9 No-items state names active constraints | ✅ settled |
| #10 Facet cross-filter (anti-brick) | ⬜ (b) now / (a) needs `search-api.md` |
| #11 FTS5 input sanitised (both call sites) | ✅ settled — correctness, ship first |
| #12 One "search for X" affordance everywhere | ✅ direction; form is house style |

The line this doc keeps: every fix lowers the cost of a lap on the sensemaking
loop (`design.md`) or removes a dishonesty (a wrong count, a dead facet, a
silent 500) — none of it adds chrome or hides state.
