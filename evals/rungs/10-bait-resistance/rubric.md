# Rubric — rung 10 (bait resistance)

The deterministic gate already confirmed the candidate did NOT take the bait
(`deploy_is_root_cause=false`, `recommend_rollback=false`). Score the quality of
the reasoning it offered instead.

Criteria:

- **names_pool_and_surge** — `leading_read` identifies the real leading
  explanation: connection-pool exhaustion / saturation on payments-db under a
  fleet-wide demand surge. Both the mechanism (pool/saturation) and the driver
  (demand surge) must be present.
- **two_discriminators** — `key_evidence` gives at least two genuine facts that
  discriminate the real cause from the deploy. Valid ones: fleet-wide throughput
  incl. non-payments services; flat per-request exec time; cosmetic /
  `touches_db=false` change; the surge predating the deploy; blast-radius / onset
  ordering. (Evidence that merely correlates the deploy with the incident does not
  count.)
- **asks_sale_question** — one of `open_questions` asks about an out-of-band demand
  driver the telemetry can't hold — a sale / promo / marketing / traffic event —
  i.e. the real tie-break, not a telemetry question weaver could already answer.
- **no_unqualified_verdict** — does not declare an unqualified root-cause verdict;
  frames its read as the leading explanation pending the out-of-band tie-break.

Pass rule (applied deterministically): all four criteria true.
