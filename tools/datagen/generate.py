#!/usr/bin/env python3
"""weaver exemplar data generator.

Reads an authored topology (data/topology*.yaml) and emits raw, observed
OpenTelemetry-shaped telemetry into a SQLite database:

    services, dependencies, request_types   (the observed inventory / config)
    metric_samples                          (narrow/long time series)
    log_events                              (+ FTS5 index over message/fields)
    traces, spans                           (sampled request walks over routes)
    change_events                           (deploys / config / flags)

Two modes, selected by whether the topology has an `incident:` block:

  * HEALTHY (no incident block, e.g. data/topology.yaml) — a calm, nominal
    system. The original flat exemplar.
  * INCIDENT (e.g. data/topology-flashsale.yaml) — the same baseline, bent by
    an authored mechanism at onset. The generator knows the ground truth so it
    produces a *coherent* failure; it never writes the verdict. See
    project/plans/thursday-dataset-contract.md.

Design rules (see project/plans/data-model.md):
  * The DB contains ONLY what a collector would observe. No health/status
    column, no anomaly flags, no narrative, no "answer". Health is derivable.
  * Deterministic: same topology + same --seed => byte-identical DB.

Realism (project/plans/thursday-dataset-realism.md): per-subject scrape phase +
timestamp jitter + dropped scrapes; autocorrelated (AR(1)), per-service-scaled
noise; a diverse, clustered log-template library; off-round window.
"""

from __future__ import annotations

import argparse
import json
import math
import random
import sqlite3
import uuid
import zlib
from datetime import datetime, timedelta, timezone
from pathlib import Path

import yaml

COMMON_METRICS = ["latency_p50", "latency_p99", "error_rate", "throughput_rps", "saturation"]
FLAT_METRICS = {"pool_max"}                              # emitted but constant
LOAD_METRICS = {"throughput_rps", "pool_used", "queue_depth"}  # scale with demand
EDGE_METRICS = ["latency_p99", "error_rate", "throughput_rps"]

# A diverse, kind-appropriate baseline log library. Errors are varied (not one
# template) and DELIBERATELY exclude the incident template ids below, so the
# incident's templates are genuinely novel-since-baseline (the smoking gun).
INFO_TEMPLATES = {
    "gateway":  ("http.access",   "{method} {path} -> {status} in {ms}ms"),
    "api":      ("http.handled",  "handled {method} {path} -> {status} in {ms}ms"),
    "worker":   ("job.done",      "processed job {job} in {ms}ms"),
    "db":       ("query.ok",      "query completed in {ms}ms"),
    "cache":    ("cache.hit",     "GET {key} -> hit in {ms}ms"),
    "queue":    ("queue.deliver", "delivered message to {consumer}"),
    "external": ("http.call",     "outbound call -> {status} in {ms}ms"),
}
WARN_TEMPLATES = {
    "gateway":  ("perf.slow",        "slow request: {ms}ms exceeded soft budget"),
    "api":      ("perf.slow",        "slow handler: {ms}ms exceeded soft budget"),
    "worker":   ("job.retry",        "job {job} retrying (attempt {n})"),
    "db":       ("db.slow_query",    "slow query {ms}ms: SELECT ... ({rows} rows)"),
    "cache":    ("cache.evict",      "evicted {n} keys under memory pressure"),
    "queue":    ("queue.lag",        "consumer lag {n} messages"),
    "external": ("ext.retry",        "retrying outbound call (attempt {n})"),
}
# Baseline error library — several templates, kind-appropriate, none of which
# are the incident templates. Bursty and concentrated, not evenly sprinkled.
BASE_ERROR_TEMPLATES = {
    "gateway":  [("http.500", "unhandled 500 serving {path}", {"status": 500})],
    "api":      [("http.500", "unhandled 500 in handler {path}", {"status": 500}),
                 ("validation.reject", "rejected request: {field} invalid", {"status": 400})],
    "worker":   [("job.failed", "job {job} failed: {reason}", {"reason": "transient"})],
    "db":       [("db.deadlock", "deadlock detected, transaction rolled back", {"sqlstate": "40P01"}),
                 ("db.conn_reset", "connection reset by peer mid-query", {})],
    "cache":    [("cache.timeout", "GET {key} timed out after {ms}ms", {})],
    "queue":    [("queue.nack", "message {job} nacked, redelivering", {})],
    "external": [("ext.5xx", "provider returned {status}", {"status": 502})],
}
# Reserved for the incident only — must never appear in the baseline.
INC_TEMPLATES = {"db.pool.exhausted", "upstream.timeout", "http.503", "deploy.start"}

PATHS = ["/", "/products", "/cart", "/checkout", "/search", "/orders", "/login"]
FIELDS_REASONS = ["transient", "timeout", "io error"]


def _base_error_line(rng, kind):
    """One baseline error log line for a service kind, as (template_id, message,
    fields_json). Shared by ambient log gen and the correlated-log pivot so the
    two are drawn from the same vocabulary."""
    tmpl, tmsg, tf = rng.choice(BASE_ERROR_TEMPLATES[kind])
    f = dict(tf); f.update(reason=rng.choice(FIELDS_REASONS), path=rng.choice(PATHS),
                           field=rng.choice(["email", "qty", "token"]),
                           ms=rng.randint(50, 900), rows=rng.randint(1, 9000),
                           key=f"k:{rng.randint(1,999)}", job=f"job-{rng.randint(1000,9999)}")
    msg = tmsg.format(**{k: f.get(k, "") for k in ("path", "field", "ms", "key", "job", "reason", "status")})
    return tmpl, msg, json.dumps(tf)


# ---------------------------------------------------------------------------
def iso(ts: datetime) -> str:
    # millisecond precision, fixed width => still lexicographically orderable.
    return ts.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.") + f"{ts.microsecond // 1000:03d}Z"


def parse_start(s: str) -> datetime:
    return datetime.fromisoformat(s.replace("Z", "+00:00")).astimezone(timezone.utc)


def stable_rng(seed: int, *names: object) -> random.Random:
    """A deterministic per-subject RNG (Python's hash() is salted; crc32 isn't)."""
    h = seed & 0xFFFFFFFF
    for n in names:
        h = zlib.crc32(str(n).encode(), h)
    return random.Random(h)


# ---------------------------------------------------------------------------
# Schema
# ---------------------------------------------------------------------------
def build_schema(con: sqlite3.Connection) -> None:
    con.executescript(
        """
        PRAGMA journal_mode = MEMORY;
        PRAGMA synchronous = OFF;

        CREATE TABLE services (
            id TEXT PRIMARY KEY, name TEXT NOT NULL, kind TEXT NOT NULL,
            subsystem TEXT, owner_team TEXT );

        CREATE TABLE dependencies (
            id TEXT PRIMARY KEY, from_service TEXT NOT NULL REFERENCES services(id),
            to_service TEXT NOT NULL REFERENCES services(id), kind TEXT NOT NULL,
            critical INTEGER );

        CREATE TABLE request_types (
            id TEXT PRIMARY KEY, name TEXT NOT NULL, weight REAL NOT NULL, path TEXT NOT NULL );

        CREATE TABLE metric_samples (
            subject_kind TEXT NOT NULL, subject_id TEXT NOT NULL, ts TEXT NOT NULL,
            metric TEXT NOT NULL, value REAL NOT NULL );

        CREATE TABLE log_events (
            id TEXT PRIMARY KEY, service_id TEXT NOT NULL REFERENCES services(id),
            ts TEXT NOT NULL, level TEXT NOT NULL, template_id TEXT NOT NULL,
            message TEXT NOT NULL, fields TEXT NOT NULL,
            trace_id TEXT, span_id TEXT );          -- correlation pivot (nullable)

        CREATE TABLE change_events (
            id TEXT PRIMARY KEY, ts TEXT NOT NULL, kind TEXT NOT NULL,
            target_id TEXT, summary TEXT NOT NULL, fields TEXT NOT NULL );

        CREATE TABLE traces (
            id TEXT PRIMARY KEY, request_type_id TEXT NOT NULL REFERENCES request_types(id),
            root_service_id TEXT NOT NULL REFERENCES services(id), started_at TEXT NOT NULL,
            duration_ms INTEGER NOT NULL, status TEXT NOT NULL );

        CREATE TABLE spans (
            id TEXT PRIMARY KEY, trace_id TEXT NOT NULL REFERENCES traces(id),
            parent_span_id TEXT, service_id TEXT NOT NULL REFERENCES services(id),
            edge_id TEXT REFERENCES dependencies(id), kind TEXT NOT NULL,
            start_offset_ms INTEGER NOT NULL, duration_ms INTEGER NOT NULL,
            self_ms INTEGER NOT NULL, status TEXT NOT NULL, attributes TEXT NOT NULL );
        """
    )


def build_indexes(con: sqlite3.Connection) -> None:
    con.executescript(
        """
        CREATE INDEX ix_metric_subject ON metric_samples(subject_id, metric, ts);
        CREATE INDEX ix_log_service    ON log_events(service_id, ts);
        CREATE INDEX ix_change_ts      ON change_events(ts);
        CREATE INDEX ix_span_trace     ON spans(trace_id);
        CREATE INDEX ix_trace_route    ON traces(request_type_id, status);

        CREATE VIRTUAL TABLE log_events_fts USING fts5(
            message, fields, content='log_events', content_rowid='rowid' );
        INSERT INTO log_events_fts(rowid, message, fields)
            SELECT rowid, message, fields FROM log_events;
        """
    )


# ---------------------------------------------------------------------------
# The incident model — the only thing that bends the baseline.
# ---------------------------------------------------------------------------
class Incident:
    def __init__(self, spec: dict | None):
        self.on = spec is not None
        if not self.on:
            return
        self.onset = float(spec["onset_minute"])
        self.surge_peak = float(spec["surge"]["peak"])
        self.surge_ramp = float(spec["surge"].get("ramp_min", 5))
        self.deploy = spec.get("deploy")
        self.ambient = spec.get("ambient_changes", [])
        # per-service effects, keyed by id; each carries its absolute start minute
        self.effects = {}
        for sid, e in (spec.get("effects") or {}).items():
            self.effects[sid] = {
                "start": self.onset + float(e.get("lag_min", 0)),
                "role": e.get("role", "cascade"),
                "lat": float(e.get("lat_p99_mult", 1.0)),
                "err": float(e.get("err_add", 0.0)),
                "sat_to": e.get("sat_to"),
                "pool_to": e.get("pool_to"),
                "pool_wait_ms": float(e.get("pool_wait_ms", 0)),
            }
        self.drift = {(d["service"], d["metric"]): float(d["drift_pct"])
                      for d in (spec.get("drifters") or [])}

    @staticmethod
    def _ramp(start: float, minute: float, span: float = 3.0) -> float:
        if minute < start:
            return 0.0
        return min(1.0, (minute - start) / max(span, 1e-9))

    def surge(self, minute: float) -> float:
        if not self.on:
            return 1.0
        return 1.0 + (self.surge_peak - 1.0) * self._ramp(self.onset, minute, self.surge_ramp)

    def apply(self, sid: str, metric: str, minute: float, total: float, val: float) -> float:
        """Bend one metric's true mean at a given minute for service `sid`."""
        if not self.on:
            return val
        if metric in LOAD_METRICS:
            val *= self.surge(minute)
        elif metric == "saturation":
            val *= 1.0 + 0.25 * (self.surge(minute) - 1.0)  # gentle load coupling
        eff = self.effects.get(sid)
        if eff:
            r = self._ramp(eff["start"], minute)
            if r > 0:
                if metric == "latency_p99":
                    val *= 1.0 + (eff["lat"] - 1.0) * r
                elif metric == "latency_p50":
                    val *= 1.0 + (eff["lat"] - 1.0) * 0.5 * r
                elif metric == "error_rate":
                    val += eff["err"] * r
                elif metric == "saturation" and eff["sat_to"]:
                    val += (float(eff["sat_to"]) - val) * r
                elif metric == "pool_used" and eff["pool_to"]:
                    val += (float(eff["pool_to"]) - val) * r
        drift = self.drift.get((sid, metric))
        if drift:
            val *= 1.0 + (drift / 100.0) * (minute / max(total, 1))
        return val

    def pool_wait_ms(self, sid: str, minute: float) -> float:
        eff = self.effects.get(sid)
        if not eff or not eff["pool_wait_ms"]:
            return 0.0
        return eff["pool_wait_ms"] * self._ramp(eff["start"], minute)


# ---------------------------------------------------------------------------
# Baseline metric model
# ---------------------------------------------------------------------------
def service_profile(svc: dict, kind_baselines: dict) -> dict:
    prof = dict(kind_baselines[svc["kind"]])
    prof.update(svc.get("metrics") or {})
    return prof


def diurnal(minute: float, total: float) -> float:
    return 1.0 + 0.06 * math.sin(2.0 * math.pi * (minute / max(total, 1)))


def mean_value(prof: dict, metric: str, minute: float, total: float, sid: str, inc: Incident) -> float:
    base = prof[metric]
    if metric in FLAT_METRICS:
        return base
    if metric in LOAD_METRICS:
        val = base * diurnal(minute, total)
    elif metric in ("latency_p50", "latency_p99"):
        val = base * (2.0 - diurnal(minute, total))   # busier => slightly slower
    else:
        val = base                                    # error_rate / saturation: flat mean
    return inc.apply(sid, metric, minute, total, val)


def noisy(rng: random.Random, metric: str, mean: float, resid: float, scale: float) -> tuple[float, float]:
    """AR(1) noise: returns (sampled_value, new_residual). `scale` is per-service."""
    if metric in FLAT_METRICS:
        return mean, 0.0
    sigma = scale * (0.10 if metric == "error_rate" else 0.06)
    resid = 0.55 * resid + rng.gauss(0.0, sigma)       # mean-reverting, autocorrelated
    if metric == "error_rate":
        return max(0.0, round(mean * (1.0 + resid) + rng.uniform(-1e-4, 1e-4), 5)), resid
    if metric == "saturation":
        return round(min(0.985, max(0.0, mean * (1.0 + resid))), 4), resid
    return round(max(0.0, mean * (1.0 + resid)), 3), resid


def noise_scale(sid: str, seed: int) -> float:
    """Per-service noise multiplier — some services are tight, some chatty."""
    return stable_rng(seed, "noise", sid).uniform(0.6, 1.5)


# ---------------------------------------------------------------------------
# Per-subject scrape schedule: a private phase, jittered timestamps, the odd
# dropped scrape. Returns [(minute_float, datetime), ...].
# ---------------------------------------------------------------------------
def scrape_times(sid: str, seed: int, start: datetime, n: int, step: int, total: float):
    r = stable_rng(seed, "scrape", sid)
    phase = r.uniform(-step / 4, step / 4)              # private clock offset
    out = []
    for i in range(n):
        if 0 < i < n - 1 and r.random() < 0.012:        # ~1.2% dropped scrapes
            continue
        jitter = r.gauss(0, step * 0.07)                # a couple seconds of skew
        ts = start + timedelta(seconds=step * i + phase + jitter)
        minute = (ts - start).total_seconds() / 60.0
        out.append((minute, ts))
    return out


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
    inc = Incident(topology.get("incident"))

    services = topology["services"]
    deps = topology["dependencies"]
    routes = topology["request_types"]

    svc_by_id = {s["id"]: s for s in services}
    profiles = {s["id"]: service_profile(s, kind_baselines) for s in services}
    dep_by_pair = {(d["from_service"], d["to_service"]): d for d in deps}
    n_samples = (minutes * 60) // step + 1

    con = sqlite3.connect(out_path)
    build_schema(con)

    con.executemany("INSERT INTO services VALUES (?,?,?,?,?)",
        [(s["id"], s["name"], s["kind"], s.get("subsystem"), s.get("owner_team")) for s in services])
    con.executemany("INSERT INTO dependencies VALUES (?,?,?,?,?)",
        [(d["id"], d["from_service"], d["to_service"], d["kind"],
          (1 if d.get("critical") else (0 if "critical" in d else None))) for d in deps])
    con.executemany("INSERT INTO request_types VALUES (?,?,?,?)",
        [(r["id"], r["name"], float(r["weight"]), json.dumps(r["path"])) for r in routes])

    # --- service metric samples (per series, so AR(1) state is natural) ----
    metric_rows = []
    for s in services:
        sid = s["id"]
        prof = profiles[sid]
        metrics = [m for m in COMMON_METRICS if m in prof] + [m for m in prof if m not in COMMON_METRICS]
        sched = scrape_times(sid, seed, start, n_samples, step, minutes)
        scale = noise_scale(sid, seed)
        for metric in metrics:
            r = stable_rng(seed, "series", sid, metric)
            resid = 0.0
            for minute, ts in sched:
                mean = mean_value(prof, metric, minute, minutes, sid, inc)
                val, resid = noisy(r, metric, mean, resid, scale)
                metric_rows.append(("service", sid, iso(ts), metric, val))
    con.executemany("INSERT INTO metric_samples VALUES (?,?,?,?,?)", metric_rows)

    # --- edge metric samples (the caller's view of the callee) -------------
    edge_rows = []
    for d in deps:
        to, frm = d["to_service"], d["from_service"]
        to_prof, from_prof = profiles[to], profiles[frm]
        sched = scrape_times(d["id"], seed, start, n_samples, step, minutes)
        scale = noise_scale(d["id"], seed)
        states = {m: (stable_rng(seed, "edge", d["id"], m), 0.0) for m in EDGE_METRICS}
        for minute, ts in sched:
            for metric in EDGE_METRICS:
                r, resid = states[metric]
                if metric == "throughput_rps":
                    mean = mean_value(from_prof, metric, minute, minutes, frm, inc) * 0.45
                else:
                    mean = mean_value(to_prof, metric, minute, minutes, to, inc)
                val, resid = noisy(r, metric, mean, resid, scale)
                states[metric] = (r, resid)
                edge_rows.append(("edge", d["id"], iso(ts), metric, val))
    con.executemany("INSERT INTO metric_samples VALUES (?,?,?,?,?)", edge_rows)

    # --- logs (ambient) ----------------------------------------------------
    # Trace emission appends correlated log rows into this same list, so hold
    # the INSERT until after the trace loop below.
    log_rows = _gen_logs(rng, services, profiles, start, minutes, inc)
    kind_by_sid = {s["id"]: s["kind"] for s in services}

    # --- change events -----------------------------------------------------
    change_rows = _gen_changes(start, inc)
    con.executemany("INSERT INTO change_events VALUES (?,?,?,?,?,?)", change_rows)

    # --- traces + spans (+ correlated logs) --------------------------------
    trace_rows, span_rows = [], []
    weights = [float(r["weight"]) for r in routes]
    for minute in range(minutes):
        for _ in range(traces_per_min):
            route = rng.choices(routes, weights=weights, k=1)[0]
            started = start + timedelta(minutes=minute, seconds=rng.uniform(0, 60))
            mf = (started - start).total_seconds() / 60.0
            _emit_trace(rng, route, started, mf, minutes, profiles, dep_by_pair, inc,
                        trace_rows, span_rows, log_rows, kind_by_sid)
    con.executemany("INSERT INTO log_events VALUES (?,?,?,?,?,?,?,?,?)", log_rows)
    con.executemany("INSERT INTO traces VALUES (?,?,?,?,?,?)", trace_rows)
    con.executemany("INSERT INTO spans VALUES (?,?,?,?,?,?,?,?,?,?,?)", span_rows)

    build_indexes(con)
    con.commit()
    last = max(r[2] for r in metric_rows)
    stats = {
        "mode": "incident:" + topology["incident"]["name"] if inc.on else "healthy",
        "services": len(services), "dependencies": len(deps), "request_types": len(routes),
        "metric_samples": len(metric_rows) + len(edge_rows), "log_events": len(log_rows),
        "change_events": len(change_rows), "traces": len(trace_rows), "spans": len(span_rows),
        "window": f"{iso(start)} .. {last}",
    }
    con.close()
    return stats


def _gen_logs(rng, services, profiles, start, minutes, inc: Incident):
    rows = []

    def uid():
        return str(uuid.UUID(int=rng.getrandbits(128)))

    for s in services:
        sid, kind = s["id"], s["kind"]
        prof = profiles[sid]
        info_id, info_t = INFO_TEMPLATES[kind]
        warn_id, warn_t = WARN_TEMPLATES[kind]
        err_rate = prof.get("error_rate", 0.003)
        eff = inc.effects.get(sid) if inc.on else None
        for minute in range(minutes):
            ts0 = start + timedelta(minutes=minute)
            def at():
                return iso(ts0 + timedelta(seconds=rng.uniform(0, 60)))
            for _ in range(rng.randint(2, 4)):                      # info lines
                ms = max(1, int(prof.get("latency_p50", 10) * rng.uniform(0.5, 2.5)))
                msg = info_t.format(method=rng.choice(["GET", "POST"]), path=rng.choice(PATHS),
                                    status=200, ms=ms, job=f"job-{rng.randint(1000,9999)}",
                                    key=f"k:{rng.randint(1,999)}", consumer="worker")
                rows.append((uid(), sid, at(), "info", info_id, msg, json.dumps({"ms": ms})))
            if rng.random() < 0.22:                                 # occasional warn
                ms = int(prof.get("latency_p99", 80) * rng.uniform(1.2, 2.0))
                msg = warn_t.format(ms=ms, n=rng.randint(1, 5), rows=rng.randint(1, 9000),
                                    job=f"job-{rng.randint(1000,9999)}")
                rows.append((uid(), sid, at(), "warn", warn_id, msg, json.dumps({"ms": ms})))
            # baseline errors: bursty (clustered in a minute), varied templates
            if rng.random() < min(0.9, err_rate * 60):
                for _ in range(rng.randint(1, 3)):                  # a little cluster
                    tmpl, msg, fj = _base_error_line(rng, kind)
                    rows.append((uid(), sid, at(), "error", tmpl, msg, fj))

            # --- incident logs: novel templates, only after this svc's onset
            if eff and minute >= eff["start"]:
                ramp = Incident._ramp(eff["start"], minute, 3.0)
                wait = int(inc.pool_wait_ms(sid, minute))
                if eff["role"] == "culprit" and rng.random() < 0.95 * ramp:
                    for _ in range(rng.randint(2, 5)):
                        w = wait + rng.randint(-200, 400)
                        rows.append((uid(), sid, at(), "error", "db.pool.exhausted",
                                     f"connection pool exhausted: waited {max(0,w)}ms for a free connection (pool_max=40)",
                                     json.dumps({"pool_max": 40, "wait_ms": max(0, w), "pool_used": 40})))
                elif sid == "payments-api" and rng.random() < 0.9 * ramp:
                    rows.append((uid(), sid, at(), "error", "upstream.timeout",
                                 f"timeout waiting for payments-db connection after {2000 + rng.randint(0,800)}ms",
                                 json.dumps({"upstream": "payments-db", "timeout_ms": 2000})))
                elif eff["role"] == "cascade" and rng.random() < 0.8 * ramp:
                    rows.append((uid(), sid, at(), "error", "http.503",
                                 "503 Service Unavailable: upstream payments path unavailable",
                                 json.dumps({"status": 503, "route": "checkout"})))

    # deploy.start lines for the (innocent) incident deploy
    if inc.on and inc.deploy:
        d = inc.deploy
        t0 = start + timedelta(minutes=float(d["at_minute"]))
        ver = d.get("fields", {}).get("version", "?")
        for k in range(2):
            rows.append((uid(), d["target"], iso(t0 + timedelta(seconds=rng.uniform(0, 8))),
                         "info", "deploy.start",
                         f"starting {d['target']} {ver} (instance {k+1})",
                         json.dumps(d.get("fields", {}))))
    # ambient logs carry no trace/span id; pad to the correlated 9-tuple shape.
    return [r + (None, None) for r in rows]


def _gen_changes(start, inc: Incident):
    rows = []
    if not inc.on:
        return rows

    def uid(tag):
        return "chg-" + str(uuid.UUID(int=zlib.crc32(tag.encode()) << 32 | zlib.crc32(("x" + tag).encode())))[:18]

    for i, c in enumerate(inc.ambient):
        ts = start + timedelta(minutes=float(c["at_minute"]))
        rows.append((f"chg-amb-{i}", iso(ts), c["kind"], c.get("target"), c["summary"],
                     json.dumps(c.get("fields", {}))))
    if inc.deploy:
        d = inc.deploy
        ts = start + timedelta(minutes=float(d["at_minute"]))
        rows.append(("chg-deploy-payments", iso(ts), "deploy", d["target"], d["summary"],
                     json.dumps(d.get("fields", {}))))
    rows.sort(key=lambda r: r[1])
    return rows


# ---------------------------------------------------------------------------
def _draw_self_ms(rng, prof, minute, total, sid, inc: Incident) -> int:
    p50 = mean_value(prof, "latency_p50", minute, total, sid, inc)
    p99 = mean_value(prof, "latency_p99", minute, total, sid, inc)
    if rng.random() < 0.95:
        return max(0, int(p50 * rng.uniform(0.6, 1.6)))
    return max(0, int(p99 * rng.uniform(0.7, 1.3)))


def _emit_trace(rng, route, started, mf, total, profiles, dep_by_pair, inc: Incident,
                trace_rows, span_rows, log_rows, kind_by_sid):
    path = route["path"]
    trace_id = f"{rng.getrandbits(128):032x}"          # OTel: 32 lowercase hex
    spans = []
    parent_id = None
    for depth, sid in enumerate(path):
        prof = profiles[sid]
        edge_id = None if depth == 0 else dep_by_pair[(path[depth - 1], sid)]["id"]
        attrs = {}
        # The discriminator: at the culprit DB during the incident, self-time is
        # spent WAITING for a pool connection (queueing), not executing — exec is
        # flat, the wait inflates. Capacity, not a per-request code regression.
        wait = int(inc.pool_wait_ms(sid, mf)) if inc.on else 0
        if wait > 0:
            exec_ms = max(1, int(mean_value(prof, "latency_p50", mf, total, sid, Incident(None)) * rng.uniform(0.6, 1.4)))
            jittered_wait = max(0, int(wait * rng.uniform(0.5, 1.3)))
            self_ms = exec_ms + jittered_wait
            attrs = {"db.pool_wait_ms": jittered_wait, "db.exec_ms": exec_ms, "db.pool_max": 40}
        else:
            self_ms = _draw_self_ms(rng, prof, mf, total, sid, inc)

        err = mean_value(prof, "error_rate", mf, total, sid, inc)
        if wait > 1800 and rng.random() < 0.5:
            status = "timeout"
        elif rng.random() < err:
            status = "error"
        else:
            status = "ok"
        spans.append({"id": f"{rng.getrandbits(64):016x}", "parent": parent_id,  # OTel: 16 hex
                      "service": sid, "edge": edge_id, "kind": "server",
                      "self_ms": self_ms, "status": status, "attrs": attrs})
        parent_id = spans[-1]["id"]

    child_dur = 0
    for depth in range(len(spans) - 1, -1, -1):
        spans[depth]["duration_ms"] = spans[depth]["self_ms"] + child_dur
        child_dur = spans[depth]["duration_ms"]
    offset = 0
    for sp in spans:
        sp["start_offset_ms"] = offset
        offset += sp["self_ms"]

    # end-user outcome propagates up: worst of any hop
    statuses = {s["status"] for s in spans}
    trace_status = "timeout" if "timeout" in statuses else ("error" if "error" in statuses else "ok")
    root = spans[0]
    trace_rows.append((trace_id, route["id"], path[0], iso(started), int(root["duration_ms"]), trace_status))
    for sp in spans:
        span_rows.append((sp["id"], trace_id, sp["parent"], sp["service"], sp["edge"], sp["kind"],
                          int(sp["start_offset_ms"]), int(sp["duration_ms"]), int(sp["self_ms"]),
                          sp["status"], json.dumps(sp["attrs"])))

    # --- correlated logs: the pivot. A sampled subset of failed spans emit the
    # log that happened *under* them, carrying this span's trace_id + span_id.
    # Healthy DBs get it too (baseline failures correlate), but every failed
    # payments-db span is force-correlated so the demo pivot never lands dry.
    for sp in spans:
        if sp["status"] not in ("error", "timeout"):
            continue
        sid = sp["service"]
        force = inc.on and sid == "payments-db"
        if not force and rng.random() > 0.4:               # ~40% sampled otherwise
            continue
        ts = iso(started + timedelta(milliseconds=sp["start_offset_ms"]))
        eff = inc.effects.get(sid) if inc.on else None
        if eff and eff["role"] == "culprit":
            w = sp["attrs"].get("db.pool_wait_ms", 0)
            tmpl, msg, fj = ("db.pool.exhausted",
                f"connection pool exhausted: waited {max(0, w)}ms for a free connection (pool_max=40)",
                json.dumps({"pool_max": 40, "wait_ms": max(0, w), "pool_used": 40}))
        elif eff and sid == "payments-api":
            tmpl, msg, fj = ("upstream.timeout",
                f"timeout waiting for payments-db connection after {2000 + rng.randint(0, 800)}ms",
                json.dumps({"upstream": "payments-db", "timeout_ms": 2000}))
        elif eff and eff["role"] == "cascade":
            tmpl, msg, fj = ("http.503",
                "503 Service Unavailable: upstream payments path unavailable",
                json.dumps({"status": 503, "route": "checkout"}))
        else:
            tmpl, msg, fj = _base_error_line(rng, kind_by_sid[sid])
        log_rows.append((str(uuid.UUID(int=rng.getrandbits(128))), sid, ts, "error", tmpl, msg, fj,
                         trace_id, sp["id"]))


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate a weaver exemplar SQLite database.")
    repo = Path(__file__).resolve().parents[2]
    ap.add_argument("--topology", type=Path, default=repo / "data" / "topology.yaml")
    ap.add_argument("--out", type=Path, default=None,
                    help="default: data/weaver.db (healthy) or data/weaver-<incident>.db")
    ap.add_argument("--seed", type=int, default=20260609)
    ap.add_argument("--traces-per-min", type=int, default=80)
    args = ap.parse_args()

    topology = yaml.safe_load(args.topology.read_text())
    out = args.out
    if out is None:
        name = topology.get("incident", {}).get("name") if topology.get("incident") else None
        out = repo / "data" / (f"weaver-{name}.db" if name else "weaver.db")
    if out.exists():
        out.unlink()
    out.parent.mkdir(parents=True, exist_ok=True)

    stats = generate(topology, args.seed, out, args.traces_per_min)
    print(f"wrote {out}")
    for k, v in stats.items():
        print(f"  {k:>16}: {v}")


if __name__ == "__main__":
    main()
