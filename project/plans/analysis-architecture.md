# weaver — where analysis lives

Settles the "analysis-lives-where" question. Resolves the apparent
tension between *"the backend is pure observation"* and *"the agent — and
the UI — need real analysis tools."* The trick: "analysis" is two
different things, and "pure observation" was over-applied.

## "Pure observation" = the stored data, not the API

The seed DB holds **only raw observed telemetry** — no health flags, no
labels, no precomputed analysis, no cached answers, no narrative. That
invariant is sacred (see `data-model.md`, "a mystery, not an oracle").

But computing a blast radius *on demand* over that raw store is a
**derivation, not a stored answer.** So: the DB is dumb; the API computes
primitives live; nothing stores or returns a verdict. Computation on the
API does not violate pure-observation — only *storing* analysis would.

## The line: primitives *enumerate*, the reasoner *discriminates*

Every analysis primitive is deliberately **undiscriminating**:

- `anomalies(window)` — lists *everything* that moved (cause AND
  collateral, mixed, unranked).
- `timeline` — orders *all* onset times; crowns none.
- `blast-radius(X)` — lists *everything* reachable from X; and **requires
  the reasoner to supply X** (a hypothesis). It cannot find X for you.
- `selectors` — enumerate a subgraph from predicates the reasoner chose.

Separating cause from collateral from irrelevant is judgment, and **there
is no endpoint for it.** No `/solve`, no `root-cause?incident=`, nothing
that takes symptoms in and returns a cause out. The backend answers
narrow sub-questions; it never poses them or composes their answers.

> **Enumerate = tool. Discriminate = reasoning.**

Two categories banned **everywhere** (backend, CLI, UI):

- **Verdict** — anything that names or ranks a *cause*.
- **Composition** — anything that runs the investigation *for* you
  (chains primitives into a conclusion).

The forbidden-primitive test: a primitive is banned iff it takes the
symptoms and returns candidates *discriminated by likelihood-of-being-
cause*. It is allowed iff it either returns raw facts, or takes a
reasoner-supplied hypothesis / neutral parameter and returns an
undiscriminating enumeration.

## Three layers, one shared implementation

| Layer | What | Where it lives | Who composes it |
|---|---|---|---|
| **Observation** | raw graph / metrics / logs / traces, as recorded | the dumb SQLite store | — |
| **Primitives** | selectors + `blast-radius` / `anomalies` / `timeline` — enumerations, never verdicts | **`Weaver.Core`, exposed via `Weaver.Api`** | the reasoner picks which to run |
| **Investigation** | compose + cross-validate → ranked hypothesis → pinned View | **the reasoner: Claude (CLI) or human (UI)** | no endpoint, ever |

## Placement: primitives in `Weaver.Core`, exposed via `Weaver.Api`

Both the CLI and the web UI consume the **same** primitives over the
**same** API. This is *forced* by the UI-parity requirement: the CLI is
C# and the UI is TypeScript, so the only way to have one implementation —
no drift, true parity — is server-side. Consequence: "check the blast
radius" is a CLI verb **and** a UI node-action **and** a selection a
pinned View can carry. One vocabulary, spoken by both surfaces. A View
Claude pins from `blast-radius(payments-db)` re-renders identically in the
UI; there is no analysis Claude can speak that the UI lacks.

The CLI calls the API rather than linking `Core` directly — dogfooding:
everything the agent does is reproducible over HTTP (see
`agent-workflow.md`).

## What this buys — the three failure modes it dodges

1. **No oracle.** There's no program to "just run." A single primitive
   never solves it; the solution is *triangulating* four or five
   (timeline says payments-db moved first → is that causal? →
   `blast-radius(payments-db)` covers exactly the alerting set ✓ → its
   logs say "pool exhausted" while upstream says "timeout" ✓ → rule out
   the external gateway scapegoat whose metrics never moved ✓). That
   cross-validation is irreducibly reasoning — where Claude shines.
2. **Not crippled.** The agent has the full realistic toolkit a real
   platform gives; it isn't hand-rolling graph reachability from raw edges.
3. **UI parity.** One shared vocabulary — the UI can always do what
   Claude says.

## Supersedes

This revises the earlier "backend = facts only; correlation lives in the
CLI/consumer toolkit" placement. That placement could not satisfy UI
parity (a C# CLI and a TS UI cannot share one consumer-side
implementation). The no-oracle protection therefore moves from *location*
(keep correlation out of the backend) to **the enumerate/discriminate
boundary + the ban on verdict/composition endpoints** — which is stronger
and location-independent.
