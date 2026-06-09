#!/usr/bin/env python3
"""weaver exemplar data generator — HEALTHY state.

Reads the authored topology (data/topology.yaml) and emits raw, observed
OpenTelemetry-shaped telemetry into a SQLite database:

    services, dependencies, request_types   (the observed inventory / config)
    metric_samples                          (narrow/long time series)
    log_events                              (+ FTS5 index over message/fields)
    traces, spans                           (sampled request walks over routes)

Design rules this script obeys (see project/plans/data-model.md):

  * The DB contains ONLY what a collector would observe. No health/status
    column on services or edges, no anomaly flags, no narrative, no "answer".
    Health is derivable by a consumer; it is never stored.
  * The backend does no analysis. This generator does no analysis either — it
    just produces plausible nominal telemetry for a calm, healthy system.
  * Deterministic: same topology + same --seed => byte-identical DB.

This generator models the HEALTHY baseline only. Failure scenarios are a
separate, later concern and are intentionally not implemented here.
"""

from __future__ import annotations

import argparse
import json
import math
import random
import sqlite3
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

import yaml

# --- metric sets emitted per service kind ---------------------------------
# A service emits exactly the series present in its (kind baseline + override)
# profile. pool_max is a flat constant (a config value), not a moving signal.
COMMON_METRICS = ["latency_p50", "latency_p99", "error_rate", "throughput_rps", "saturation"]
FLAT_METRICS = {"pool_max"}            # emitted but constant
EDGE_METRICS = ["latency_p99", "error_rate", "throughput_rps"]

# generic, kind-appropriate healthy log lines (no incident semantics)
INFO_TEMPLATES = {
    "gateway":  ("http.access",   "{method} {path} -> {status} in {ms}ms"),
    "api":      ("http.handled",  "handled {method} {path} -> {status} in {ms}ms"),
    "worker":   ("job.done",      "processed job {job} in {ms}ms"),
    "db":       ("query.ok",      "query completed in {ms}ms"),
    "cache":    ("cache.hit",     "GET {key} -> hit in {ms}ms"),
    "queue":    ("queue.deliver", "delivered message to {consumer}"),
    "external": ("http.call",     "outbound call -> {status} in {ms}ms"),
}
WARN_TEMPLATE = ("perf.slow", "slow operation: {ms}ms exceeded soft budget")


def iso(ts: datetime) -> str:
    return ts.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def parse_start(s: str) -> datetime:
    return datetime.fromisoformat(s.replace("Z", "+00:00")).astimezone(timezone.utc)


# ---------------------------------------------------------------------------
# Schema
# ---------------------------------------------------------------------------
def build_schema(con: sqlite3.Connection) -> None:
    con.executescript(
        """
        PRAGMA journal_mode = MEMORY;
        PRAGMA synchronous = OFF;

        CREATE TABLE services (
            id          TEXT PRIMARY KEY,
            name        TEXT NOT NULL,
            kind        TEXT NOT NULL,
            subsystem   TEXT,
            owner_team  TEXT
        );

        CREATE TABLE dependencies (
            id           TEXT PRIMARY KEY,
            from_service TEXT NOT NULL REFERENCES services(id),
            to_service   TEXT NOT NULL REFERENCES services(id),
            kind         TEXT NOT NULL,
            critical     INTEGER          -- nullable bool; declared config, not health
        );

        CREATE TABLE request_types (
            id      TEXT PRIMARY KEY,
            name    TEXT NOT NULL,
            weight  REAL NOT NULL,
            path    TEXT NOT NULL          -- json array of service ids
        );

        CREATE TABLE metric_samples (
            subject_kind TEXT NOT NULL,    -- 'service' | 'edge'
            subject_id   TEXT NOT NULL,
            ts           TEXT NOT NULL,
            metric       TEXT NOT NULL,
            value        REAL NOT NULL
        );

        CREATE TABLE log_events (
            id          TEXT PRIMARY KEY,
            service_id  TEXT NOT NULL REFERENCES services(id),
            ts          TEXT NOT NULL,
            level       TEXT NOT NULL,     -- info | warn | error | fatal
            template_id TEXT NOT NULL,
            message     TEXT NOT NULL,
            fields      TEXT NOT NULL       -- json
        );

        CREATE TABLE traces (
            id              TEXT PRIMARY KEY,
            request_type_id TEXT NOT NULL REFERENCES request_types(id),
            root_service_id TEXT NOT NULL REFERENCES services(id),
            started_at      TEXT NOT NULL,
            duration_ms     INTEGER NOT NULL,
            status          TEXT NOT NULL   -- ok | error | timeout (observed outcome)
        );

        CREATE TABLE spans (
            id              TEXT PRIMARY KEY,
            trace_id        TEXT NOT NULL REFERENCES traces(id),
            parent_span_id  TEXT,
            service_id      TEXT NOT NULL REFERENCES services(id),
            edge_id         TEXT REFERENCES dependencies(id),
            kind            TEXT NOT NULL,  -- server | internal | client
            start_offset_ms INTEGER NOT NULL,
            duration_ms     INTEGER NOT NULL,
            self_ms         INTEGER NOT NULL,
            status          TEXT NOT NULL,
            attributes      TEXT NOT NULL   -- json
        );
        """
    )


def build_indexes(con: sqlite3.Connection) -> None:
    con.executescript(
        """
        CREATE INDEX ix_metric_subject ON metric_samples(subject_id, metric, ts);
        CREATE INDEX ix_log_service    ON log_events(service_id, ts);
        CREATE INDEX ix_span_trace     ON spans(trace_id);
        CREATE INDEX ix_trace_route    ON traces(request_type_id, status);

        -- full-text over log text (built-in FTS5, no new dependency)
        CREATE VIRTUAL TABLE log_events_fts USING fts5(
            message, fields, content='log_events', content_rowid='rowid'
        );
        INSERT INTO log_events_fts(rowid, message, fields)
            SELECT rowid, message, fields FROM log_events;
        """
    )


# ---------------------------------------------------------------------------
# Baseline metric model — describes NORMAL operating behaviour
# ---------------------------------------------------------------------------
def service_profile(svc: dict, kind_baselines: dict) -> dict:
    """Merge kind baseline with per-service overrides into one profile dict."""
    prof = dict(kind_baselines[svc["kind"]])
    prof.update(svc.get("metrics") or {})
    return prof


def diurnal(minute: float, total: float) -> float:
    """A gentle load wobble across the window; subtle, healthy."""
    return 1.0 + 0.06 * math.sin(2.0 * math.pi * (minute / max(total, 1)))


def mean_value(profile: dict, metric: str, minute: float, total: float) -> float:
    """The true mean of a metric at a given minute (no per-sample noise yet)."""
    base = profile[metric]
    if metric in FLAT_METRICS:
        return base
    if metric in ("throughput_rps", "saturation", "queue_depth", "pool_used"):
        return base * diurnal(minute, total)
    if metric in ("latency_p50", "latency_p99"):
        return base * (2.0 - diurnal(minute, total))  # busier => slightly slower
    return base  # error_rate: flat mean, noise added per sample


def sample_noise(rng: random.Random, metric: str, mean: float) -> float:
    if metric in FLAT_METRICS:
        return mean
    if metric == "error_rate":
        # multiplicative jitter, floored at 0; occasional tiny blip
        v = mean * rng.uniform(0.4, 1.8)
        return max(0.0, round(v, 5))
    if metric == "saturation":
        return round(min(0.98, max(0.0, mean * rng.uniform(0.9, 1.1))), 4)
    v = mean * rng.gauss(1.0, 0.06)
    return round(max(0.0, v), 3)


# ---------------------------------------------------------------------------
# Generation
# ---------------------------------------------------------------------------
def generate(topology: dict, seed: int, out_path: Path, traces_per_min: int) -> dict:
    rng = random.Random(seed)
    win = topology["window"]
    start = parse_start(win["start"])
    minutes = int(win["minutes"])
    step = int(win["sample_seconds"])
    kind_baselines = topology["kind_baselines"]

    services = topology["services"]
    deps = topology["dependencies"]
    routes = topology["request_types"]

    svc_by_id = {s["id"]: s for s in services}
    profiles = {s["id"]: service_profile(s, kind_baselines) for s in services}
    dep_by_pair = {(d["from_service"], d["to_service"]): d for d in deps}

    n_samples = (minutes * 60) // step + 1
    sample_times = [start + timedelta(seconds=step * i) for i in range(n_samples)]

    con = sqlite3.connect(out_path)
    build_schema(con)

    # --- inventory --------------------------------------------------------
    con.executemany(
        "INSERT INTO services VALUES (?,?,?,?,?)",
        [(s["id"], s["name"], s["kind"], s.get("subsystem"), s.get("owner_team")) for s in services],
    )
    con.executemany(
        "INSERT INTO dependencies VALUES (?,?,?,?,?)",
        [(d["id"], d["from_service"], d["to_service"], d["kind"],
          (1 if d.get("critical") else (0 if "critical" in d else None))) for d in deps],
    )
    con.executemany(
        "INSERT INTO request_types VALUES (?,?,?,?)",
        [(r["id"], r["name"], float(r["weight"]), json.dumps(r["path"])) for r in routes],
    )

    # --- service metric samples ------------------------------------------
    metric_rows = []
    for s in services:
        prof = profiles[s["id"]]
        metrics = [m for m in COMMON_METRICS if m in prof]
        metrics += [m for m in prof if m not in COMMON_METRICS]  # db/queue extras
        for i, ts in enumerate(sample_times):
            minute = i * step / 60.0
            for metric in metrics:
                mean = mean_value(prof, metric, minute, minutes)
                val = sample_noise(rng, metric, mean)
                metric_rows.append(("service", s["id"], iso(ts), metric, val))
    con.executemany("INSERT INTO metric_samples VALUES (?,?,?,?,?)", metric_rows)

    # --- edge metric samples (observed from the caller's side) -----------
    # An edge into `to` reflects the caller's experience of the callee, so it
    # tracks the callee's latency/error means. Throughput scales off the caller.
    edge_rows = []
    for d in deps:
        to_prof = profiles[d["to_service"]]
        from_prof = profiles[d["from_service"]]
        for i, ts in enumerate(sample_times):
            minute = i * step / 60.0
            lat = sample_noise(rng, "latency_p99", mean_value(to_prof, "latency_p99", minute, minutes))
            err = sample_noise(rng, "error_rate", mean_value(to_prof, "error_rate", minute, minutes))
            thr = sample_noise(rng, "throughput_rps",
                               mean_value(from_prof, "throughput_rps", minute, minutes) * 0.45)
            edge_rows.append(("edge", d["id"], iso(ts), "latency_p99", lat))
            edge_rows.append(("edge", d["id"], iso(ts), "error_rate", err))
            edge_rows.append(("edge", d["id"], iso(ts), "throughput_rps", thr))
    con.executemany("INSERT INTO metric_samples VALUES (?,?,?,?,?)", edge_rows)

    # --- logs (mundane, healthy operational lines) -----------------------
    log_rows = []
    paths = ["/", "/products", "/cart", "/checkout", "/search", "/orders", "/login"]
    for s in services:
        prof = profiles[s["id"]]
        tmpl_id, tmpl = INFO_TEMPLATES[s["kind"]]
        err_rate = prof.get("error_rate", 0.003)
        for minute in range(minutes):
            ts0 = start + timedelta(minutes=minute)
            for _ in range(rng.randint(2, 4)):  # a few info lines per minute
                ts = ts0 + timedelta(seconds=rng.uniform(0, 60))
                ms = max(1, int(prof.get("latency_p50", 10) * rng.uniform(0.5, 2.5)))
                fields = {"ms": ms}
                msg = tmpl.format(
                    method=rng.choice(["GET", "POST"]), path=rng.choice(paths),
                    status=200, ms=ms, job=f"job-{rng.randint(1000,9999)}",
                    key=f"k:{rng.randint(1,999)}", consumer="worker", )
                log_rows.append((str(uuid.UUID(int=rng.getrandbits(128))), s["id"], iso(ts),
                                 "info", tmpl_id, msg, json.dumps(fields)))
            if rng.random() < 0.25:  # an occasional slow-op warning
                ts = ts0 + timedelta(seconds=rng.uniform(0, 60))
                ms = int(prof.get("latency_p99", 80) * rng.uniform(1.2, 2.0))
                log_rows.append((str(uuid.UUID(int=rng.getrandbits(128))), s["id"], iso(ts),
                                 "warn", WARN_TEMPLATE[0], WARN_TEMPLATE[1].format(ms=ms),
                                 json.dumps({"ms": ms})))
            if rng.random() < err_rate * 60:  # rare baseline errors
                ts = ts0 + timedelta(seconds=rng.uniform(0, 60))
                log_rows.append((str(uuid.UUID(int=rng.getrandbits(128))), s["id"], iso(ts),
                                 "error", "http.5xx", "unhandled 500 while serving request",
                                 json.dumps({"status": 500})))
    con.executemany("INSERT INTO log_events VALUES (?,?,?,?,?,?,?)", log_rows)

    # --- traces + spans (sampled walks over routes) ----------------------
    trace_rows, span_rows = [], []
    weights = [float(r["weight"]) for r in routes]
    for minute in range(minutes):
        ts0 = start + timedelta(minutes=minute)
        for _ in range(traces_per_min):
            route = rng.choices(routes, weights=weights, k=1)[0]
            started = ts0 + timedelta(seconds=rng.uniform(0, 60))
            minute_f = minute + (started - ts0).total_seconds() / 60.0
            _emit_trace(rng, route, started, minute_f, minutes, svc_by_id,
                        profiles, dep_by_pair, trace_rows, span_rows)
    con.executemany("INSERT INTO traces VALUES (?,?,?,?,?,?)", trace_rows)
    con.executemany("INSERT INTO spans VALUES (?,?,?,?,?,?,?,?,?,?,?)", span_rows)

    build_indexes(con)
    con.commit()

    stats = {
        "services": len(services), "dependencies": len(deps), "request_types": len(routes),
        "metric_samples": len(metric_rows) + len(edge_rows),
        "log_events": len(log_rows), "traces": len(trace_rows), "spans": len(span_rows),
        "window": f"{iso(start)} .. {iso(sample_times[-1])}",
    }
    con.close()
    return stats


def _draw_self_ms(rng: random.Random, profile: dict, minute: float, total: float) -> int:
    """Per-hop self time: usually near p50, with a p99 tail ~5% of the time."""
    p50 = mean_value(profile, "latency_p50", minute, total)
    p99 = mean_value(profile, "latency_p99", minute, total)
    if rng.random() < 0.95:
        return max(0, int(p50 * rng.uniform(0.6, 1.6)))
    return max(0, int(p99 * rng.uniform(0.7, 1.3)))


def _emit_trace(rng, route, started, minute_f, total, svc_by_id, profiles,
                dep_by_pair, trace_rows, span_rows) -> None:
    path = route["path"]
    trace_id = str(uuid.UUID(int=rng.getrandbits(128)))

    # one span per hop; self_ms drawn from each service's latency at this time
    spans = []  # dicts assembled top-down, durations folded bottom-up after
    parent_id = None
    for depth, svc_id in enumerate(path):
        prof = profiles[svc_id]
        edge_id = None if depth == 0 else dep_by_pair[(path[depth - 1], svc_id)]["id"]
        self_ms = _draw_self_ms(rng, prof, minute_f, total)
        # a span occasionally errors at the service's baseline error rate
        status = "error" if rng.random() < prof.get("error_rate", 0.003) else "ok"
        spans.append({
            "id": str(uuid.UUID(int=rng.getrandbits(128))),
            "parent": parent_id, "service": svc_id, "edge": edge_id,
            "kind": "server", "self_ms": self_ms, "status": status,
            "attrs": {},
        })
        parent_id = spans[-1]["id"]

    # fold durations + offsets along the linear chain (leaf -> root)
    child_dur = 0
    child_off = 0
    for depth in range(len(spans) - 1, -1, -1):
        sp = spans[depth]
        sp["duration_ms"] = sp["self_ms"] + child_dur
        child_dur = sp["duration_ms"]
    offset = 0
    for sp in spans:  # root..leaf: each child starts after parent's self work
        sp["start_offset_ms"] = offset
        offset += sp["self_ms"]

    # observed end-user outcome: error if any hop errored (propagates up)
    trace_status = "error" if any(s["status"] == "error" for s in spans) else "ok"
    root = spans[0]
    trace_rows.append((trace_id, route["id"], path[0], iso(started),
                       int(root["duration_ms"]), trace_status))
    for sp in spans:
        span_rows.append((sp["id"], trace_id, sp["parent"], sp["service"], sp["edge"],
                          sp["kind"], int(sp["start_offset_ms"]), int(sp["duration_ms"]),
                          int(sp["self_ms"]), sp["status"], json.dumps(sp["attrs"])))


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate weaver's HEALTHY exemplar SQLite database.")
    repo = Path(__file__).resolve().parents[2]
    ap.add_argument("--topology", type=Path, default=repo / "data" / "topology.yaml")
    ap.add_argument("--out", type=Path, default=repo / "data" / "weaver.db")
    ap.add_argument("--seed", type=int, default=20260608)
    ap.add_argument("--traces-per-min", type=int, default=80)
    args = ap.parse_args()

    topology = yaml.safe_load(args.topology.read_text())
    if args.out.exists():
        args.out.unlink()
    args.out.parent.mkdir(parents=True, exist_ok=True)

    stats = generate(topology, args.seed, args.out, args.traces_per_min)

    print(f"wrote {args.out}")
    for k, v in stats.items():
        print(f"  {k:>16}: {v}")


if __name__ == "__main__":
    main()
