# weaver — Thursday dataset: the frozen data contract

The `.db` is the expensive, churny artifact. This doc freezes **exactly what it
must contain** so we generate it *once*, against a stable target. Upstream of
this: narrative rationale is here; realism mechanics are in
`thursday-dataset-realism.md`; the answer key is the gitignored
`project/private/ground-truth-flash-sale.md`.

Nothing is generated until this is agreed.

## The incident, in one sentence

Customer-support bot reports users can't check out. Checkout is failing because
`payments-db`'s connection pool (`pool_max=40`) is exhausted — driven by a ~2×
**fleet-wide traffic surge** the telemetry can't explain. A `payments-api`
**deploy (v2.4.1) lands at the same instant** and looks guilty; it is innocent.
The data narrows to two suspects and **cannot pick** — resolution needs two facts
only the operator holds: *was a promo/sale running, and what did v2.4.1 change?*

The symptom is **not** stored as an alert. There is no alerts/incidents table.
Investigation begins from the user-facing symptom in the data: `checkout`-route
traces with root `status = error | timeout`. (Why nothing paged is left open —
maybe alerting didn't trip, maybe there is none, maybe it's bad. Out of scope.)

## Schema delta vs. the current generator

Current `generate.py` emits: `services, dependencies, request_types,
metric_samples, log_events, traces, spans` (+ FTS5). HEALTHY only.

**One new table:**

```
change_events
  id            TEXT PRIMARY KEY
  ts            TEXT NOT NULL          -- when it happened
  kind          TEXT NOT NULL          -- deploy | config | migration | feature_flag
  target_id     TEXT                   -- service id it touched (nullable: fleet-wide)
  summary       TEXT NOT NULL          -- "Deployed payments-api v2.4.1"
  fields        TEXT NOT NULL          -- json: {"version":"2.4.1","prev":"2.4.0", ...}
```

Realistic deploy-annotation stream (like a dashboard's vertical deploy lines).
The window carries **several** change_events as ambient noise — routine deploys,
a config tweak, a feature-flag flip on unrelated services — so the one
incident-coincident deploy isn't the *only* marker (that would make it a tell).

**No `db.*` schema change** — the demand-vs-regression discriminator rides in the
existing `spans.attributes` json (currently emitted as `{}`; now populated).

**Surface work this implies** (separate from data gen, but gated by it): a
`GET /api/change-events?from=&to=&target=` endpoint and a `weaver changes` verb,
so the deploy is queryable. The demo script depends on this verb existing.

## Signal choreography (the heart of it)

Timing uses the realism plan's off-round window; call incident onset **T0**.
A clean baseline prefix precedes T0 (the `--split` / `--base` comparison anchor).

**At T0, two things happen together — this coincidence is the whole puzzle:**

1. **Traffic surge.** `throughput_rps` ramps to ~2× over ~3–5 min, **fleet-wide
   and upstream**: `browse`, `search`, `recommend`, `view-cart`, `checkout` all
   rise. Crucially it rises on services that **do not depend on payments**
   (search-api, recommendations, catalog-db). This is the load-bearing tell that
   the trigger is *external demand*, not a payments defect — a payments-api bug
   cannot make people browse and search more. Throughput is the **earliest**
   mover.
2. **Deploy.** `change_events` row + a `payments-api` log line: `deploy.start —
   "starting payments-api v2.4.1"`. Exactly coincident with T0. The red herring.

**The cascade that follows (textbook onset order — points the finger at
payments-db):**

| service        | what moves                                  | onset    |
|----------------|---------------------------------------------|----------|
| (all storefront)| `throughput_rps` ↑ ~2×                      | **T0**   |
| `payments-db`  | `pool_used` → `pool_max` (40); `saturation`↑; `latency_p99`↑ once waits begin | ~T0+4–6m |
| `payments-api` | `latency_p99`↑ (blocked on pool), `error_rate`↑ (timeouts) | ~T0+6–8m |
| `checkout-api` | `error_rate`↑, timeouts on the payments hop | ~T0+8–10m|
| `web-gateway`  | 5xx on the `checkout` route                 | latest   |

`timeline` therefore reads payments-db → payments-api → checkout → gateway: a
clean downstream cascade that **correctly** localizes the manifestation to
payments-db. `blast-radius(payments-db)` covers the symptom set. So far an oracle
would stop and crown payments-db (or, seeing the coincident deploy, crown the
deploy). Both are incomplete.

**The discriminator (why it's demand, not a regression) — observable, not
labeled:**

- On `payments-db` spans during the incident, attribute **`db.pool_wait_ms`**
  inflates (requests *queue* for a free connection), while the query's own
  **`self_ms` stays flat** (each query is as fast as ever *once it gets a
  connection*). Signature = **queueing on a fixed resource**, i.e. capacity, not
  a per-request code regression.
- `pool_used` tracks `throughput_rps` linearly right up to the `pool_max=40`
  ceiling — pool pressure scales with load, exactly as demand (not a leak/regression) predicts.
- Across the v2.4.1 deploy boundary there is **no step-change in per-request
  cost** — `self_ms` and query latency are continuous through T0; only *volume*
  changes. The deploy left no per-request fingerprint.

**Logs (template-set-diff is the smoking gun for the blast region):**

- `payments-db` emits a **novel** template at onset: `db.pool.exhausted —
  "connection pool exhausted: waited {ms}ms for a free connection (pool_max=40)"`.
  Specific, originating.
- `payments-api` / `checkout-api` emit **novel generic** templates:
  `upstream.timeout`, `http.503`. Collateral, not originating.
- These template_ids are **absent from the baseline** — the novelty *is* the
  signal (per `demo-vs-production.md` template-set-diff). The healthy baseline
  must therefore **not** use any of these ids.

## Decoys (must be present, must not be the cause)

Real fleets are noisy; the puzzle needs plausible-but-wrong leads that `anomalies`
will enumerate undifferentiated:

- **Collateral from the surge.** `recommendations`, `search-api` latency rises
  under the 2× load — genuinely anomalous, genuinely not the cause.
- **The intrinsically-noisy baseline service** (realism tell #3): one service that
  wanders/drifts across the *whole* window, incident or not. Must be unrelated to
  payments (e.g. `recommendations` or an `external`).
- **Ambient change_events.** Other deploys/config flips in the window, none
  causal — so "find a deploy near the incident" returns more than one hit.

**Constraint:** none of the realism oddities (noisy service, legacy/weird-named
services from tell #6) may sit on the payments/checkout path, or texture becomes
a label. Verify against the ground-truth.

## The resolution (out-of-band — belongs on the corkboard as a hand-added fact)

The data supports **both** "2× demand overwhelmed an undersized pool" and "the
v2.4.1 deploy regressed something." The tie-break is **not in the telemetry**:

- *Was a promo/sale running?* → marketing doesn't emit OTel. Operator: "flash
  sale launched 14:00." Confirms demand is real.
- *What did v2.4.1 change?* → Operator: "copy/UI only, nothing touching the DB."
  Exonerates the deploy.

Resolved hypothesis (what gets pinned): **payments-db pool exhaustion under a 2×
external demand surge; v2.4.1 is a coincidence; remedy = raise pool / shed load,
NOT roll back.** This is the corkboard — payments-db as suspect, the surge and the
deploy both pinned, a red string to the deploy that gets **crossed out** by the
operator's note.

## Ground truth (gitignored)

`project/private/ground-truth-flash-sale.md` records: the mechanism, the exact
onset times, why each competing explanation is/ isn't supported by which signal,
the two operator facts that resolve it, and a checklist asserting no realism
texture co-locates with the culprit. Never loaded by the API; lets us verify the
puzzle is solvable-but-ambiguous as intended.

## Explicitly out of scope (so we don't churn the schema again)

- No `alerts` / `incidents` tables. No `solve` / `root-cause` verb.
- No new metric tables — discriminator is span attributes only.
- Interior/zoom spans: still deferred (data-model RISK note).

## Generation order, once this is agreed

1. Add `change_events` to the generator schema; emit ambient + the incident deploy.
2. Populate `spans.attributes` (`db.pool_wait_ms`, `self_ms` already present).
3. Implement incident injection: surge curve, pool-exhaustion cascade with lag,
   novel log templates, the flat-self_ms-through-deploy property.
4. Apply realism mechanics (`thursday-dataset-realism.md`).
5. Write `ground-truth-flash-sale.md`; verify solvable-but-ambiguous by running
   the real CLI loop against the new db.
6. Wire `GET /api/change-events` + `weaver changes`; write the demo script
   against verbs that exist.
