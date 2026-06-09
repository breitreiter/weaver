using System.Text.Json;

namespace Weaver.Contracts;

// Wire DTOs shared by Weaver.Api and Weaver.Cli. Mirror data-model.md.
// Timestamps stay as ISO-8601 strings (lexicographically ordered); json
// blobs (log fields, span attributes) surface as JsonElement so the API
// returns structured nested JSON rather than escaped strings.

public record ServiceDto(string Id, string Name, string Kind, string? Subsystem, string? OwnerTeam);

public record DependencyDto(string Id, string FromService, string ToService, string Kind, bool? Critical);

public record RequestTypeDto(string Id, string Name, double Weight, IReadOnlyList<string> Path);

public record GraphDto(
    IReadOnlyList<ServiceDto> Services,
    IReadOnlyList<DependencyDto> Dependencies,
    IReadOnlyList<RequestTypeDto> RequestTypes);

public record ServiceDetailDto(
    ServiceDto Service,
    IReadOnlyList<DependencyDto> DependsOn,
    IReadOnlyList<DependencyDto> DependedOnBy);

public record MetricPointDto(string Ts, double Value);

public record MetricSeriesDto(
    string SubjectKind,
    string SubjectId,
    string Metric,
    IReadOnlyList<MetricPointDto> Points);

public record LogEventDto(
    string Id,
    string ServiceId,
    string Ts,
    string Level,
    string TemplateId,
    string Message,
    JsonElement Fields);

public record SpanDto(
    string Id,
    string? ParentSpanId,
    string ServiceId,
    string? EdgeId,
    string Kind,
    int StartOffsetMs,
    int DurationMs,
    int SelfMs,
    string Status,
    JsonElement Attributes);

public record TraceDto(
    string Id,
    string RequestTypeId,
    string RootServiceId,
    string StartedAt,
    int DurationMs,
    string Status);

public record TraceDetailDto(TraceDto Trace, IReadOnlyList<SpanDto> Spans);
