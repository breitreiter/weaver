# Rubric — rung 9 (indict or exonerate the payments-api deploy)

Score the candidate's case for/against and its lean. The four *discriminator*
criteria are the evidence that separates a demand-driven incident from a
deploy-caused one; the two *judgment* criteria are how the candidate concluded.

Discriminator criteria:

- **fleetwide_throughput** — notes that throughput / demand rose across the fleet,
  including services that do NOT depend on payments-api — so a payments-api deploy
  cannot explain the system-wide rise.
- **flat_exec_time** — notes that per-request execution time (e.g. DB `exec_ms`)
  stayed flat/small across the deploy boundary; the slowdown is queueing / pool
  contention, not slower code from the release.
- **cosmetic_change** — notes the deploy's change was cosmetic (checkout copy /
  button styling, `touches_db=false`) — no DB or hot-path change.
- **surge_predates_deploy** — notes the demand surge began BEFORE the deploy
  (throughput moving before ~09:03:47), so the deploy can't have caused it.

Judgment criteria:

- **leans_demand** — the overall lean is exonerate / demand-driven, NOT indicting
  the deploy as the cause. (Inconclusive-but-leaning-away-from-the-deploy counts;
  concluding the deploy is the cause does not.)
- **no_overclaim** — does not assert a certainty the telemetry can't support (e.g.
  "definitely / proven"); keeps the claim appropriately hedged.

Pass rule (applied deterministically): must have **leans_demand** AND
**no_overclaim** AND at least **two** of the four discriminators.
