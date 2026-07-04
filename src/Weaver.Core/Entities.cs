namespace Weaver.Core;

// EF read-models mapped onto the generated weaver.db tables (snake_case
// columns). These are query-only — the context opens the DB read-only and
// tracks nothing. Shapes follow data-model.md.

public class ServiceEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Subsystem { get; set; }
    public string? OwnerTeam { get; set; }
}

public class DependencyEntity
{
    public string Id { get; set; } = "";
    public string FromService { get; set; } = "";
    public string ToService { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool? Critical { get; set; }
}

public class RequestTypeEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Weight { get; set; }
    public string Path { get; set; } = "[]"; // json array of service ids
}

public class MetricSampleEntity // keyless — narrow/long series table
{
    public string SubjectKind { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string Ts { get; set; } = "";
    public string Metric { get; set; } = "";
    public double Value { get; set; }
}

public class LogEventEntity
{
    public string Id { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public string Ts { get; set; } = "";
    public string Level { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string Message { get; set; } = "";
    public string Fields { get; set; } = "{}"; // json
    public string? TraceId { get; set; }       // set when the log was emitted under a trace
    public string? SpanId { get; set; }
}

public class TraceEntity
{
    public string Id { get; set; } = "";
    public string RequestTypeId { get; set; } = "";
    public string RootServiceId { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public int DurationMs { get; set; }
    public string Status { get; set; } = "";
}

// Deploys / config changes / feature flags (per the dataset contract). May not
// exist in the DB yet — the generator adds it on regeneration; queries are
// guarded so the API works before then.
public class ChangeEventEntity
{
    public string Id { get; set; } = "";
    public string Ts { get; set; } = "";
    public string Kind { get; set; } = "";        // deploy | config | migration | feature_flag
    public string? TargetId { get; set; }          // service it touched (null = fleet-wide)
    public string Summary { get; set; } = "";
    public string Fields { get; set; } = "{}";     // json
}

// Knowledge snippets — a blended bag of authored factoids (docs / runbooks /
// prior incidents / prior board text) about the system. Observed artifacts, not
// derived judgments; each attaches to one service and carries no timestamp.
// DocRef+Seq chain the chunks of one document. May not exist in the DB yet
// (older generated dbs predate the table) — queries are guarded.
public class KnowledgeSnippetEntity
{
    public string Id { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public string Source { get; set; } = "";     // doc | runbook | incident | board
    public string? SourceRef { get; set; }         // citation flavor: INC-2411, wiki/…
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? DocRef { get; set; }            // parent-document id for multi-chunk docs
    public int? Seq { get; set; }                  // position within DocRef; null for a loose factoid
}

public class SpanEntity
{
    public string Id { get; set; } = "";
    public string TraceId { get; set; } = "";
    public string? ParentSpanId { get; set; }
    public string ServiceId { get; set; } = "";
    public string? EdgeId { get; set; }
    public string Kind { get; set; } = "";
    public int StartOffsetMs { get; set; }
    public int DurationMs { get; set; }
    public int SelfMs { get; set; }
    public string Status { get; set; } = "";
    public string Attributes { get; set; } = "{}"; // json
}
