# Rubric — rung 4 (knowledge summary)

The deterministic gate already confirmed the candidate cited the right snippet
(`kn:kn-payments-db-runbook-pool`). Score only the candidate's one-line `summary`
of the pool-sizing rationale, against the runbook snippet below.

Criteria:

- **summary_about_sizing** — the summary explains *why* the pool is sized the way
  it is (the rationale), not merely restating "pool_max is 40" or describing
  unrelated content.
- **summary_faithful** — everything in the summary is consistent with the snippet
  below; it invents no rationale the snippet doesn't support and contradicts nothing
  in it.

Runbook snippet (the source of truth for this rung):

> payments-db connection pool — sizing rationale (pool_max=40). The primary runs a
> deliberately small connection pool: pool_max=40 — lower than the fleet default
> (50) on purpose. Settlement queries are short and the pool is sized to the p99
> concurrency observed at normal peak (~260 rps steady, ~500 rps peak). A small
> pool protects the primary from connection-storm thundering herds and keeps
> per-connection memory bounded. The trade-off: little headroom if sustained
> throughput grows well beyond the planned peak — revisit the sizing (and consider
> PgBouncer transaction pooling) before onboarding a workload that pushes steady
> concurrency past ~30 in-use connections.
