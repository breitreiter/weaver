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
builder.Services.AddOpenApi();

var app = builder.Build();

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
