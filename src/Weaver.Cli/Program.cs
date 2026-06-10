using System.Net;
using System.Text;
using System.Text.Json;
using Weaver.Contracts;
using Weaver.Core;

// weaver — the agent's CLI surface. A pure client of Weaver.Api (everything it
// does is reproducible over HTTP). Observation verbs only, for now; output
// follows project/plans/cli.md (native English, summaries by default, the
// trajectory encoder for series, inline next-move hints).

var argv = new ArgParser(args);
var verb = argv.Verb;
var api = new Api(Environment.GetEnvironmentVariable("WEAVER_API") ?? "http://localhost:5180");

try
{
    switch (verb)
    {
        case "graph": Graph(); break;
        case "service": Service(); break;
        case "metrics": Metrics(); break;
        case "logs": Logs(); break;
        case "traces": Traces(); break;
        case "trace": Trace(); break;
        case "changes": Changes(); break;
        case "blast-radius": BlastRadius(); break;
        case "anomalies": Anomalies(); break;
        case "timeline": Timeline(); break;
        case "board": Board(); break;
        case "pin": Pin(); break;
        case "link": Link(); break;
        case "crossout": CrossOut(); break;
        case "help" or "" or null: Help(); break;
        default: Console.Error.WriteLine($"unknown verb '{verb}'. try: weaver help"); Environment.Exit(2); break;
    }
}
catch (ApiError e)
{
    Console.Error.WriteLine($"weaver: {e.Message}");
    Environment.Exit(1);
}

void Help()
{
    Console.WriteLine("""
        weaver — investigate a service graph from observed telemetry

        observe
          graph                         the topology: services, deps, routes
          service <id>                  one service: deps + a shape per signal
          metrics <id> [--metric m]     a signal's trajectory (shape + prose)
          logs [<id>] [--grep q]        log lines (FTS via --grep)
          traces [--route r]            sampled request traces, slowest first
          trace <id>                    one trace: spans + where self-time went
          changes [--target s]          deploy/config/flag events (annotation stream)

        correlate (enumerations, never a verdict)
          anomalies [--split t] [--z n] what moved vs the base window
          timeline  [--split t]         who moved first (onset ordering)
          blast-radius <id>             who depends on a node (tests a guess)

        build the wall (the board — co-built with the human)
          board new [title]             start a board; prints its id + URL
          pin --kind K --ref R [...]    pin a finding (--label, --note, --evidence)
          link <a> <b> --as "explains"  draw a red-string edge between two pins
          crossout <edge> [--restore]   cut a red string (kept, struck through)
          board show [id]               print the board

        common flags: --json (raw)  --raw (series points)  --limit N
                      --kind service|edge  --level L  --status S
                      --split <iso>  --z <n>  --min-pct <n>  --board <id>
        env: WEAVER_API (default :5180)  WEAVER_WEB (:5173)  WEAVER_BOARD (current board)
        """);
}

void Graph()
{
    var g = api.Get<GraphDto>("/api/graph");
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }

    var bySub = g.Services.GroupBy(s => s.Subsystem ?? "(none)").OrderBy(x => x.Key).ToList();
    Console.WriteLine($"{g.Services.Count} services across {bySub.Count} subsystems · "
        + $"{g.Dependencies.Count} dependencies · {g.RequestTypes.Count} routes");
    foreach (var grp in bySub)
        Console.WriteLine($"  {grp.Key,-14} {string.Join(", ", grp.Select(s => s.Id))}");
    Hint("weaver traces --status error   (where users feel it)",
         "weaver service <id>            (drill a service)");
}

void Service()
{
    var id = argv.Need(0, "service id");
    var d = api.Get<ServiceDetailDto>($"/api/services/{id}");
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }

    var s = d.Service;
    Console.WriteLine($"{s.Id}  ({s.Kind}, subsystem={s.Subsystem ?? "-"}, team={s.OwnerTeam ?? "-"})");
    Console.WriteLine("depends on:      " + (d.DependsOn.Count == 0 ? "(none)"
        : string.Join(", ", d.DependsOn.Select(x => $"{x.ToService}[{x.Kind}{(x.Critical == true ? ",critical" : "")}]"))));
    Console.WriteLine("depended on by:  " + (d.DependedOnBy.Count == 0 ? "(none)"
        : string.Join(", ", d.DependedOnBy.Select(x => x.FromService))));

    var series = api.Get<List<MetricSeriesDto>>($"/api/metrics?subjectId={id}");
    Console.WriteLine("signals:");
    foreach (var ser in series.Where(x => x.Metric != "pool_max"))
        Console.WriteLine($"  {ser.Metric,-15} {Encode(ser).ShapeCode}");
    Hint($"weaver metrics {id} --metric latency_p99   (one signal, in detail)",
         $"weaver logs {id} --level error");
}

void Metrics()
{
    var id = argv.Need(0, "subject id");
    var kind = argv.Opt("kind", "service");
    var qs = $"/api/metrics?subjectId={id}&subjectKind={kind}";
    if (argv.Opt("metric") is { } m) qs += $"&metric={m}";
    if (argv.Opt("from") is { } f) qs += $"&from={f}";
    if (argv.Opt("to") is { } to) qs += $"&to={to}";

    var series = api.Get<List<MetricSeriesDto>>(qs);
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
    if (series.Count == 0) { Console.WriteLine($"no metrics for {kind} '{id}'"); return; }

    foreach (var ser in series)
    {
        Console.WriteLine($"{ser.Metric}  ({ser.Points.Count} samples)");
        if (argv.Raw)
        {
            foreach (var p in ser.Points.Take(argv.Limit ?? 240))
                Console.WriteLine($"  {p.Ts}  {p.Value}");
            continue;
        }
        var t = Encode(ser);
        Console.WriteLine($"  shape: {t.ShapeCode}");
        Console.WriteLine($"  {t.Prose}");
    }
    Hint($"weaver logs {id}", $"weaver metrics {id} --raw   (raw points)");
}

void Logs()
{
    var qs = "/api/logs?";
    if (argv.Pos.Count > 0) qs += $"serviceId={argv.Pos[0]}&";
    if (argv.Opt("level") is { } l) qs += $"level={l}&";
    if (argv.Opt("grep") is { } g) qs += $"q={Uri.EscapeDataString(g)}&";
    qs += $"limit={argv.Limit ?? 200}";

    var logs = api.Get<List<LogEventDto>>(qs);
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
    Console.WriteLine($"{logs.Count} log lines"
        + (argv.Opt("grep") is { } gg ? $" matching \"{gg}\"" : ""));

    foreach (var grp in logs.GroupBy(x => (x.Level, x.TemplateId)).OrderByDescending(x => x.Count()))
        Console.WriteLine($"  {grp.Count(),4}  {grp.Key.Level,-5} {grp.Key.TemplateId}");
    Console.WriteLine("recent:");
    foreach (var e in logs.OrderByDescending(x => x.Ts).Take(5))
        Console.WriteLine($"  {Clock(e.Ts)} {e.Level,-5} {e.ServiceId,-16} {e.Message}");
    Hint("weaver logs <id> --level error", "weaver logs --grep \"<term>\"   (full-text)");
}

void Traces()
{
    var qs = "/api/traces?";
    if (argv.Opt("route") is { } r) qs += $"route={r}&";
    if (argv.Opt("status") is { } st) qs += $"status={st}&";
    if (argv.Opt("min-ms") is { } mm) qs += $"minDurationMs={mm}&";
    qs += $"limit={argv.Limit ?? 20}";

    var traces = api.Get<List<TraceDto>>(qs);
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
    Console.WriteLine($"{traces.Count} traces (slowest first)");
    Console.WriteLine($"  {"id",-10} {"ms",5}  {"status",-7} route");
    foreach (var t in traces)
        Console.WriteLine($"  {t.Id[..8],-10} {t.DurationMs,5}  {t.Status,-7} {t.RequestTypeId}");
    Hint("weaver trace <id>            (spans + self-time)",
         "weaver traces --status error");
}

void Trace()
{
    var id = argv.Need(0, "trace id");
    var d = api.Get<TraceDetailDto>($"/api/traces/{id}");
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }

    var t = d.Trace;
    Console.WriteLine($"trace {t.Id[..8]}  route={t.RequestTypeId}  {t.DurationMs}ms  {t.Status}");
    int maxSelf = d.Spans.Count == 0 ? 0 : d.Spans.Max(s => s.SelfMs);
    int depth = 0;
    foreach (var s in d.Spans)
    {
        var hot = s.SelfMs == maxSelf && maxSelf > 0 ? "  <- most self-time" : "";
        Console.WriteLine($"  {new string(' ', depth * 2)}{s.ServiceId,-18} self {s.SelfMs,4}ms  dur {s.DurationMs,4}ms  {s.Status}{hot}");
        if (s.Attributes.ValueKind == JsonValueKind.Object && s.Attributes.EnumerateObject().Any())
            Console.WriteLine($"  {new string(' ', depth * 2)}    {string.Join("  ", s.Attributes.EnumerateObject().Select(p => $"{p.Name}={p.Value}"))}");
        depth++;
    }
    Hint($"weaver service {d.Spans.LastOrDefault()?.ServiceId ?? "<id>"}");
}

void Changes()
{
    var qs = "/api/change-events?";
    if (argv.Opt("from") is { } f) qs += $"from={Uri.EscapeDataString(f)}&";
    if (argv.Opt("to") is { } t) qs += $"to={Uri.EscapeDataString(t)}&";
    if (argv.Opt("target") is { } tg) qs += $"target={tg}&";

    var rows = api.Get<List<ChangeEventDto>>(qs);
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
    if (rows.Count == 0) { Console.WriteLine("no change events in range (deploys/config/flags)."); return; }

    Console.WriteLine($"{rows.Count} change event(s):");
    Console.WriteLine($"  {"when",-10} {"kind",-12} {"target",-16} summary");
    foreach (var c in rows)
    {
        Console.WriteLine($"  {Clock(c.Ts),-10} {c.Kind,-12} {c.TargetId ?? "(fleet)",-16} {c.Summary}");
        if (c.Fields.ValueKind == JsonValueKind.Object && c.Fields.EnumerateObject().Any())
            Console.WriteLine($"  {"",-10} {"",-12} {"",-16} {string.Join("  ", c.Fields.EnumerateObject().Select(p => $"{p.Name}={p.Value}"))}");
    }
    Hint("weaver pin --kind change --ref <service> --label \"...\"   (pin a deploy as evidence)");
}

void BlastRadius()
{
    var id = argv.Need(0, "service id");
    var b = api.Get<BlastRadiusDto>($"/api/blast-radius/{id}");
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }

    Console.WriteLine($"blast radius of {b.Node}: {b.Count} service(s) depend on it"
        + (b.Count == 0 ? " (nothing downstream — it's a leaf dependency)" : ""));
    foreach (var grp in b.Dependents.GroupBy(d => d.Hops).OrderBy(g => g.Key))
        Console.WriteLine($"  {grp.Key} hop{(grp.Key > 1 ? "s" : " ")}: {string.Join(", ", grp.Select(d => d.ServiceId))}");
    Hint("weaver anomalies            (which of these actually moved?)",
         $"weaver service {id}");
}

void Anomalies()
{
    var qs = AnalysisQuery("/api/anomalies");
    var a = api.Get<List<AnomalyDto>>(qs);
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
    if (a.Count == 0) { Console.WriteLine("no anomalies vs base — the system looks calm."); Hint("weaver timeline", "weaver graph"); return; }

    Console.WriteLine($"{a.Count} signal(s) moved vs base (unranked — cause and collateral mixed):");
    Console.WriteLine($"  {"subject",-18} {"metric",-14} {"delta",8}  {"z",5}  onset");
    foreach (var x in a)
        Console.WriteLine($"  {x.SubjectId,-18} {x.Metric,-14} {x.DeltaPct,7:+0.#;-0.#}%  {x.Z,5:0.#}  {(x.OnsetTs is { } o ? Clock(o) : "-")} ({x.Direction})");
    Hint("weaver timeline             (which moved first?)",
         "weaver blast-radius <id>    (does a suspect's downstream cover these?)");
}

void Timeline()
{
    var qs = AnalysisQuery("/api/timeline");
    var t = api.Get<List<TimelineEntryDto>>(qs);
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
    if (t.Count == 0) { Console.WriteLine("no anomalies to order — the system looks calm."); Hint("weaver graph"); return; }

    Console.WriteLine($"onset order ({t.Count} services moved) — earliest first; precedence, not causation:");
    foreach (var e in t)
        Console.WriteLine($"  {Clock(e.OnsetTs)}  {e.SubjectId,-18} (first via {e.Metric}, z {e.Z:0.#})");
    Hint($"weaver blast-radius {t[0].SubjectId}   (does the earliest mover's downstream cover the rest?)",
         $"weaver service {t[0].SubjectId}");
}

// --- the board (the agent co-builds the wall of red string) ---------------
void Board()
{
    var web = Environment.GetEnvironmentVariable("WEAVER_WEB") ?? "http://localhost:5173";
    var sub = argv.Pos.Count > 0 ? argv.Pos[0] : "show";

    if (sub == "new")
    {
        var title = string.Join(" ", argv.Pos.Skip(1));
        var c = api.Post<CreatedDto>("/api/boards", new { title = string.IsNullOrWhiteSpace(title) ? null : title });
        Console.WriteLine($"board {c.Id} created");
        Console.WriteLine($"open: {web}{c.Url}");
        Console.WriteLine($"tip:  export WEAVER_BOARD={c.Id}   (so pin/link target it)");
        return;
    }

    var id = ResolveBoard(posIndex: 1);
    var b = api.Get<BoardDto>($"/api/boards/{id}");
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
    Console.WriteLine($"board {b.Id}: {b.Title}  ({b.Items.Count} pins, {b.Edges.Count} edges)");
    Console.WriteLine($"open: {web}/view?board={b.Id}");
    Console.WriteLine("pins:");
    foreach (var it in b.Items)
        Console.WriteLine($"  {it.Id}  {it.Kind,-8} {it.Ref,-18} {it.Label}");
    if (b.Edges.Count > 0)
    {
        Console.WriteLine("red string:");
        foreach (var e in b.Edges)
            Console.WriteLine($"  {e.FromItem} -> {e.ToItem}  ({e.Kind}{(e.Label is { } l ? ": " + l : "")}) [{e.DrawnBy}]");
    }
    Hint("weaver pin --kind K --ref R --label \"...\"", "weaver link <a> <b> --as \"explains\"");
}

void Pin()
{
    var id = ResolveBoard();
    var kind = argv.Opt("kind") ?? "note";
    var refv = argv.Opt("ref") ?? (argv.Pos.Count > 0 ? argv.Pos[0] : "");
    object? evidence =
        argv.Opt("evidence") is { } ev ? JsonSerializer.Deserialize<JsonElement>(ev) :
        argv.Opt("note") is { } note ? new { note } : null;
    var c = api.Post<CreatedDto>($"/api/boards/{id}/items",
        new { kind, @ref = refv, label = argv.Opt("label"), evidence });
    Console.WriteLine($"pinned {c.Id}  ({kind} {refv})");
    Hint("weaver link <a> <b> --as \"explains\"", "weaver board show");
}

void Link()
{
    var id = ResolveBoard();
    var from = argv.Need(0, "from item id");
    var to = argv.Need(1, "to item id");
    api.Post<CreatedDto>($"/api/boards/{id}/edges",
        new { from, to, kind = argv.Opt("kind") ?? "causal", label = argv.Opt("as") ?? argv.Opt("label"), drawnBy = "agent" });
    Console.WriteLine($"linked {from} -> {to}{(argv.Opt("as") is { } a ? $"  ({a})" : "")}");
    Hint("weaver board show");
}

void CrossOut()
{
    var id = ResolveBoard();
    var edge = argv.Need(0, "edge id");
    var restore = argv.Has("restore");
    api.Post<BoardEdgeDto>($"/api/boards/{id}/edges/{edge}/crossout", new { crossedOut = !restore });
    Console.WriteLine(restore ? $"restored {edge}" : $"crossed out {edge}");
    Hint("weaver board show");
}

string ResolveBoard(int posIndex = -1)
{
    var id = (posIndex >= 0 && argv.Pos.Count > posIndex ? argv.Pos[posIndex] : null)
             ?? argv.Opt("board") ?? Environment.GetEnvironmentVariable("WEAVER_BOARD");
    if (string.IsNullOrWhiteSpace(id))
    {
        Console.Error.WriteLine("weaver: no board. Run `weaver board new`, pass --board <id>, or set $WEAVER_BOARD.");
        Environment.Exit(2);
    }
    return id!;
}

// --- shared helpers -------------------------------------------------------
string AnalysisQuery(string path)
{
    var parts = new List<string>();
    if (argv.Opt("split") is { } s) parts.Add($"split={Uri.EscapeDataString(s)}");
    if (argv.Opt("z") is { } z) parts.Add($"z={z}");
    if (argv.Opt("min-pct") is { } mp) parts.Add($"minPct={mp}");
    return parts.Count == 0 ? path : $"{path}?{string.Join("&", parts)}";
}

TrajectoryResult Encode(MetricSeriesDto s)
{
    var vals = s.Points.Select(p => p.Value).ToList();
    double mins = 120;
    if (s.Points.Count > 1)
        mins = (DateTimeOffset.Parse(s.Points[^1].Ts) - DateTimeOffset.Parse(s.Points[0].Ts)).TotalMinutes;
    return Trajectory.Encode(vals, mins, Unit(s.Metric));
}

static string Unit(string metric) => metric switch
{
    "latency_p50" or "latency_p99" => "ms",
    "throughput_rps" => " rps",
    _ => "",
};

static string Clock(string iso) => DateTimeOffset.TryParse(iso, out var d) ? d.ToString("HH:mm:ss") : iso;

void Hint(params string[] next) => Console.WriteLine("next: " + string.Join("\n      ", next));

// --- tiny arg parser ------------------------------------------------------
sealed class ArgParser
{
    static readonly HashSet<string> BoolFlags = ["json", "raw"];
    public string? Verb { get; }
    public List<string> Pos { get; } = [];
    readonly Dictionary<string, string> opts = [];
    readonly HashSet<string> flags = [];

    public ArgParser(string[] a)
    {
        Verb = a.Length > 0 ? a[0] : null;
        for (int i = 1; i < a.Length; i++)
        {
            var tok = a[i];
            if (tok.StartsWith("--"))
            {
                var name = tok[2..];
                if (BoolFlags.Contains(name)) flags.Add(name);
                else if (i + 1 < a.Length && !a[i + 1].StartsWith("--")) opts[name] = a[++i];
                else flags.Add(name);
            }
            else Pos.Add(tok);
        }
    }

    public bool Json => flags.Contains("json");
    public bool Raw => flags.Contains("raw");
    public bool Has(string k) => flags.Contains(k);
    public int? Limit => opts.TryGetValue("limit", out var v) && int.TryParse(v, out var n) ? n : null;
    public string? Opt(string k) => opts.TryGetValue(k, out var v) ? v : null;
    public string Opt(string k, string dflt) => opts.TryGetValue(k, out var v) ? v : dflt;
    public string Need(int i, string what)
    {
        if (Pos.Count > i) return Pos[i];
        Console.Error.WriteLine($"weaver: missing {what}");
        Environment.Exit(2);
        return "";
    }
}

// --- API client -----------------------------------------------------------
sealed class ApiError(string msg) : Exception(msg);

sealed class Api
{
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    readonly HttpClient http;
    public string LastRaw { get; private set; } = "";

    public Api(string baseUrl) => http = new HttpClient { BaseAddress = new Uri(baseUrl) };

    public T Get<T>(string path)
    {
        HttpResponseMessage resp;
        try { resp = http.GetAsync(path).GetAwaiter().GetResult(); }
        catch (HttpRequestException) { throw new ApiError($"can't reach the API at {http.BaseAddress}. Is it running? (dotnet run --project src/Weaver.Api)"); }

        LastRaw = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (resp.StatusCode == HttpStatusCode.NotFound) throw new ApiError(ErrText(LastRaw) ?? "not found");
        if (!resp.IsSuccessStatusCode) throw new ApiError($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {LastRaw}");
        return JsonSerializer.Deserialize<T>(LastRaw, J) ?? throw new ApiError("empty response");
    }

    public T Post<T>(string path, object body)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, J), Encoding.UTF8, "application/json");
        HttpResponseMessage resp;
        try { resp = http.PostAsync(path, content).GetAwaiter().GetResult(); }
        catch (HttpRequestException) { throw new ApiError($"can't reach the API at {http.BaseAddress}. Is it running?"); }

        LastRaw = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (resp.StatusCode == HttpStatusCode.NotFound) throw new ApiError(ErrText(LastRaw) ?? "not found");
        if (!resp.IsSuccessStatusCode) throw new ApiError($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {LastRaw}");
        return JsonSerializer.Deserialize<T>(LastRaw, J) ?? throw new ApiError("empty response");
    }

    static string? ErrText(string body)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(body).TryGetProperty("error", out var e) ? e.GetString() : null; }
        catch { return null; }
    }
}
