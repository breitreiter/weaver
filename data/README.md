# weaver — exemplar data

The demo's sample telemetry. The **source of truth is text** (`topology.yaml`
+ the generator); the SQLite database is a reproducible build artifact and is
gitignored. We never check a binary `.db` into git — review happens on the
authored YAML, and anyone can regenerate the database from it.

## Files

| Path | Role |
|---|---|
| `topology.yaml` | Authored, human-readable structure of the system: services, dependencies, request routes, and each service's *normal* operating profile. Diffable; this is what code review looks at. |
| `../tools/datagen/generate.py` | Deterministic generator. Reads `topology.yaml`, emits raw observed telemetry into `weaver.db`. |
| `weaver.db` | **Generated** SQLite database (gitignored). Regenerate any time. |

## Regenerate

```bash
python3 tools/datagen/generate.py          # -> data/weaver.db
# options: --topology <path> --out <path> --seed <int> --traces-per-min <n>
```

Requires `pyyaml` (`pip install pyyaml`). Same topology + same `--seed` produces
a byte-identical database.

## What's in the database

Only what an OpenTelemetry collector would actually observe — no analysis, no
labels, no answer baked in:

- `services`, `dependencies`, `request_types` — the observed inventory / config.
- `metric_samples` — narrow/long time series (per service and per edge), 30s cadence.
- `log_events` — operational logs, with an FTS5 full-text index over message/fields.
- `traces` + `spans` — sampled request walks over the routes; `self_ms` per span
  shows which hop spent the time.

### This is the HEALTHY baseline

Everything generated here is nominal: a calm system with realistic noise (a low
background error rate, an occasional slow-op warning, latency tails). There is
**no `status`/`health` column** anywhere — health is something a *consumer*
derives from these metrics, never something the data asserts. The backend reads
this data and serves it; it performs no analysis.

Failure scenarios — perturbing this baseline into a latent, solvable incident —
are a separate, later concern and are intentionally not modeled yet. When they
arrive, the database will still contain only observed telemetry; the "answer"
will live in the data's shape (onset ordering, where `self_ms` accrues, which
logs are specific vs. generic), recoverable only by analysis, never as a field.

## Schema notes

- `metric_samples` is narrow/long (`subject_kind`, `subject_id`, `ts`, `metric`,
  `value`) so any signal over any window is one query. Indexed on
  `(subject_id, metric, ts)`.
- `dependencies.from_service` **depends on** `to_service` (direction is
  load-bearing). `critical` is declared config, not a health signal.
- Timestamps are UTC ISO-8601; ids are stable kebab-case slugs.
