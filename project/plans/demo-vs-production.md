# weaver — demo vs. production (deliberate tradeoffs)

Every "we're not doing X" here is a **judgment call, not an oversight.**
The recurring pattern: pick the lean thing the demo's data volume
actually justifies, but design the *seam* so the heavy production option
drops in behind the same interface — no rewrite, just a swap.

Knowing when **not** to reach for the big hammer is the skill this
demonstrates. So the logic lives here, in the open.

## The meta-rule: build the interface, not the infrastructure

Each tradeoff below is safe precisely because the production-grade
option slots in behind an interface we *did* build. We're not skipping
the hard part — we're isolating it so the demo stays honest about its
scale while the architecture stays honest about the real world.

---

## Storage: SQLite, not ClickHouse

- **The big-hammer temptation:** a columnar OLAP store (ClickHouse) for
  telemetry.
- **Demo choice:** SQLite + EF Core, with FTS5 for log text.
- **Why it's right here:** worst case — several hundred services × every
  signal × a bounded window — is single-digit *millions* of rows. SQLite
  with the right indexes answers that in microseconds. Standing up a
  columnar cluster to query that volume is over-engineering, and reads as
  a judgment flag.
- **IRL / the seam:** the selector layer is **store-agnostic.** At real
  trace volumes (billions of spans) you swap the store behind the same
  query interface. ClickHouse stays a live option, not a rewrite. (See
  `data-model.md`.)

## Filtration: a selector grammar, not a search engine

- **The big-hammer temptation:** Lucene / FeatureBase / a dedicated
  search-and-segmentation engine for the "beefy filter."
- **Demo choice:** a composable C# **selector grammar** over SQLite
  (attribute / metric / topology / trace / log predicates).
- **Why it's right here:** the power the brief actually needs lives in the
  *query semantics*, not the engine. At hundreds of nodes, set operations
  and predicate evaluation are trivial in-process. The engine would add
  infra to babysit without adding capability.
- **IRL / the seam:** the selector grammar is the public contract; a real
  search/segmentation backend can implement it at scale unchanged.

## Graph queries: a closed selector vocabulary, not Cypher

- **The big-hammer temptation:** Cypher (the openCypher / GQL lingua
  franca) — a genuinely expressive, standard graph query language.
- **Demo choice:** the small, *closed* selector vocabulary
  (`blast-radius` / `downstream-of` / `on-path-to` / `anomalous` /
  `in-traces` / `log-matches`, composed with `and`/`or`/`not` + `context`).
- **Why it's right here:** two reasons. (1) **Infra:** Cypher implies a
  graph engine (Neo4j) or an embedded interpreter; our graph is hundreds
  of nodes, so traversals are trivial in-process over an adjacency list —
  no query engine, and no impedance mismatch against our relational +
  time-series SQLite. (2) **The bigger one — for an agent-driven surface,
  a constrained vocabulary beats a general language.** Our selectors are
  intention-revealing (`blast-radius(payments-db)` reads as what it
  *means*), terse enough to live in a URL, and offer few ways to write a
  wrong query. Cypher's expressiveness is surface area the agent and the
  human must wield correctly — power we don't need, at the cost of
  legibility we do.
- **IRL / the seam:** Cypher/GQL is the right lingua franca at scale and
  for power users; exposing it over the topology as an advanced escape
  hatch (alongside the selector contract) is a clean live option. We're
  not rejecting its expressiveness — we're choosing a legible, closed
  vocabulary for a small graph driven by a reasoning agent.

## Semantic log search: FTS5 + template-set-diff, not embeddings

- **The big-hammer temptation:** vector embeddings (e.g. Qwen3 on the
  local box) + semantic log search / clustering.
- **Demo choice:** FTS5 keyword search, plus "what log *type* is new since
  the base?" as exact set arithmetic over authored `template_id`s.
- **Why it's right here:** three reasons. (1) Volume is thousands of lines
  from ~a dozen templates. (2) The valuable signal — a *novel* log
  template appearing during the incident — is exact `template_id`
  set-difference (the differential principle, applied to logs), not fuzzy
  similarity. (3) In an LLM-driven workflow **the agent is the semantic
  layer** — it already knows "pool exhausted" ≈ "connection timeout"; a
  separate embedding model is largely redundant with the reasoner at the
  center of the design. Ranking-by-meaning in the backend would also
  violate "the backend is pure observation."
- **IRL / the seam:** at uncontrolled production log volumes with
  freeform text, semantic search + embedding-based template-novelty
  detection is how you cut through. The `logs` tool's interface doesn't
  change — only its implementation.

## Telemetry source: a generated snapshot, not a live pipeline

- **The big-hammer temptation:** real OTel SDK instrumentation, a
  collector, and streaming ingestion (Kafka, etc.).
- **Demo choice:** a deterministic, authored dataset (`data/*.yaml` → a
  generated SQLite) that the backend serves as raw observed telemetry.
- **Why it's right here:** a fixed, diffable, reproducible incident is
  *better* for a demo than live noise — you can rerun it, reason about it,
  and it can't break on stage. The data is synthetic but realistic
  (traces are sampled paths over the real topology — no fake codebase).
- **IRL / the seam:** the backend reads telemetry through a repository
  boundary; production swaps the source from "generated snapshot" to
  "live ingested stream" behind it. The query/observation surface is
  identical.

## Causality: the operator asserts it, not a Pearlian causal model

- **The big-hammer temptation:** formal causal inference — a Pearlian
  causal graph / do-calculus / causal-discovery layer that *computes* "X
  caused Y."
- **Demo choice:** weaver **does not assert causality at all.** It
  enumerates the evidence that bears on cause — onset precedence
  (`timeline`), reachability (`blast-radius`), cause-vs-collateral log
  texture — and the **operator draws the causal conclusion** (the red
  string), bringing domain expertise the tool can't encode.
- **Why it's right here:** building a *real* causal model of a live
  service network is genuinely hard — confounders, non-stationarity, no
  controlled interventions, a topology that changes under you. A faked
  one would be **super fake**, and a confidently-wrong causal claim is
  worse than none: it's the oracle/black-box this project argues against.
  Deferring causality to the operator is the honest design, not a cop-out
  — it's the same enumerate-vs-discriminate line (`analysis-architecture.md`):
  the tool enumerates, the reasoner discriminates.
- **IRL / the seam:** causal inference is a real field, and at scale you'd
  *assist* the operator with it (rank candidate causes, flag likely
  confounders) — but as a **suggestion the human adjudicates**, never an
  asserted verdict. The interface (operator/agent owns the conclusion)
  doesn't change; the assistance behind it can grow.

---

## A different kind of "no"

Not every omission is a scaling tradeoff. **Auth, users, billing, and any
screen beyond the single graph view** are *product* non-goals — out of
scope by design, not infrastructure we'd swap in at scale. Those live in
the constitution's Non-goals, and the honest answer there is just "yes,
obviously, in a real product" — they simply aren't what this demonstrates.

Likewise, **fractal zoom into a node** is not on this list: it's
something we *want* but are deferring on budget (a scope-creep risk), not
something we're declining as overkill. See `view-model.md`.
