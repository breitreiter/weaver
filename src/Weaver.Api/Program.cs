using Microsoft.EntityFrameworkCore;
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
    var items = db.BoardItems.AsNoTracking().Where(i => i.BoardId == id).OrderBy(i => i.CreatedAt).ToList().Select(ToBoardItemDto).ToList();
    var edges = db.BoardEdges.AsNoTracking().Where(e => e.BoardId == id).OrderBy(e => e.CreatedAt).ToList().Select(ToBoardEdgeDto).ToList();
    return Results.Ok(new BoardDto(b.Id, b.Title, b.CreatedAt, items, edges));
});

app.MapPost("/api/boards/{id}/items", (BoardsDbContext db, string id, PinReq req) =>
{
    if (db.Boards.FirstOrDefault(x => x.Id == id) is null) return Results.NotFound(new { error = $"unknown board '{id}'" });
    var item = new BoardItemEntity
    {
        Id = NewId(), BoardId = id, Kind = req.Kind, Ref = req.Ref,
        Evidence = req.Evidence?.GetRawText() ?? "{}", Label = req.Label, X = req.X, Y = req.Y, CreatedAt = NowIso(),
    };
    db.BoardItems.Add(item);
    db.SaveChanges();
    return Results.Ok(new CreatedDto(item.Id, $"/view?board={id}"));
});

app.MapPost("/api/boards/{id}/edges", (BoardsDbContext db, string id, LinkReq req) =>
{
    if (db.Boards.FirstOrDefault(x => x.Id == id) is null) return Results.NotFound(new { error = $"unknown board '{id}'" });
    var edge = new BoardEdgeEntity
    {
        Id = NewId(), BoardId = id, FromItem = req.From, ToItem = req.To,
        Kind = req.Kind ?? "causal", Label = req.Label, DrawnBy = req.DrawnBy ?? "agent", CreatedAt = NowIso(),
    };
    db.BoardEdges.Add(edge);
    db.SaveChanges();
    return Results.Ok(new CreatedDto(edge.Id, $"/view?board={id}"));
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
    string? metric, string? from, string? to, string? split, double? z, double? minPct, int? limit) =>
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
            if (!string.IsNullOrWhiteSpace(q)) sq = sq.Where(s => s.Id.Contains(q!) || s.Name.Contains(q!));
            var rows = sq.OrderBy(s => s.Id).Take(lim).ToList();
            return Results.Ok(rows.Select(s => new SearchResultDto(
                "service", "svc:" + s.Id, s.Id, $"{s.Kind} · {s.Subsystem ?? "-"}",
                new { s.Kind, s.Subsystem, s.OwnerTeam }, new PinTargetDto([s.Id], null))).ToList());
        }
        case "anomalies":
        {
            var (series, at) = LoadSeries(db, split);
            var rows = Analysis.Anomalies(series, at, z ?? 3.0, minPct ?? 15.0)
                .Where(a => a.SubjectKind != "service" || pass(a.SubjectId)).Take(lim).ToList();
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
            var m = metric ?? "latency_p99";
            var (series, _) = LoadSeries(db, split);
            var rows = series.Where(s => s.Kind == "service" && s.Metric == m && pass(s.Id)).Take(lim).ToList();
            return Results.Ok(rows.Select(s =>
            {
                var tr = Trajectory.Encode(s.Points.Select(p => p.Val).ToList(), MinutesOf(s.Points), UnitFor(m));
                return new SearchResultDto(
                    "metric", $"me:{s.Id}:{m}", $"{s.Id}  {m}", tr.ShapeCode,
                    new { tr.ShapeCode, tr.Prose, tr.Min, tr.Max, tr.Mean },
                    new PinTargetDto([s.Id], new EvidenceRefDto("metric", m, null, new { tr.ShapeCode, tr.Prose })));
            }).ToList());
        }
        case "changes":
        {
            var rows = ChangeEvents(db, from, to, null)
                .Where(c => c.TargetId is null || pass(c.TargetId)).Take(lim).ToList();
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

static BoardItemDto ToBoardItemDto(BoardItemEntity i) =>
    new(i.Id, i.Kind, i.Ref, ParseJson(i.Evidence), i.Label, i.X, i.Y);
static BoardEdgeDto ToBoardEdgeDto(BoardEdgeEntity e) =>
    new(e.Id, e.FromItem, e.ToItem, e.Kind, e.Label, e.DrawnBy);
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

static string UnitFor(string metric) =>
    metric is "latency_p50" or "latency_p99" ? "ms" : metric == "throughput_rps" ? " rps" : "";

static double MinutesOf(IReadOnlyList<(DateTimeOffset Ts, double Val)> pts) =>
    pts.Count > 1 ? (pts[^1].Ts - pts[0].Ts).TotalMinutes : 120;
