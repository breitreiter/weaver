# weaver — Thursday dataset: realism plan

A second reviewer flagged the current exemplar `data/weaver.db` as obviously
synthetic. The existing db is fine to keep as a **flat healthy exemplar**. This
plan covers the dataset we generate for the Thursday demo, which must (a) carry a
planted incident and (b) not read as machine-generated.

## Two binding constraints (do not break either)

1. **Determinism stays.** `same topology + same --seed => byte-identical DB`
   (`generate.py` docstring). Every fix below is a *seeded* `rng` draw, not
   wall-clock randomness. Jitter ≠ nondeterminism: we replace perfect grids with
   *reproducible* irregularity. Stamp any real timestamps from `args`, never
   `datetime.now()`.
2. **Texture must not become a label.** The reviewer's tells are all about
   *texture* (cadence, noise shape, naming). Adding realism must not leak the
   answer: the one noisy/drifting baseline service we add (tell 3) and the odd
   names (tell 6) must be **decoys/flavor unrelated to the culprit**, or we've
   replaced "too clean" with "the weird one is the cause." Verify against
   `ground-truth-*.md` that none of the new texture co-locates with the planted
   mechanism.

## Prerequisite: incident injection (not yet built)

`generate.py` is HEALTHY-only ("Failure scenarios are a separate, later
concern"). Thursday needs the mystery, so incident injection is the gating work
item; the realism fixes below decorate the substrate the incident rides on. Build
the planted mechanism per `data-model.md` "Generation defaults": calm baseline
prefix → onset at ~T+30m → `payments-db` pool exhaustion cascading up
payments → checkout → gateway with realistic lag; culprit logs specific ("pool
exhausted"), downstream logs generic ("upstream 503 / timeout"); write
`project/private/ground-truth-<incident>.md`. The realism work and incident work
share the same noise/log/trace code paths, so do realism **first** (it makes the
incident's deviations legible against an organic baseline) — or interleave, but
land both before Thursday.

---

## The six tells → concrete fixes

### Tell 1 — mechanically perfect 30s cadence, zero gaps, zero skew

*Where:* `generate(...)` builds `sample_times` as an exact arithmetic grid
shared by every subject (`generate.py:219-220`); `iso()` truncates to whole
seconds.

*Fixes (all seeded):*
- **Per-subject scrape phase + jitter.** Give each service/edge its own scrape
  offset (seeded from its id) and add ±1–4s Gaussian jitter per sample, so
  timestamps land at `14:03:17`, `14:03:46`, … not on the half-minute. Emit
  sub-second precision (`%H:%M:%S.%f` → milliseconds) so ts aren't all whole
  seconds.
- **Dropped scrapes.** With small per-sample probability (~0.5–1%) skip a point;
  occasionally drop 2–3 in a row (a brief collector gap). Different subjects drop
  at different times — no global gaps.
- **Slight clock skew per host.** A fixed small offset per `owner_team`/host so
  not every service shares one clock.
- Cadence itself can vary slightly by signal (metrics 30s, logs event-driven,
  traces already are) — they already differ; keep that.

*Guard:* `anomalies`/`timeline` primitives compare windows; ragged timestamps
must not break their bucketing. Confirm `Analysis.cs` tolerates gaps/jitter
(bucket by floor-to-bucket, not by index) — fix there if it assumes a grid.

### Tell 2 — window starts on a round wall-clock boundary

*Where:* `topology.yaml` `window.start: "2026-06-08T14:00:00Z"`, `minutes: 120`.

*Fix:* off-boundary start (e.g. `2026-06-08T13:47:11Z`) and a non-round duration
(e.g. `minutes: 137`) so the sample count isn't `2N+1`. The incident onset
offset should likewise be odd (e.g. T+34m, not T+30m). Pass `start` via `args`
if we want a fresh timestamp without editing the file, but keep it authored for
diffability per the topology header.

### Tell 3 — noise too dialed-in (symmetric, flat-mean, no autocorrelation, every service single-digit ±)

This is the biggest modeling change. Current model = `mean × iid gauss(1, 0.06)`
(`sample_noise`, `generate.py:187-197`): memoryless, symmetric, identical shape
everywhere.

*Fixes:*
- **Autocorrelation (AR(1) / random walk).** Carry per-(subject,metric) state so
  consecutive samples are correlated — values *drift and wander*, not iid wobble.
  `x_t = mean_t + φ·(x_{t-1} − mean_{t-1}) + ε`. This alone kills the "synthetic
  steady signal" read.
- **Heavy tails / bursts.** Replace pure gauss with occasional multiplicative
  spikes (lognormal tail) on latency/error so p99 has real bursty excursions, not
  symmetric ±.
- **Per-service heterogeneity.** Draw a noise scale and AR coefficient per
  service from its id-seed, so some services are tight and some are chatty.
  Strengthen the diurnal term for a few services (real diurnal drift over the
  window), leave others flat.
- **At least one intrinsically noisy/drifting baseline service** — a "background
  character" (e.g. `recommendations` or a flaky `external`) that wanders or has a
  slow upward creep across the whole window. **MUST be a decoy** unrelated to the
  culprit (constraint 2) — it gives `anomalies` a realistic false positive to
  enumerate, which is exactly the cause-vs-collateral difficulty the design wants.

*Guard:* re-tune the incident's deltas so they still clear the now-noisier
baseline — the mechanism must remain detectable above organic noise.

### Tell 4 — error logs are one identical template, evenly sprinkled, in the first minute

*Where:* baseline errors hard-code one line — `"unhandled 500 while serving
request"`, `template_id="http.5xx"`, `{status:500}` — at flat per-minute prob
(`generate.py:297-301`). Info/warn already vary; errors don't.

*Fixes:*
- **Template library per kind.** A handful of realistic error templates with
  varied `template_id`s and rendered messages (e.g. db: `db.deadlock`,
  `db.conn_timeout`; api: `http.502_upstream`, `validation.reject`,
  `npe.unhandled`; cache: `cache.evict_storm`; external: `ext.timeout`,
  `ext.429`). Vary structured `fields` (status 500/502/503/429, error strings,
  latency, occasionally a short stack frame).
- **Cluster, don't sprinkle.** Real errors burst and concentrate. Tie baseline
  error emission to the (now autocorrelated) `error_rate` series so errors
  cluster in time and on the few services that are momentarily hot, instead of
  one-per-service evenly. Concentrate baseline noise on 2–3 historically-flaky
  services rather than uniformly across 24.
- **Spread across the window**, not the first minute (current code already loops
  per-minute; ensure the burst model doesn't front-load).
- **Reserve the specific incident strings** ("pool exhausted", "upstream 503")
  for the incident generator — baseline gets generic variety, incident gets the
  one distinctive template whose *novelty* is the smoking gun (template-set-diff,
  per `demo-vs-production.md`).

### Tell 5 — suspiciously round counts (24 svc / 10 subsys / 35 deps / 7 routes; 200 logs / 20 traces)

Two separate problems:

**a) Round data counts** (`topology.yaml`). Grow to an irregular total — e.g.
~27–31 services across unevenly-sized subsystems (some subsystems 1 service, one
with 5–6), more routes (10–13), an odd dep count. Add a couple of services that
exist but barely participate (legacy, low traffic) so counts aren't a clean
composite. Folds together with tell 6.

**b) Round API caps** (`Program.cs:92,105`): logs default `limit ?? 200`, traces
`limit ?? 100`. The reviewer's "exactly 200 lines / 20 traces" is partly the cap
returning a round number. Fixes: make the underlying data voluminous enough that
a capped query is obviously a *page* of many (so 200-of-thousands reads as
pagination, not "the dataset is 200"), and/or set non-round defaults and return a
`total`/`hasMore` indicator so a returned count never looks like the whole. This
is an API change, small; pair it with the data-volume bump.

### Tell 6 — textbook topology + too-uniform `<noun>-<suffix>` naming

*Where:* `topology.yaml` services/subsystems — the canonical
edge→storefront→cart/catalog/checkout→payments/orders/fulfillment reference
architecture, every node `*-api/-db/-cache/-worker/-gateway/-bff`.

*Fixes (flavor only — none may correlate with the culprit, constraint 2):*
- **At least one weirdly-named service** — an internal codename
  (`mercury`, `kraken`, `project-atlas`), not a noun-suffix.
- **Inconsistent conventions** — a legacy `svc_payments_v1` still taking a sliver
  of traffic next to the modern `payments-api`; a `pricing` service with no
  suffix; a typo'd or abbreviated name (`inv-db` vs `inventory-db`).
- **A service that doesn't fit the clean tree** — a shared `legacy-monolith` that
  several subsystems still call, or an `analytics-tap` consuming `order-events`
  off to the side. Breaks the tutorial-perfect decomposition.
- **Mismatched ownership** — one service whose `owner_team` doesn't match its
  subsystem (an org seam), since the design already treats `owner_team` as a
  possible analysis signal.
- Keep the culprit (`payments-db`) and the critical money path conventionally
  named so the oddities don't point at the answer.

---

## Verification (before Thursday)

1. **Solvability unchanged.** Regenerate, then run the actual investigation loop
   (anomalies → timeline → blast-radius → logs/traces) and confirm the planted
   mechanism is still recoverable *and* that the new decoy noise (tell 3) and odd
   names (tell 6) produce plausible-but-wrong leads without being the answer.
   Cross-check `ground-truth-*.md`.
2. **Anti-tell pass.** Re-run the reviewer's six checks against the new db:
   timestamps off-grid with gaps? noise autocorrelated + heterogeneous? errors
   varied + clustered? counts irregular? at least one weird name? If a fresh
   reader can still say "this is generated," iterate.
3. **Primitives survive raggedness.** `Analysis.cs` bucketing/baseline-split must
   tolerate jitter, gaps, and off-round windows — add a test over the new db.
4. **Determinism check.** Generate twice with the same seed → byte-identical.

## Sequencing

1. Build incident injection (gating) + `ground-truth` writer.
2. Tell 2 + 5a + 6 — pure `topology.yaml` edits (cheap, high visual payoff).
3. Tell 3 — noise model rewrite in `sample_noise`/`mean_value` (carries state;
   biggest change; re-tune incident deltas after).
4. Tell 1 — timestamp jitter/gaps/skew in `sample_times` + `iso()`.
5. Tell 4 — error template library + burst-clustered emission.
6. Tell 5b — API caps + `total`/`hasMore`.
7. Verification pass; iterate on any surviving tell.
