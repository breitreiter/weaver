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
FacetsDto? facetsCache = null;  // lazily fetched for time/did-you-mean resolution

// the six search scopes + their sort labels, mirroring the UI's left panel.
GraphDto? graphCache = null;  // lazily fetched for did-you-mean service resolution
string[] scopes = ["anomalies", "traces", "logs", "services", "metrics", "changes"];
var sortBy = new Dictionary<string, string>
{
    ["anomalies"] = "magnitude", ["traces"] = "duration", ["logs"] = "recency",
    ["changes"] = "time", ["services"] = "name",
};

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
        case "search": Search(); break;
        case "facets": Facets(); break;
        case "relationships" or "rel": Relationships(); break;
        case "evidence": NodeEvidence(); break;
        case "board": Board(); break;
        case "pin": Pin(); break;
        case "chart": Chart(); break;
        case "unpin": Unpin(); break;
        case "doc": Doc(); break;
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

        forage (the same lens the UI's left panel uses)
          search <scope> [facets]       the unified query: anomalies | traces |
                                          logs | services | metrics | changes.
                                          every row prints a typed id you can pin.
          facets                        what subsystems/levels/routes/… exist
          service <id>                  one service: deps + a shape per signal
          metrics <id> [--metric m]     a signal's trajectory (shape + prose)
          logs [<id>] [--grep q]        log lines (FTS via --grep)
          traces [--route r]            sampled request traces, slowest first
          trace <id>                    one trace: spans + where self-time went
          changes [--target s]          deploy/config/flag events
          evidence <service>            the node dossier (signals/logs/changes)

        correlate (enumerations, never a verdict)
          anomalies [--split t] [--z n] what moved vs the base window
          timeline  [--split t]         who moved first (onset ordering)
          blast-radius <id>             who depends on a node (tests a guess)
          relationships <a> <b>         the facts between two services (ground a link)

        write it up (the board — pins + the co-edited document)
          board new [title]             start a board; prints its id + URL
          board show [id|url]           print the board (pinned services + evidence)
          pin <id|service>              pin a search result by its typed id, OR a
              [--as K --aspect A]         service + manual evidence (--note/--evidence/--at)
          chart --sql "q" --title "t"   author a chart from raw SQL (read-only sandbox):
              [--type line|bar|area|scatter]  prints the rows as a table + a ch: id;
              [--x col] [--y a,b]         --pin <service> snapshots it to the board.
              [--pin <service>]           the visual render is web-only.
          unpin <evidence-id>           drop one finding
          unpin <service> --all         remove a service and its evidence
          doc show [id|url]             print the co-edited document (+ version)
          doc edit --find "x" --replace "y"  anchored find/replace — re-anchors on
                                          a concurrent edit, never blind offsets
          doc append "text"             add text to the end of the document

        common flags: --json (raw)  --raw (series points)  --limit N
          facets: --grep q  --subsystem S  --kind K  --team T  --level L
                  --template T  --route R  --status S  --metric M  --service S
                  --from <t>  --to <t>  --split <iso>  --z <n>  --min-pct <n>
          --board <id|url>  (or set $WEAVER_BOARD; a pasted /view?board= URL works)
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
    var id = ResolveService(argv.Need(0, "service id"));
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
    var kind = argv.Opt("kind", "service");
    var rawId = argv.Need(0, "subject id");
    // only services live in the graph; resolve those, pass edge/other ids through.
    var id = kind == "service" ? ResolveService(rawId) : rawId;
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
    {
        // correlated logs carry the trace they fired under — surface it so the
        // pivot (weaver trace <id>) is one glance away.
        var pivot = e.TraceId is { Length: >= 8 } tid ? $"  → tr:{tid[..8]}" : "";
        Console.WriteLine($"  {Clock(e.Ts)} {e.Level,-5} {e.ServiceId,-16} {e.Message}{pivot}");
    }
    Hint("weaver logs <id> --level error", "weaver trace <id>   (follow a correlated log to its trace)");
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
    // hot hop = the busiest SERVER span (client spans are just network legs).
    int maxSelf = d.Spans.Where(s => s.Kind == "server").Select(s => s.SelfMs).DefaultIfEmpty(0).Max();
    int depth = 0;
    foreach (var s in d.Spans)
    {
        var hot = s.Kind == "server" && s.SelfMs == maxSelf && maxSelf > 0 ? "  <- most self-time" : "";
        Console.WriteLine($"  {new string(' ', depth * 2)}{s.ServiceId,-18} {s.Kind,-6} self {s.SelfMs,4}ms  dur {s.DurationMs,4}ms  {s.Status}{hot}");
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
    Hint("weaver pin <service> --as change --aspect deploy --label \"...\"   (pin a deploy as evidence)");
}

void BlastRadius()
{
    var id = ResolveService(argv.Need(0, "service id"));
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

// --- forage: the unified search (same lens as the UI's left panel) --------
// Six scopes, the same facets/caps/sort the UI uses. Every row leads with its
// TYPED ID (an:svc:metric, tr:…, log:…, me:…, ce:…, svc:…) — the shared handle
// the human and agent both speak and that `pin <id>` resolves.
void Search()
{
    var scope = argv.Pos.Count > 0 ? argv.Pos[0] : "anomalies";
    if (!scopes.Contains(scope))
    {
        Console.Error.WriteLine($"weaver: unknown scope '{scope}'. one of: {string.Join(" | ", scopes)}");
        Environment.Exit(2);
    }
    var limit = argv.Limit ?? 60;
    var qs = SearchQuery(scope, limit);
    var rows = api.Get<List<SearchResultDto>>("/api/search" + qs);
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }

    var capped = rows.Count >= limit;
    var sort = sortBy.TryGetValue(scope, out var s) ? s : null;
    Console.WriteLine(capped
        ? $"{scope} — top {limit}{(sort is not null ? $" by {sort}" : "")} (more exist)"
        : $"{scope} — {rows.Count} result{(rows.Count == 1 ? "" : "s")}");
    if (rows.Count == 0) { Hint("weaver facets   (what can I filter by?)"); return; }

    foreach (var r in rows)
    {
        Console.WriteLine($"  {r.Id}");
        Console.WriteLine($"      {r.Title}");
        if (!string.IsNullOrWhiteSpace(r.Subtitle)) Console.WriteLine($"      {r.Subtitle}");
    }
    Hint($"weaver pin {rows[0].Id}   (pin the top finding to the board)",
         "weaver facets                  (narrow with another facet)");
}

void Facets()
{
    var f = api.Get<FacetsDto>("/api/search/facets");
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
    Console.WriteLine($"window:      {f.Window.Start}  ..  {f.Window.End}");
    void Row(string name, IReadOnlyList<string> vals) =>
        Console.WriteLine($"{name,-12} {(vals.Count == 0 ? "(none)" : string.Join(", ", vals))}");
    Row("subsystems:", f.Subsystems);
    Row("kinds:", f.Kinds);
    Row("teams:", f.Teams);
    Row("metrics:", f.Metrics);
    Row("log levels:", f.LogLevels);
    Row("templates:", f.LogTemplates);
    Row("routes:", f.Routes);
    Row("trace stat:", f.TraceStatuses);
    Row("change kinds:", f.ChangeKinds);
    Hint("weaver search anomalies --subsystem <s>", "weaver search logs --grep \"<term>\" --level error");
}

// the observed facts between two services — what a claim can be grounded in.
// Enumerates (dependency / shared route / temporal precedence); never crowns a cause.
void Relationships()
{
    var a = ResolveService(argv.Need(0, "service a"));
    var b = ResolveService(argv.Need(1, "service b"));
    var r = api.Get<RelationshipsDto>($"/api/relationships?a={Uri.EscapeDataString(a)}&b={Uri.EscapeDataString(b)}");
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
    if (r.Relationships.Count == 0)
    {
        Console.WriteLine($"no recorded relationship between {a} and {b} — a link here is your own assertion.");
        Hint($"weaver link {a} {b} --as \"explains\"   (draw it as a hypothesis)");
        return;
    }
    Console.WriteLine($"{r.Relationships.Count} fact(s) between {a} and {b} (enumerated, not ranked):");
    foreach (var rel in r.Relationships)
        Console.WriteLine($"  [{rel.Group,-10}] {rel.From} -> {rel.To}  {rel.Title}\n      {rel.Detail}");
    Hint($"weaver link {r.Relationships[0].From} {r.Relationships[0].To} --as \"{r.Relationships[0].SuggestedLabel}\"");
}

void NodeEvidence()
{
    var id = ResolveService(argv.Need(0, "service id"));
    var qs = $"/api/nodes/{id}/evidence";
    if (argv.Opt("from") is { } f) qs += $"?from={Uri.EscapeDataString(f)}";
    if (argv.Opt("to") is { } t) qs += (qs.Contains('?') ? "&" : "?") + $"to={Uri.EscapeDataString(t)}";
    var d = api.Get<NodeEvidenceDto>(qs);
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }

    Console.WriteLine($"{d.Node.Id}  ({d.Node.Kind}, subsystem={d.Node.Subsystem ?? "-"}, team={d.Node.OwnerTeam ?? "-"})");
    Console.WriteLine($"window: {d.Window.Start}  ..  {d.Window.End}");
    Console.WriteLine("signals:");
    foreach (var sig in d.Signals)
        Console.WriteLine($"  {sig.Metric,-15} {sig.ShapeCode}");
    if (d.Logs.Count > 0)
    {
        Console.WriteLine("log groups:");
        foreach (var lg in d.Logs.Take(argv.Limit ?? 8))
            Console.WriteLine($"  {lg.Count,5}  {lg.Level,-5} {lg.TemplateId,-22} {lg.Sample}");
    }
    if (d.Changes.Count > 0)
    {
        Console.WriteLine("changes:");
        foreach (var c in d.Changes)
            Console.WriteLine($"  {Clock(c.Ts),-10} {c.Kind,-12} {c.Summary}");
    }
    Console.WriteLine($"traces touching this service: {d.TracesParticipated}");
    Hint($"weaver search anomalies --service {id}", $"weaver pin <id>   (pin a finding from above)");
}

// build the /api/search query string from the shared facet flags.
string SearchQuery(string scope, int limit)
{
    var p = new List<string> { $"scope={scope}", $"limit={limit}" };
    void Add(string key, string? val) { if (!string.IsNullOrWhiteSpace(val)) p.Add($"{key}={Uri.EscapeDataString(val)}"); }
    Add("q", argv.Opt("grep") ?? argv.Opt("q"));
    Add("subsystem", argv.Opt("subsystem"));
    Add("kind", argv.Opt("kind"));
    Add("team", argv.Opt("team"));
    Add("level", argv.Opt("level"));
    Add("template", argv.Opt("template"));
    Add("route", argv.Opt("route"));
    Add("status", argv.Opt("status"));
    Add("metric", argv.Opt("metric"));
    Add("service", argv.Opt("service"));
    Add("minMs", argv.Opt("min-ms"));
    Add("from", ResolveTime(argv.Opt("from")));
    Add("to", ResolveTime(argv.Opt("to")));
    Add("split", argv.Opt("split"));
    Add("z", argv.Opt("z"));
    Add("minPct", argv.Opt("min-pct"));
    return "?" + string.Join("&", p);
}

// --- the board (pins + the co-edited document) ----------------------------
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
        Console.WriteLine($"tip:  export WEAVER_BOARD={c.Id}   (so pin/doc target it)");
        return;
    }

    var id = ResolveBoard(posIndex: 1);
    var b = api.Get<BoardDto>($"/api/boards/{id}");

    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
    Console.WriteLine($"board {b.Id}: {b.Title}  ({b.Nodes.Count} service{(b.Nodes.Count == 1 ? "" : "s")})");
    Console.WriteLine($"open: {web}/view?board={b.Id}");
    Console.WriteLine("services:");
    foreach (var n in b.Nodes)
    {
        Console.WriteLine($"  {n.ServiceId,-20} {n.Label}");
        foreach (var ev in n.Evidence)
            // lead with the storage id (the `unpin` handle); trailing @ref is the
            // document handle (what you write into the doc to reference this finding).
            Console.WriteLine($"      {ev.Id}  {ev.Kind,-8} {ev.Aspect}"
                + (string.IsNullOrWhiteSpace(ev.Summary) ? (ev.Label is { } l ? "  " + l : "") : "  " + ev.Summary)
                + (ev.RefId is { } rid ? $"   @{rid}" : ""));
    }
    Hint("weaver pin <id|service>", "weaver doc show", "weaver doc edit --find \"…\" --replace \"…\"");
}

// Pin a search result by its TYPED ID (an:/tr:/log:/me:/ce:/svc:) — resolves the
// exact payload the UI would have pinned — OR a bare service + manual evidence
// (--as <kind> --aspect <a>, with --note / --evidence / --at).
void Pin()
{
    var id = ResolveBoard();
    var arg = argv.Opt("ref") ?? (argv.Pos.Count > 0 ? argv.Pos[0] : "");
    if (string.IsNullOrWhiteSpace(arg)) { Console.Error.WriteLine("weaver: pin needs a <typed-id> or <service>."); Environment.Exit(2); return; }

    // a typed search-result id → resolve it to the same pin target the UI uses.
    if (IsTypedId(arg))
    {
        var r = api.Get<SearchResultDto>($"/api/search/resolve?id={Uri.EscapeDataString(arg)}");
        api.Post<CreatedDto>($"/api/boards/{id}/pin",
            new { serviceIds = r.Pin.NodeIds, evidence = r.Pin.Evidence, label = r.Title });
        var where = r.Pin.NodeIds.Count > 1 ? $"{r.Pin.NodeIds.Count} services" : r.Pin.NodeIds.FirstOrDefault() ?? "(fleet)";
        Console.WriteLine($"pinned {r.Type}: {r.Title}  → {where}");
        Hint("weaver board show", "weaver link <a> <b> --as \"explains\"");
        return;
    }

    // otherwise a bare service pin, optionally with manual evidence. soft-resolve
    // so a typo'd real service is corrected but edge subjects still pin.
    var service = ResolveService(arg, strict: false);
    var evKind = argv.Opt("as") ?? argv.Opt("kind");
    var hasEvidence = evKind is { } k0 && k0 is not ("service" or "node" or "note");
    object? evidence = null;
    if (hasEvidence)
    {
        object? payload =
            argv.Opt("evidence") is { } ev ? JsonSerializer.Deserialize<JsonElement>(ev) :
            argv.Opt("note") is { } note ? new { note } : null;
        evidence = new { kind = evKind, aspect = argv.Opt("aspect") ?? "", at = ResolveTime(argv.Opt("at")), payload };
    }
    api.Post<CreatedDto>($"/api/boards/{id}/pin",
        new { serviceIds = new[] { service }, evidence, label = argv.Opt("label") });
    Console.WriteLine(hasEvidence ? $"pinned {service}  (+{evKind})" : $"pinned {service}");
    Hint("weaver pin an:<service>:<metric>   (pin a search result by id)", "weaver search anomalies");
}

// Author a chart from raw SQL: run it through the read-only sandbox, print the
// result as a prose table + the `ch:` id, and (with --pin <service>) snapshot it onto
// the board as chart evidence. The VISUAL render is web-only (cli.md no-glyph rule) —
// here we show the numbers so the agent can sanity-check them. See agent-sql-charts.md.
void Chart()
{
    var sql = argv.Opt("sql");
    if (string.IsNullOrWhiteSpace(sql)) { Console.Error.WriteLine("weaver: chart needs --sql \"<query>\"."); Environment.Exit(2); return; }
    var title = argv.Opt("title");
    if (string.IsNullOrWhiteSpace(title)) { Console.Error.WriteLine("weaver: chart needs --title \"<t>\" (it names the ch: id)."); Environment.Exit(2); return; }

    // --pin names BOTH the subject node and the intent to save. Soft-resolve a typo'd
    // service (did-you-mean) like `pin`; a cross-node subject still passes through.
    var subject = argv.Opt("pin") is { } pv && !string.IsNullOrWhiteSpace(pv) ? ResolveService(pv, strict: false) : null;
    // resolve the board up-front when we'll pin, so a missing board fails fast (before exec).
    var board = subject is not null ? ResolveBoard() : null;
    var yCols = argv.Opt("y")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var exec = api.Post<ChartExecDto>("/api/charts/exec", new
    {
        sql, title, subject, type = argv.Opt("type"), xColumn = argv.Opt("x"), yColumns = yCols,
    });
    if (argv.Json) { Console.WriteLine(api.LastRaw); return; }

    PrintTable(exec.Columns, exec.Rows);
    if (exec.Truncated) Console.WriteLine($"  … truncated at {exec.RowCount} rows (raise --limit on the query or narrow it).");
    var chartId = exec.Result?.Id ?? "ch:?";
    Console.WriteLine();

    if (subject is not null && exec.Result is { } r)
    {
        api.Post<CreatedDto>($"/api/boards/{board}/pin",
            new { serviceIds = r.Pin.NodeIds, evidence = r.Pin.Evidence, label = title });
        Console.WriteLine($"pinned chart {chartId}  → {subject}");
        Hint("weaver board show", $"weaver doc append \"… see @{chartId}\"");
    }
    else
    {
        Console.WriteLine($"chart {chartId}  (not pinned — add --pin <service> to save it to the board)");
        Hint($"weaver chart --sql \"…\" --title \"{title}\" --pin <service>");
    }
}

// A plain aligned table for a chart's rows — numbers right-aligned, cells capped so a
// runaway column can't blow the terminal. Cells arrive as JsonElement (raw SQLite
// number/string/null); nothing interpretive is added. Web renders the visual; this is
// the numbers.
static void PrintTable(IReadOnlyList<string> cols, IReadOnlyList<IReadOnlyList<object?>> rows)
{
    var n = cols.Count;
    if (n == 0) { Console.WriteLine("  (no columns)"); return; }
    var cells = rows.Select(r => Enumerable.Range(0, n).Select(i => Cell(i < r.Count ? r[i] : null)).ToArray()).ToList();
    var numeric = new bool[n];
    var widths = new int[n];
    for (var i = 0; i < n; i++)
    {
        widths[i] = Math.Min(40, Math.Max(cols[i].Length, cells.Count == 0 ? 0 : cells.Max(c => c[i].Length)));
        numeric[i] = rows.Count > 0 && rows.All(r => i < r.Count && r[i] is JsonElement je && je.ValueKind == JsonValueKind.Number);
    }
    string Fmt(string[] c) => "  " + string.Join("  ", Enumerable.Range(0, n).Select(i =>
    {
        var s = c[i].Length > widths[i] ? c[i][..widths[i]] : c[i];
        return numeric[i] ? s.PadLeft(widths[i]) : s.PadRight(widths[i]);
    }));
    Console.WriteLine(Fmt(cols.ToArray()));
    Console.WriteLine("  " + string.Join("  ", widths.Select(w => new string('-', w))));
    foreach (var c in cells) Console.WriteLine(Fmt(c));
    if (rows.Count == 0) Console.WriteLine("  (0 rows)");
}

static string Cell(object? o) => o switch
{
    null => "",
    JsonElement e => e.ValueKind switch
    {
        JsonValueKind.Null => "",
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => e.GetRawText(),
    },
    _ => o.ToString() ?? "",
};

// a typed search-result id: known prefix + ':' (an:svc:metric, tr:…, log:…, me:…, ce:…, svc:…, ch:…).
// `ch:` (an authored SQL chart) resolves to a teaching error — charts are minted by
// `weaver chart`, not pinned by id — but it's a typed id so it routes there, not into
// the bare-service did-you-mean path.
static bool IsTypedId(string s)
{
    var c = s.IndexOf(':');
    return c > 0 && s[..c] is "an" or "tr" or "log" or "me" or "ce" or "svc" or "ch";
}

void Unpin()
{
    var id = ResolveBoard();
    var what = argv.Need(0, "evidence-id or service");
    if (argv.Has("all"))
    {
        api.Delete($"/api/boards/{id}/nodes/{Uri.EscapeDataString(what)}");
        Console.WriteLine($"removed {what} (node + its evidence + its strings)");
    }
    else
    {
        api.Delete($"/api/boards/{id}/evidence/{Uri.EscapeDataString(what)}");
        Console.WriteLine($"dropped evidence {what}");
    }
    Hint("weaver board show");
}

// The co-edited document — Claude's hands on the board's synthesis surface.
//   doc show   — print the current document + version
//   doc edit   — an ANCHORED find/replace (the discipline from co-edit-document.md:
//                small, localized, context-anchored edits — never blind offsets)
//   doc append — add text to the end (the common "record a finding" move)
void Doc()
{
    var sub = argv.Pos.Count > 0 ? argv.Pos[0] : "show";

    if (sub == "show")
    {
        var b = api.Get<BoardDto>($"/api/boards/{ResolveBoard(posIndex: 1)}");
        if (argv.Json) { Console.WriteLine(api.LastRaw); return; }
        Console.WriteLine($"# doc — board {b.Id} (v{b.DocVersion})");
        Console.WriteLine(string.IsNullOrEmpty(b.Doc) ? "(empty — nothing written yet)" : b.Doc);
        return;
    }

    var id = ResolveBoard();

    if (sub == "append")
    {
        var text = argv.Opt("text") ?? string.Join(" ", argv.Pos.Skip(1));
        if (string.IsNullOrEmpty(text)) { Console.Error.WriteLine("weaver: doc append needs --text \"…\" (or trailing words)."); Environment.Exit(2); return; }
        EditDoc(id, cur => cur.Length == 0 ? text : cur.TrimEnd('\n') + "\n\n" + text, "appended");
        return;
    }

    if (sub == "edit")
    {
        var find = argv.Opt("find");
        var replace = argv.Opt("replace") ?? "";
        if (string.IsNullOrEmpty(find)) { Console.Error.WriteLine("weaver: doc edit needs --find \"…\" [--replace \"…\"]."); Environment.Exit(2); return; }
        EditDoc(id, cur =>
        {
            var i = cur.IndexOf(find, StringComparison.Ordinal);
            if (i < 0) throw new ApiError($"anchor not found: \"{Trunc(find)}\". `doc show` first and copy exact text.");
            if (cur.IndexOf(find, i + find.Length, StringComparison.Ordinal) >= 0)
                throw new ApiError($"anchor \"{Trunc(find)}\" matches more than once — add surrounding context to make it unique.");
            return cur[..i] + replace + cur[(i + find.Length)..];
        }, "edited");
        return;
    }

    Console.Error.WriteLine($"weaver: unknown doc subcommand '{sub}'. try: doc show | doc edit | doc append");
    Environment.Exit(2);
}

// Apply a pure text transform to the doc and PUT it (3-way-merged server-side).
// On a conflict — a concurrent human edit touched the SAME lines — we re-fetch and
// re-apply the transform (re-anchoring), never blind-retrying the stale patch. If
// the transform's anchor is gone after that edit, it surfaces (the human reworked
// exactly there — their call to make). Disjoint concurrent edits merge silently.
void EditDoc(string id, Func<string, string> transform, string did)
{
    for (int attempt = 0; attempt < 3; attempt++)
    {
        var b = api.Get<BoardDto>($"/api/boards/{id}");
        var next = transform(b.Doc);
        if (next == b.Doc) { Console.WriteLine("no change."); return; }
        var res = api.Put<DocDto>($"/api/boards/{id}/doc",
            new { baseVersion = b.DocVersion, baseText = b.Doc, text = next });
        if (!res.Conflict) { Console.WriteLine($"{did} — board {id} now at v{res.DocVersion}"); return; }
        // conflict: loop re-fetches the latest text and re-anchors the transform.
    }
    throw new ApiError("doc edit kept colliding with concurrent edits — `doc show` and try again.");
}

static string Trunc(string s) => s.Length <= 40 ? s.Replace("\n", "⏎") : s[..40].Replace("\n", "⏎") + "…";

string ResolveBoard(int posIndex = -1)
{
    var id = (posIndex >= 0 && argv.Pos.Count > posIndex ? argv.Pos[posIndex] : null)
             ?? argv.Opt("board") ?? Environment.GetEnvironmentVariable("WEAVER_BOARD");
    if (string.IsNullOrWhiteSpace(id))
    {
        Console.Error.WriteLine("weaver: no board. Run `weaver board new`, pass --board <id>, or set $WEAVER_BOARD.");
        Environment.Exit(2);
    }
    return BoardId(id!);
}

// Accept a pasted board URL anywhere a board id goes — the tired user pastes the
// link in their address bar and it just works. `…/view?board=ab12cd34` -> ab12cd34.
static string BoardId(string s)
{
    var m = System.Text.RegularExpressions.Regex.Match(s, @"[?&]board=([^&\s]+)");
    return m.Success ? m.Groups[1].Value : s.Trim();
}

// --- shared helpers -------------------------------------------------------
FacetsDto FacetsCached() => facetsCache ??= api.Get<FacetsDto>("/api/search/facets");
GraphDto GraphCached() => graphCache ??= api.Get<GraphDto>("/api/graph");

// Forgiving service id (did-you-mean): exact wins; else a unique case-insensitive,
// prefix, or substring match auto-resolves with a note on stderr — so a rushed user
// can type "payments" for payments-db. `strict` exits with nearby ids when nothing
// resolves uniquely; soft (link) passes the token through (edge subjects aren't in
// the graph). The graph is fetched once and cached.
string ResolveService(string token, bool strict = true)
{
    var ids = GraphCached().Services.Select(s => s.Id).ToList();
    if (ids.Contains(token)) return token;
    List<string> Match(Func<string, bool> p) => ids.Where(p).ToList();
    foreach (var hits in new[]
    {
        Match(s => string.Equals(s, token, StringComparison.OrdinalIgnoreCase)),
        Match(s => s.StartsWith(token, StringComparison.OrdinalIgnoreCase)),
        Match(s => s.Contains(token, StringComparison.OrdinalIgnoreCase)),
    })
    {
        if (hits.Count == 1) { Console.Error.WriteLine($"weaver: read '{token}' as {hits[0]}"); return hits[0]; }
        if (hits.Count > 1 && strict)
        {
            Console.Error.WriteLine($"weaver: '{token}' is ambiguous — did you mean: {string.Join(", ", hits.Take(6))}?");
            Environment.Exit(2);
        }
    }
    if (!strict) return token;
    Console.Error.WriteLine($"weaver: no service matches '{token}'. try `weaver graph` for the list.");
    Environment.Exit(2);
    return token;
}

// Forgiving time: a full ISO/date passes through; a bare "14:30" / "14:30:00"
// borrows the date from the dataset window, so a rushed user can type the clock
// time they see on a chart. Returns null for empty input.
string? ResolveTime(string? t)
{
    if (string.IsNullOrWhiteSpace(t)) return null;
    t = t.Trim();
    if (!System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d{1,2}:\d{2}(:\d{2})?$")) return t;
    var date = FacetsCached().Window.Start[..10];           // yyyy-MM-dd
    return $"{date}T{(t.Count(c => c == ':') == 1 ? t + ":00" : t)}";
}

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

    // 409 Conflict is an EXPECTED outcome here: the doc PUT returns a typed body
    // (DocDto with Conflict=true) so the caller can re-fetch and re-anchor. Only
    // other non-success codes throw.
    public T Put<T>(string path, object body)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, J), Encoding.UTF8, "application/json");
        HttpResponseMessage resp;
        try { resp = http.PutAsync(path, content).GetAwaiter().GetResult(); }
        catch (HttpRequestException) { throw new ApiError($"can't reach the API at {http.BaseAddress}. Is it running?"); }

        LastRaw = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (resp.StatusCode == HttpStatusCode.NotFound) throw new ApiError(ErrText(LastRaw) ?? "not found");
        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.Conflict)
            throw new ApiError($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {LastRaw}");
        return JsonSerializer.Deserialize<T>(LastRaw, J) ?? throw new ApiError("empty response");
    }

    public void Delete(string path)
    {
        HttpResponseMessage resp;
        try { resp = http.DeleteAsync(path).GetAwaiter().GetResult(); }
        catch (HttpRequestException) { throw new ApiError($"can't reach the API at {http.BaseAddress}. Is it running?"); }

        LastRaw = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (resp.StatusCode == HttpStatusCode.NotFound) throw new ApiError(ErrText(LastRaw) ?? "not found");
        if (!resp.IsSuccessStatusCode) throw new ApiError($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {LastRaw}");
    }

    static string? ErrText(string body)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(body).TryGetProperty("error", out var e) ? e.GetString() : null; }
        catch { return null; }
    }
}
