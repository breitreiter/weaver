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
