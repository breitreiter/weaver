using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using Weaver.Contracts;
using Weaver.Core;

var builder = WebApplication.CreateBuilder(args);

// Read-only over the generated SQLite. Mode=ReadOnly makes "the backend never
// mutates observed telemetry" a property the runtime enforces, not just a habit.
var dbPath = WeaverDatabase.Locate();
if (!File.Exists(dbPath))
    Console.Error.WriteLine(
        $"[weaver] data file not found at {dbPath}\n" +
        $"[weaver] generate it first:  python3 tools/datagen/generate.py");

builder.Services.AddDbContext<WeaverDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath};Mode=ReadOnly"));

// Boards are writable user content — a separate store, NOT the read-only telemetry DB.
builder.Services.AddDbContext<BoardsDbContext>(o =>
    o.UseSqlite($"Data Source={WeaverDatabase.LocateBoards()}"));

builder.Services.AddOpenApi();

var app = builder.Build();

// Create the boards store if it doesn't exist yet.
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<BoardsDbContext>().Database.EnsureCreated();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// --- index ---------------------------------------------------------------
app.MapGet("/", () => new
{
    name = "weaver",
    db = dbPath,
    surface = "observation only — facts, never verdicts (see analysis-architecture.md)",
    endpoints = new[]
    {
        "GET /api/graph",
        "GET /api/services/{id}",
        "GET /api/metrics?subjectId=&subjectKind=service|edge&metric=&from=&to=",
        "GET /api/logs?serviceId=&level=&q=&from=&to=&limit=",
        "GET /api/traces?route=&status=&minDurationMs=&limit=",
        "GET /api/traces/{id}",
    },
});

// --- topology ------------------------------------------------------------
app.MapGet("/api/graph", (WeaverDbContext db) => new GraphDto(
    db.Services.ToList().Select(ToServiceDto).ToList(),
    db.Dependencies.ToList().Select(ToDepDto).ToList(),
    db.RequestTypes.ToList().Select(ToRouteDto).ToList()));

app.MapGet("/api/services/{id}", (WeaverDbContext db, string id) =>
{
    var s = db.Services.FirstOrDefault(x => x.Id == id);
    if (s is null) return Results.NotFound(new { error = $"unknown service '{id}'" });
    var dependsOn = db.Dependencies.Where(d => d.FromService == id).ToList().Select(ToDepDto).ToList();
    var dependedOnBy = db.Dependencies.Where(d => d.ToService == id).ToList().Select(ToDepDto).ToList();
    return Results.Ok(new ServiceDetailDto(ToServiceDto(s), dependsOn, dependedOnBy));
});

// Real relationships between two on-board nodes — the observed facts the operator
// can ground a red string in when they draw a line. Enumerates (direct dependency,
// shared route, temporal precedence); never crowns one as the cause. Direction
// comes from the data, not the drag.
app.MapGet("/api/relationships", (WeaverDbContext db, string a, string b) =>
{
    if (db.Services.FirstOrDefault(s => s.Id == a) is null) return Results.NotFound(new { error = $"unknown service '{a}'" });
    if (db.Services.FirstOrDefault(s => s.Id == b) is null) return Results.NotFound(new { error = $"unknown service '{b}'" });
    return Results.Ok(new RelationshipsDto(a, b, RelationshipsBetween(db, a, b)));
});

// --- metrics (raw series for one subject; bounded by subjectId) ----------
app.MapGet("/api/metrics", (WeaverDbContext db, string subjectId, string? subjectKind,
    string? metric, string? from, string? to) =>
{
    var kind = subjectKind ?? "service";
    var q = db.MetricSamples.Where(m => m.SubjectId == subjectId && m.SubjectKind == kind);
    if (metric is not null) q = q.Where(m => m.Metric == metric);
    if (from is not null) q = q.Where(m => string.Compare(m.Ts, from) >= 0);
    if (to is not null) q = q.Where(m => string.Compare(m.Ts, to) <= 0);

    var rows = q.OrderBy(m => m.Ts).ToList();
    return rows
        .GroupBy(m => m.Metric)
        .Select(g => new MetricSeriesDto(kind, subjectId, g.Key,
            g.Select(m => new MetricPointDto(m.Ts, m.Value)).ToList()))
        .OrderBy(s => s.Metric)
        .ToList();
});

// --- logs (FTS5 for `q`) -------------------------------------------------
app.MapGet("/api/logs", (WeaverDbContext db, string? serviceId, string? level,
    string? q, string? from, string? to, int? limit) =>
{
    IQueryable<LogEventEntity> query = string.IsNullOrWhiteSpace(q)
        ? db.Logs
        : db.Logs.FromSqlInterpolated($@"
            SELECT le.* FROM log_events le
            JOIN log_events_fts fts ON le.rowid = fts.rowid
            WHERE log_events_fts MATCH {q}");

    if (serviceId is not null) query = query.Where(l => l.ServiceId == serviceId);
    if (level is not null) query = query.Where(l => l.Level == level);
    if (from is not null) query = query.Where(l => string.Compare(l.Ts, from) >= 0);
    if (to is not null) query = query.Where(l => string.Compare(l.Ts, to) <= 0);

    var rows = query.OrderBy(l => l.Ts).Take(limit ?? 200).ToList();
    return rows.Select(ToLogDto).ToList();
});

// --- traces --------------------------------------------------------------
app.MapGet("/api/traces", (WeaverDbContext db, string? route, string? status,
    int? minDurationMs, int? limit) =>
{
    var q = db.Traces.AsQueryable();
    if (route is not null) q = q.Where(t => t.RequestTypeId == route);
    if (status is not null) q = q.Where(t => t.Status == status);
    if (minDurationMs is not null) q = q.Where(t => t.DurationMs >= minDurationMs);

    var rows = q.OrderByDescending(t => t.DurationMs).Take(limit ?? 100).ToList();
    return rows.Select(ToTraceDto).ToList();
});

app.MapGet("/api/traces/{id}", (WeaverDbContext db, string id) =>
{
    var t = db.Traces.FirstOrDefault(x => x.Id == id);
    if (t is null) return Results.NotFound(new { error = $"unknown trace '{id}'" });
    var spans = db.Spans.Where(s => s.TraceId == id)
        .OrderBy(s => s.StartOffsetMs).ToList()
        .Select(ToSpanDto).ToList();
    return Results.Ok(new TraceDetailDto(ToTraceDto(t), spans));
});

// --- analysis primitives (enumerate; never discriminate a cause) ---------
app.MapGet("/api/blast-radius/{node}", (WeaverDbContext db, string node) =>
{
    if (db.Services.FirstOrDefault(s => s.Id == node) is null)
        return Results.NotFound(new { error = $"unknown service '{node}'" });
    var edges = db.Dependencies.Select(d => new { d.FromService, d.ToService }).ToList()
        .Select(e => (e.FromService, e.ToService)).ToList();
    return Results.Ok(Analysis.BlastRadius(edges, node));
});

app.MapGet("/api/anomalies", (WeaverDbContext db, string? split, double? z, double? minPct) =>
{
    var (series, at) = LoadSeries(db, split);
    return Analysis.Anomalies(series, at, z ?? 3.0, minPct ?? 15.0);
});

app.MapGet("/api/timeline", (WeaverDbContext db, string? split, double? z, double? minPct) =>
{
    var (series, at) = LoadSeries(db, split);
    return Analysis.Timeline(series, at, z ?? 3.0, minPct ?? 15.0);
});

// --- boards (writable; co-built by human + agent) ------------------------
app.MapPost("/api/boards", (BoardsDbContext db, CreateBoardReq req) =>
{
    var b = new BoardEntity { Id = NewId(), CreatedAt = NowIso(), Title = string.IsNullOrWhiteSpace(req.Title) ? "untitled" : req.Title! };
    db.Boards.Add(b);
    db.SaveChanges();
    return new CreatedDto(b.Id, $"/view?board={b.Id}");
});

app.MapGet("/api/boards/{id}", (BoardsDbContext db, string id) =>
{
    var b = db.Boards.AsNoTracking().FirstOrDefault(x => x.Id == id);
    if (b is null) return Results.NotFound(new { error = $"unknown board '{id}'" });
    var nodes = db.BoardNodes.AsNoTracking().Where(n => n.BoardId == id).OrderBy(n => n.CreatedAt).ToList();
    var evidence = db.Evidence.AsNoTracking().Where(e => e.BoardId == id).OrderBy(e => e.CreatedAt).ToList()
        .ToLookup(e => e.ServiceId);
    var nodeDtos = nodes.Select(n => new BoardNodeDto(
        n.ServiceId, n.Label, evidence[n.ServiceId].Select(ToEvidenceDto).ToList())).ToList();
    var edges = db.BoardEdges.AsNoTracking().Where(e => e.BoardId == id).OrderBy(e => e.CreatedAt).ToList().Select(ToBoardEdgeDto).ToList();
    return Results.Ok(new BoardDto(b.Id, b.Title, b.CreatedAt, nodeDtos, edges));
});

// Pin = ensure a node per service (idempotent — a service already on the board is
// reused, not duplicated), then layer the evidence (if any) onto the first. A
// trace passes its participant services; they all land, evidence on the subject.
app.MapPost("/api/boards/{id}/pin", (BoardsDbContext db, string id, PinReq req) =>
{
    if (db.Boards.FirstOrDefault(x => x.Id == id) is null) return Results.NotFound(new { error = $"unknown board '{id}'" });
    var services = (req.ServiceIds ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
    if (services.Count == 0) services.Add("(fleet)");

    // the label describes the subject's finding, so only the subject (first service)
    // carries it; trace participants are ensured as bare nodes.
    foreach (var svc in services)
    {
        if (!db.BoardNodes.Any(n => n.BoardId == id && n.ServiceId == svc))
            db.BoardNodes.Add(new BoardNodeEntity
            {
                Id = NewId(), BoardId = id, ServiceId = svc,
                Label = svc == services[0] ? req.Label : null, CreatedAt = NowIso(),
            });
    }
    if (req.Evidence is { } ev)
    {
        db.Evidence.Add(new EvidenceEntity
        {
            Id = NewId(), BoardId = id, ServiceId = services[0],
            Kind = ev.Kind, Aspect = ev.Aspect, At = ev.At,
            Payload = ev.Payload is null ? "{}" : JsonSerializer.Serialize(ev.Payload),
            Label = req.Label, CreatedAt = NowIso(),
        });
    }
    db.SaveChanges();
    return Results.Ok(new CreatedDto(services[0], $"/view?board={id}"));
});

app.MapPost("/api/boards/{id}/edges", (BoardsDbContext db, string id, LinkReq req) =>
{
    if (db.Boards.FirstOrDefault(x => x.Id == id) is null) return Results.NotFound(new { error = $"unknown board '{id}'" });
    var edge = new BoardEdgeEntity
    {
        Id = NewId(), BoardId = id, FromService = req.From, ToService = req.To,
        Kind = req.Kind ?? "causal", Label = req.Label, DrawnBy = req.DrawnBy ?? "agent", CreatedAt = NowIso(),
    };
    db.BoardEdges.Add(edge);
    db.SaveChanges();
    return Results.Ok(new CreatedDto(edge.Id, $"/view?board={id}"));
});

// Cross out (or restore) the red string — the demo's payoff: the operator cuts a
// thread after an out-of-band exoneration. The edge is kept (struck through), not
// deleted, so the reasoning trail stays reviewable.
app.MapPost("/api/boards/{id}/edges/{edgeId}/crossout", (BoardsDbContext db, string id, string edgeId, CrossOutReq req) =>
{
    var edge = db.BoardEdges.FirstOrDefault(e => e.BoardId == id && e.Id == edgeId);
    if (edge is null) return Results.NotFound(new { error = $"unknown edge '{edgeId}'" });
    edge.CrossedOut = req.CrossedOut;
    db.SaveChanges();
    return Results.Ok(ToBoardEdgeDto(edge));
});

app.MapDelete("/api/boards/{id}/edges/{edgeId}", (BoardsDbContext db, string id, string edgeId) =>
{
    var edge = db.BoardEdges.FirstOrDefault(e => e.BoardId == id && e.Id == edgeId);
    if (edge is null) return Results.NotFound(new { error = $"unknown edge '{edgeId}'" });
    db.BoardEdges.Remove(edge);
    db.SaveChanges();
    return Results.Ok(new { ok = true });
});

// Take a single finding off the wall. Unlike the edge crossout (which is kept,
// struck through), a mis-pinned piece of evidence is just removed — it never
// carried reasoning, only an observation.
app.MapDelete("/api/boards/{id}/evidence/{evidenceId}", (BoardsDbContext db, string id, string evidenceId) =>
{
    var ev = db.Evidence.FirstOrDefault(e => e.BoardId == id && e.Id == evidenceId);
    if (ev is null) return Results.NotFound(new { error = $"unknown evidence '{evidenceId}'" });
    db.Evidence.Remove(ev);
    db.SaveChanges();
    return Results.Ok(new { ok = true });
});

// Remove a whole service from the board: its node, every piece of evidence layered
// on it, and any red string touching it. No FK cascade is configured, so each
// table is cleared explicitly.
app.MapDelete("/api/boards/{id}/nodes/{serviceId}", (BoardsDbContext db, string id, string serviceId) =>
{
    var node = db.BoardNodes.FirstOrDefault(n => n.BoardId == id && n.ServiceId == serviceId);
    if (node is null) return Results.NotFound(new { error = $"unknown node '{serviceId}'" });
    db.BoardNodes.Remove(node);
    db.Evidence.RemoveRange(db.Evidence.Where(e => e.BoardId == id && e.ServiceId == serviceId));
    db.BoardEdges.RemoveRange(db.BoardEdges.Where(e => e.BoardId == id && (e.FromService == serviceId || e.ToService == serviceId)));
    db.SaveChanges();
    return Results.Ok(new { ok = true });
});

// --- change events (deploys/config/flags); tolerant if not generated yet --
app.MapGet("/api/change-events", (WeaverDbContext db, string? from, string? to, string? target) =>
    ChangeEvents(db, from, to, target));

// --- search: facets --------------------------------------------------------
app.MapGet("/api/search/facets", (WeaverDbContext db) => new FacetsDto(
    new WindowDto(db.MetricSamples.Min(m => m.Ts)!, db.MetricSamples.Max(m => m.Ts)!),
    db.Services.Where(s => s.Subsystem != null).Select(s => s.Subsystem!).Distinct().OrderBy(x => x).ToList(),
    db.Services.Select(s => s.Kind).Distinct().OrderBy(x => x).ToList(),
    db.Services.Where(s => s.OwnerTeam != null).Select(s => s.OwnerTeam!).Distinct().OrderBy(x => x).ToList(),
    db.MetricSamples.Select(m => m.Metric).Distinct().OrderBy(x => x).ToList(),
    db.Logs.Select(l => l.Level).Distinct().OrderBy(x => x).ToList(),
    db.Logs.Select(l => l.TemplateId).Distinct().OrderBy(x => x).ToList(),
    db.RequestTypes.Select(r => r.Id).OrderBy(x => x).ToList(),
    db.Traces.Select(t => t.Status).Distinct().OrderBy(x => x).ToList(),
    ChangeKinds(db)));

// --- search: structured query -> typed results -----------------------------
app.MapGet("/api/search", (WeaverDbContext db, string? scope, string? q,
    string? subsystem, string? kind, string? team,
    string? level, string? template, string? route, string? status, int? minMs,
    string? metric, string? from, string? to, string? split, double? z, double? minPct, int? limit,
    string? service) =>
{
    var lim = limit ?? 50;
    scope ??= "services";

    HashSet<string>? svc = null;
    if (subsystem is not null || kind is not null || team is not null)
    {
        var sq0 = db.Services.AsQueryable();
        if (subsystem is not null) sq0 = sq0.Where(s => s.Subsystem == subsystem);
        if (kind is not null) sq0 = sq0.Where(s => s.Kind == kind);
        if (team is not null) sq0 = sq0.Where(s => s.OwnerTeam == team);
        svc = sq0.Select(s => s.Id).ToHashSet();
    }
    bool pass(string sid) => svc is null || svc.Contains(sid);

    switch (scope)
    {
        case "services":
        {
            var sq = db.Services.AsQueryable();
            if (subsystem is not null) sq = sq.Where(s => s.Subsystem == subsystem);
            if (kind is not null) sq = sq.Where(s => s.Kind == kind);
            if (team is not null) sq = sq.Where(s => s.OwnerTeam == team);
            if (service is not null) sq = sq.Where(s => s.Id == service);
            if (!string.IsNullOrWhiteSpace(q)) sq = sq.Where(s => s.Id.Contains(q!) || s.Name.Contains(q!));
            var rows = sq.OrderBy(s => s.Id).Take(lim).ToList();
            return Results.Ok(rows.Select(s => new SearchResultDto(
                "service", "svc:" + s.Id, s.Id, $"{s.Kind} · {s.Subsystem ?? "-"}",
                new { s.Kind, s.Subsystem, s.OwnerTeam }, new PinTargetDto([s.Id], null))).ToList());
        }
        case "anomalies":
        {
            var (series, at) = LoadSeries(db, split);
            // Anomalies carry an OnsetTs, so the window filters on *when the anomaly
            // began* — the analog of logs/traces filtering on their own timestamp.
            // Compared as parsed instants: OnsetTs is second-precision (Iso) while
            // from/to arrive at ms-precision, so a raw string compare would be off
            // at second boundaries.
            var fromT = from is null ? (DateTimeOffset?)null : ParseUtc(from);
            var toT = to is null ? (DateTimeOffset?)null : ParseUtc(to);
            var rows = Analysis.Anomalies(series, at, z ?? 3.0, minPct ?? 15.0)
                .Where(a => a.SubjectKind != "service" || pass(a.SubjectId))
                .Where(a => service is null || a.SubjectId == service)
                .Where(a => OnsetInWindow(a.OnsetTs, fromT, toT)).Take(lim).ToList();
            return Results.Ok(rows.Select(a => new SearchResultDto(
                "anomaly", $"an:{a.SubjectId}:{a.Metric}",
                $"{a.SubjectId}  {a.Metric}  {(a.DeltaPct > 0 ? "+" : "")}{a.DeltaPct}%",
                $"z {a.Z} · {a.Direction} · onset {a.OnsetTs}", a,
                new PinTargetDto([a.SubjectId], new EvidenceRefDto("anomaly", a.Metric, a.OnsetTs, a)))).ToList());
        }
        case "logs":
        {
            IQueryable<LogEventEntity> lq = string.IsNullOrWhiteSpace(q)
                ? db.Logs
                : db.Logs.FromSqlInterpolated($@"
                    SELECT le.* FROM log_events le
                    JOIN log_events_fts f ON le.rowid = f.rowid
                    WHERE log_events_fts MATCH {q}");
            if (service is not null) lq = lq.Where(l => l.ServiceId == service);
            if (level is not null) lq = lq.Where(l => l.Level == level);
            if (template is not null) lq = lq.Where(l => l.TemplateId == template);
            if (from is not null) lq = lq.Where(l => string.Compare(l.Ts, from) >= 0);
            if (to is not null) lq = lq.Where(l => string.Compare(l.Ts, to) <= 0);
            var rows = lq.OrderByDescending(l => l.Ts).Take(lim).ToList().Where(l => pass(l.ServiceId));
            return Results.Ok(rows.Select(l => new SearchResultDto(
                "log", "log:" + l.Id, l.Message, $"{l.Level} · {l.ServiceId} · {l.Ts}", ToLogDto(l),
                new PinTargetDto([l.ServiceId], new EvidenceRefDto("log", l.TemplateId, l.Ts, ToLogDto(l))))).ToList());
        }
        case "traces":
        {
            var tq = db.Traces.AsQueryable();
            if (route is not null) tq = tq.Where(t => t.RequestTypeId == route);
            if (status is not null) tq = tq.Where(t => t.Status == status);
            if (minMs is not null) tq = tq.Where(t => t.DurationMs >= minMs);
            if (from is not null) tq = tq.Where(t => string.Compare(t.StartedAt, from) >= 0);
            if (to is not null) tq = tq.Where(t => string.Compare(t.StartedAt, to) <= 0);
            // a trace "belongs to" a service if any of its spans touch that service —
            // this is how the traces button pulls in NODES adjacent to the one on the board.
            if (service is not null) tq = tq.Where(t => db.Spans.Any(s => s.TraceId == t.Id && s.ServiceId == service));
            var rows = tq.OrderByDescending(t => t.DurationMs).Take(lim).ToList();
            return Results.Ok(rows.Select(t =>
            {
                var spans = db.Spans.Where(s => s.TraceId == t.Id).OrderByDescending(s => s.SelfMs).ToList();
                var hot = spans.FirstOrDefault();
                var nodeIds = spans.Select(s => s.ServiceId).Distinct().ToList();
                return new SearchResultDto(
                    "trace", "tr:" + t.Id, $"{t.RequestTypeId}  {t.DurationMs}ms  {t.Status}",
                    hot is not null ? $"hot hop {hot.ServiceId} ({hot.SelfMs}ms self)" : t.Id[..8],
                    new { trace = ToTraceDto(t), spans = spans.Select(ToSpanDto) },
                    new PinTargetDto(nodeIds, new EvidenceRefDto("trace", "route:" + t.RequestTypeId, t.StartedAt,
                        new { trace = ToTraceDto(t), hot = hot is not null ? ToSpanDto(hot) : null })));
            }).ToList());
        }
        case "metrics":
        {
            var (series, _) = LoadSeries(db, split);
            var sel = series.Where(s => s.Kind == "service" && pass(s.Id));
            if (service is not null) sel = sel.Where(s => s.Id == service);
            // scoped to one service → list ALL its metric series; browsing across
            // services → pick one metric (the facet, default latency_p99).
            if (metric is not null) sel = sel.Where(s => s.Metric == metric);
            else if (service is null) sel = sel.Where(s => s.Metric == "latency_p99");
            var rows = sel.OrderBy(s => s.Id).ThenBy(s => s.Metric).Take(lim).ToList();
            return Results.Ok(rows.Select(s =>
            {
                var tr = Trajectory.Encode(s.Points.Select(p => p.Val).ToList(), MinutesOf(s.Points), UnitFor(s.Metric));
                return new SearchResultDto(
                    "metric", $"me:{s.Id}:{s.Metric}", $"{s.Id}  {s.Metric}", tr.ShapeCode,
                    new { tr.ShapeCode, tr.Prose, tr.Min, tr.Max, tr.Mean },
                    new PinTargetDto([s.Id], new EvidenceRefDto("metric", s.Metric, null, new { tr.ShapeCode, tr.Prose })));
            }).ToList());
        }
        case "changes":
        {
            var rows = ChangeEvents(db, from, to, null)
                .Where(c => c.TargetId is null || pass(c.TargetId))
                .Where(c => service is null || c.TargetId == service).Take(lim).ToList();
            return Results.Ok(rows.Select(c => new SearchResultDto(
                "change", "ce:" + c.Id, c.Summary,
                $"{c.Kind} · {c.Ts}" + (c.TargetId is not null ? " · " + c.TargetId : " · fleet-wide"), c,
                new PinTargetDto(c.TargetId is not null ? [c.TargetId] : [],
                    new EvidenceRefDto("change", c.Kind, c.Ts, c)))).ToList());
        }
        default:
            return Results.BadRequest(new { error = $"unknown scope '{scope}' (services|anomalies|logs|traces|metrics|changes)" });
    }
});

// --- search: volume histogram (honest counts over the FULL matching set) ----
// Mirrors the /api/search filters per scope, but COUNTS every matching row
// bucketed by time — never the capped, sorted result page. This is the only
// honest source for the chart-wall volume layer (logs|traces|changes); binning
// the search page would chart "the top 60", not the volume.
app.MapGet("/api/search/histogram", (WeaverDbContext db, string? scope, string? q,
    string? subsystem, string? kind, string? team,
    string? level, string? template, string? route, string? status, int? minMs,
    string? from, string? to, long? bucketMs) =>
{
    scope ??= "logs";
    if (scope is not ("logs" or "traces" or "changes"))
        return Results.BadRequest(new { error = $"histogram supports logs|traces|changes (got '{scope}')" });

    // subsystem/kind/team facet -> set of service ids (mirrors /api/search `pass`)
    HashSet<string>? svc = null;
    if (subsystem is not null || kind is not null || team is not null)
    {
        var sq = db.Services.AsQueryable();
        if (subsystem is not null) sq = sq.Where(s => s.Subsystem == subsystem);
        if (kind is not null) sq = sq.Where(s => s.Kind == kind);
        if (team is not null) sq = sq.Where(s => s.OwnerTeam == team);
        svc = sq.Select(s => s.Id).ToHashSet();
    }

    // pull only the timestamps of the full matching set — no limit, no ordering
    List<string> stamps;
    switch (scope)
    {
        case "logs":
        {
            IQueryable<LogEventEntity> lq = string.IsNullOrWhiteSpace(q)
                ? db.Logs
                : db.Logs.FromSqlInterpolated($@"
                    SELECT le.* FROM log_events le
                    JOIN log_events_fts f ON le.rowid = f.rowid
                    WHERE log_events_fts MATCH {q}");
            if (level is not null) lq = lq.Where(l => l.Level == level);
            if (template is not null) lq = lq.Where(l => l.TemplateId == template);
            if (from is not null) lq = lq.Where(l => string.Compare(l.Ts, from) >= 0);
            if (to is not null) lq = lq.Where(l => string.Compare(l.Ts, to) <= 0);
            stamps = lq.Select(l => new { l.Ts, l.ServiceId }).ToList()
                .Where(r => svc is null || svc.Contains(r.ServiceId)).Select(r => r.Ts).ToList();
            break;
        }
        case "traces":
        {
            // traces expose only route/status/minMs facets (no subsystem) — mirror that
            var tq = db.Traces.AsQueryable();
            if (route is not null) tq = tq.Where(t => t.RequestTypeId == route);
            if (status is not null) tq = tq.Where(t => t.Status == status);
            if (minMs is not null) tq = tq.Where(t => t.DurationMs >= minMs);
            if (from is not null) tq = tq.Where(t => string.Compare(t.StartedAt, from) >= 0);
            if (to is not null) tq = tq.Where(t => string.Compare(t.StartedAt, to) <= 0);
            stamps = tq.Select(t => t.StartedAt).ToList();
            break;
        }
        default: // changes — fleet-wide always pass; targeted pass the svc facet
        {
            stamps = ChangeEvents(db, from, to, null)
                .Where(c => c.TargetId is null || svc is null || svc.Contains(c.TargetId))
                .Select(c => c.Ts).ToList();
            break;
        }
    }

    // window: explicit from/to, else the global telemetry window so stacked
    // volume panels share one time axis by default.
    var winStart = from ?? db.MetricSamples.Min(m => m.Ts)!;
    var winEnd = to ?? db.MetricSamples.Max(m => m.Ts)!;
    var startMs = DateTimeOffset.Parse(winStart, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
    var endMs = DateTimeOffset.Parse(winEnd, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
    if (endMs <= startMs) endMs = startMs + 1;

    var bucket = bucketMs ?? NiceBucketMs((endMs - startMs) / 60.0);
    if (bucket < 1) bucket = 1;
    var n = Math.Min(2000, (int)Math.Ceiling((endMs - startMs) / (double)bucket));
    if (n < 1) n = 1;

    var counts = new int[n];
    foreach (var s in stamps)
    {
        var ms = DateTimeOffset.Parse(s, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        if (ms < startMs || ms > endMs) continue;
        var idx = (int)((ms - startMs) / bucket);
        if (idx >= n) idx = n - 1;
        counts[idx]++;
    }

    var buckets = new List<HistogramBucketDto>(n);
    for (var i = 0; i < n; i++)
    {
        var bms = startMs + (long)i * bucket;
        buckets.Add(new HistogramBucketDto(
            DateTimeOffset.FromUnixTimeMilliseconds(bms).UtcDateTime
                .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            bms, counts[i]));
    }

    return Results.Ok(new HistogramDto(scope, new WindowDto(winStart, winEnd), bucket, counts.Sum(), buckets));
});

// --- node evidence dossier (powers the evidence panel + the pin menu) ------
app.MapGet("/api/nodes/{id}/evidence", (WeaverDbContext db, string id, string? from, string? to) =>
{
    var s = db.Services.FirstOrDefault(x => x.Id == id);
    if (s is null) return Results.NotFound(new { error = $"unknown service '{id}'" });
    var f = from ?? db.MetricSamples.Min(m => m.Ts)!;
    var t = to ?? db.MetricSamples.Max(m => m.Ts)!;

    var samples = db.MetricSamples
        .Where(m => m.SubjectKind == "service" && m.SubjectId == id
            && string.Compare(m.Ts, f) >= 0 && string.Compare(m.Ts, t) <= 0)
        .OrderBy(m => m.Ts).ToList();
    var signals = samples.GroupBy(m => m.Metric).Where(g => g.Key != "pool_max")
        .Select(g =>
        {
            var pts = g.Select(x => (DateTimeOffset.Parse(x.Ts), x.Value)).ToList();
            var tr = Trajectory.Encode(g.Select(x => x.Value).ToList(), MinutesOf(pts), UnitFor(g.Key));
            return new NodeSignalDto(g.Key, tr.ShapeCode, tr.Prose);
        }).OrderBy(x => x.Metric).ToList();

    var logGroups = db.Logs
        .Where(l => l.ServiceId == id && string.Compare(l.Ts, f) >= 0 && string.Compare(l.Ts, t) <= 0)
        .ToList()
        .GroupBy(l => (l.Level, l.TemplateId))
        .Select(g => new NodeLogGroupDto(g.Key.TemplateId, g.Key.Level, g.Count(), g.First().Message))
        .OrderByDescending(x => x.Count).ToList();

    var changes = ChangeEvents(db, f, t, id);
    var traceCount = db.Spans.Where(sp => sp.ServiceId == id).Select(sp => sp.TraceId).Distinct().Count();

    return Results.Ok(new NodeEvidenceDto(ToServiceDto(s), new WindowDto(f, t), signals, logGroups, changes, traceCount));
});

app.Run();

// --- projections (entity -> wire DTO) ------------------------------------
static ServiceDto ToServiceDto(ServiceEntity s) => new(s.Id, s.Name, s.Kind, s.Subsystem, s.OwnerTeam);
static DependencyDto ToDepDto(DependencyEntity d) => new(d.Id, d.FromService, d.ToService, d.Kind, d.Critical);
static RequestTypeDto ToRouteDto(RequestTypeEntity r) =>
    new(r.Id, r.Name, r.Weight, JsonSerializer.Deserialize<string[]>(r.Path) ?? []);
static LogEventDto ToLogDto(LogEventEntity l) =>
    new(l.Id, l.ServiceId, l.Ts, l.Level, l.TemplateId, l.Message, ParseJson(l.Fields));
static TraceDto ToTraceDto(TraceEntity t) =>
    new(t.Id, t.RequestTypeId, t.RootServiceId, t.StartedAt, t.DurationMs, t.Status);
static SpanDto ToSpanDto(SpanEntity s) =>
    new(s.Id, s.ParentSpanId, s.ServiceId, s.EdgeId, s.Kind, s.StartOffsetMs, s.DurationMs, s.SelfMs, s.Status, ParseJson(s.Attributes));
static JsonElement ParseJson(string s) =>
    JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(s) ? "{}" : s);

// The search window arrives timezone-naive from the picker (the UTC wall-clock
// the facet bounds are drawn from), so read it as UTC rather than server-local.
static DateTimeOffset ParseUtc(string s) => DateTimeOffset.Parse(
    s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

// Does an anomaly's onset fall in [from, to]? No bounds → everything passes.
// A bounded window can't place an onset-less anomaly, so those drop out.
static bool OnsetInWindow(string? onset, DateTimeOffset? from, DateTimeOffset? to)
{
    if (from is null && to is null) return true;
    if (onset is null) return false;
    var t = ParseUtc(onset);
    return (from is null || t >= from) && (to is null || t <= to);
}

// Load every metric series, plus the comparison split (default: 30% into the
// observed window). The whole dataset is small enough to group in memory.
static (List<Analysis.SeriesInput> series, DateTimeOffset split) LoadSeries(WeaverDbContext db, string? split)
{
    var rows = db.MetricSamples.OrderBy(m => m.Ts).ToList();
    var series = rows
        .GroupBy(m => (m.SubjectKind, m.SubjectId, m.Metric))
        .Select(g => new Analysis.SeriesInput(g.Key.SubjectKind, g.Key.SubjectId, g.Key.Metric,
            g.Select(x => (DateTimeOffset.Parse(x.Ts), x.Value)).ToList()))
        .ToList();

    DateTimeOffset at;
    if (split is not null) at = DateTimeOffset.Parse(split);
    else
    {
        var min = DateTimeOffset.Parse(rows[0].Ts);
        var max = DateTimeOffset.Parse(rows[^1].Ts);
        at = min + (max - min) * 0.3;
    }
    return (series, at);
}

static EvidenceItemDto ToEvidenceDto(EvidenceEntity e) =>
    new(e.Id, e.Kind, e.Aspect, e.At, ParseJson(e.Payload), e.Label);
static BoardEdgeDto ToBoardEdgeDto(BoardEdgeEntity e) =>
    new(e.Id, e.FromService, e.ToService, e.Kind, e.Label, e.DrawnBy, e.CrossedOut);
static string NewId() => Guid.NewGuid().ToString("N")[..8];
static string NowIso() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

static ChangeEventDto ToChangeDto(ChangeEventEntity c) =>
    new(c.Id, c.Ts, c.Kind, c.TargetId, c.Summary, ParseJson(c.Fields));

// change_events may not be in the DB yet (the generator adds it later) — guard so
// the API works against the current healthy baseline.
static List<ChangeEventDto> ChangeEvents(WeaverDbContext db, string? from, string? to, string? target)
{
    try
    {
        var q = db.ChangeEvents.AsQueryable();
        if (from is not null) q = q.Where(c => string.Compare(c.Ts, from) >= 0);
        if (to is not null) q = q.Where(c => string.Compare(c.Ts, to) <= 0);
        if (target is not null) q = q.Where(c => c.TargetId == target);
        return q.OrderBy(c => c.Ts).ToList().Select(ToChangeDto).ToList();
    }
    catch (Microsoft.Data.Sqlite.SqliteException) { return []; }
}

static List<string> ChangeKinds(WeaverDbContext db)
{
    try { return db.ChangeEvents.Select(c => c.Kind).Distinct().OrderBy(x => x).ToList(); }
    catch (Microsoft.Data.Sqlite.SqliteException) { return []; }
}

// Enumerate the observed relationships between two services — facts only, no
// ranking. Direct dependencies (either direction), shared request-type paths,
// and temporal anomaly precedence. Direction is taken from the data, not the
// drag, so the drawn edge points the way the fact does.
static List<RelationshipDto> RelationshipsBetween(WeaverDbContext db, string a, string b)
{
    var rels = new List<RelationshipDto>();

    // 1. direct dependencies (either direction) — the strongest "real edge".
    foreach (var d in db.Dependencies
        .Where(d => (d.FromService == a && d.ToService == b) || (d.FromService == b && d.ToService == a))
        .ToList())
    {
        var crit = d.Critical == true ? " · critical" : "";
        rels.Add(new RelationshipDto("dependency", d.FromService, d.ToService, "dependency",
            $"{d.Kind}{crit}",
            $"{d.FromService} calls {d.ToService} directly ({d.Kind})",
            $"depends on ({d.Kind})",
            new { d.Id, d.Kind, d.Critical }));
    }

    // 2. shared request-type paths — both services sit on the same route. Collapse
    // every shared route into ONE relationship (two hub services co-occur on many
    // routes; a row each is noise). Direction follows the path order, taken from the
    // majority of shared routes.
    var shared = db.RequestTypes.ToList()
        .Select(r => new { r, path = JsonSerializer.Deserialize<string[]>(r.Path) ?? [] })
        .Select(x => new { x.r, ia = Array.IndexOf(x.path, a), ib = Array.IndexOf(x.path, b), hops = x.path.Length })
        .Where(x => x.ia >= 0 && x.ib >= 0)
        .ToList();
    if (shared.Count > 0)
    {
        var aBefore = shared.Count(x => x.ia <= x.ib);
        var (from, to) = aBefore * 2 >= shared.Count ? (a, b) : (b, a);
        var names = shared.Select(x => x.r.Name).ToList();
        var preview = string.Join(", ", names.Take(3)) + (names.Count > 3 ? $", +{names.Count - 3} more" : "");
        rels.Add(new RelationshipDto("route", from, to, "route",
            shared.Count == 1 ? $"on route {names[0]}" : $"on {shared.Count} shared routes",
            $"{from} precedes {to} on {preview}",
            shared.Count == 1 ? $"co-located on {names[0]}" : $"shares {shared.Count} routes",
            new { count = shared.Count, routes = shared.Select(x => new { x.r.Id, x.r.Name, x.hops }) }));
    }

    // 3. temporal precedence — both moved; order by anomaly onset. Reading
    // precedence as causation stays the operator's call, so this draws a red string.
    var (series, at) = LoadSeries(db, null);
    var onsets = Analysis.Anomalies(series.Where(s => s.Kind == "service" && (s.Id == a || s.Id == b)), at, 3.0, 15.0)
        .Where(x => x.OnsetTs is not null)
        .GroupBy(x => x.SubjectId)
        .ToDictionary(g => g.Key, g => g.OrderBy(x => x.OnsetTs, StringComparer.Ordinal).First());
    if (onsets.TryGetValue(a, out var oa) && onsets.TryGetValue(b, out var ob))
    {
        var (first, second) = string.Compare(oa.OnsetTs, ob.OnsetTs, StringComparison.Ordinal) <= 0 ? (oa, ob) : (ob, oa);
        var dm = Math.Round((DateTimeOffset.Parse(second.OnsetTs!) - DateTimeOffset.Parse(first.OnsetTs!)).TotalMinutes);
        rels.Add(new RelationshipDto("temporal", first.SubjectId, second.SubjectId, "temporal",
            dm > 0 ? $"moved {dm:0}m before {second.SubjectId}" : $"moved together with {second.SubjectId}",
            $"{first.SubjectId} {first.Metric} shifted {first.OnsetTs}; {second.SubjectId} {second.Metric} at {second.OnsetTs}",
            dm > 0 ? $"preceded by {dm:0}m" : "co-onset",
            new
            {
                first = new { first.SubjectId, first.Metric, first.OnsetTs },
                second = new { second.SubjectId, second.Metric, second.OnsetTs },
                deltaMin = dm,
            }));
    }

    return rels;
}

// snap a raw bucket width to the nearest human-friendly value (~60 buckets/window)
static long NiceBucketMs(double rawMs)
{
    long[] ladder =
    [
        1000, 2000, 5000, 10_000, 15_000, 30_000,        // 1s .. 30s
        60_000, 120_000, 300_000, 600_000, 900_000, 1_800_000,  // 1m .. 30m
        3_600_000, 7_200_000, 21_600_000, 86_400_000,    // 1h .. 1d
    ];
    var best = ladder[0];
    foreach (var step in ladder)
        if (Math.Abs(step - rawMs) < Math.Abs(best - rawMs)) best = step;
    return best;
}

static string UnitFor(string metric) =>
    metric is "latency_p50" or "latency_p99" ? "ms" : metric == "throughput_rps" ? " rps" : "";

static double MinutesOf(IReadOnlyList<(DateTimeOffset Ts, double Val)> pts) =>
    pts.Count > 1 ? (pts[^1].Ts - pts[0].Ts).TotalMinutes : 120;
