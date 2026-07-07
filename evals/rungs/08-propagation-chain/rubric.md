# Rubric — rung 8 (propagation chain justification)

The deterministic gate already confirmed the chain array's shape
(`payments-db → payments-api → checkout-api → [storefront-bff] → web-gateway`,
origin first, user-facing last). Score whether the candidate's `hops_justified`
prose (and its transcript, if provided) actually *grounds* the chain rather than
asserting it.

Criteria:

- **hops_are_real_dependencies** — the justification ties the hops to actual
  dependency relationships (e.g. payments-api calls payments-db; checkout-api calls
  payments-api; the path fronts through to web-gateway), citing the dependency /
  blast-radius / relationship facts — not just naming services in an order.
- **onset_consistent** — the justification is consistent with onset ordering: the
  origin (payments-db) degraded first and the failure propagated outward toward the
  user-facing edge, not the reverse.
