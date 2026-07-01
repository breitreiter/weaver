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

// --- analysis primitives (enumerations, never verdicts) ------------------

public record BlastNodeDto(string ServiceId, int Hops);

public record BlastRadiusDto(string Node, int Count, IReadOnlyList<BlastNodeDto> Dependents);

// One signal that moved between the base window and the subject window.
// Lists what moved — cause AND collateral, undifferentiated.
public record AnomalyDto(
    string SubjectKind,
    string SubjectId,
    string Metric,
    double BaseMean,
    double SubjectMean,
    double DeltaPct,
    double Z,
    string Direction,   // "up" | "down"
    string? OnsetTs);

// Earliest onset per subject — orders precedence, crowns nothing.
public record TimelineEntryDto(string SubjectId, string SubjectKind, string Metric, string OnsetTs, double Z);

// The observed relationships between two services — facts the operator can ground a
// claim in (direct dependency, shared route, temporal precedence). Enumerates what
// the data holds; never ranks one as the cause. From/To carry the relationship's
// real direction. See sensemaking-pivot.md.
public record RelationshipDto(
    string Group,            // dependency | route | temporal
    string From,
    string To,
    string EdgeKind,         // board edge kind this would draw (dependency | route | temporal)
    string Title,
    string Detail,
    string SuggestedLabel,
    object? Evidence);

public record RelationshipsDto(string A, string B, IReadOnlyList<RelationshipDto> Relationships);

// --- the board (sensemaking; co-built by human + agent) ------------------

// A board node is a service placed on the wall, carrying its layered evidence.
// Summary is the one-line, kind-aware rendering of the payload — computed
// server-side so the CLI's `board show` and the UI's evidence card read the SAME
// words. (Both used to render this independently; the UI copy is retired.)
// RefId is the canonical TYPED id (an:svc:metric, tr:…, log:…) — the same identity
// used to pin from the CLI and to `@`-reference the finding in the document. Id is
// the opaque storage handle (what `unpin`/delete take); RefId is the shared one.
public record EvidenceItemDto(string Id, string Kind, string Aspect, string? At, JsonElement? Payload, string? Label, string Summary, string? RefId);
public record BoardNodeDto(string ServiceId, string? Label, IReadOnlyList<EvidenceItemDto> Evidence);
public record BoardDto(string Id, string Title, string CreatedAt,
    IReadOnlyList<BoardNodeDto> Nodes, string Doc, int DocVersion);

// PUT the co-edited document. BaseVersion = the version the writer last saw;
// BaseText = what it was editing from (the merge ancestor when the server has
// moved on). Conflict (in DocDto) means the writer should refetch + re-diff.
public record DocPutReq(int BaseVersion, string? BaseText, string? Text);
public record DocDto(string Doc, int DocVersion, bool Conflict);

// request bodies
public record CreateBoardReq(string? Title);
// pin = ensure a node per service + (optionally) layer one piece of evidence onto
// the first. ServiceIds carries multiple for a trace (its participant services).
public record PinReq(IReadOnlyList<string> ServiceIds, EvidenceRefDto? Evidence, string? Label);
public record CreatedDto(string Id, string Url);

// --- change events (deploys/config/flags — telemetry, per the dataset contract)
public record ChangeEventDto(string Id, string Ts, string Kind, string? TargetId, string Summary, JsonElement Fields);

// --- search API (the left-panel query layer; see search-api.md) ----------

public record WindowDto(string Start, string End);

public record FacetsDto(
    WindowDto Window,
    IReadOnlyList<string> Subsystems,
    IReadOnlyList<string> Kinds,
    IReadOnlyList<string> Teams,
    IReadOnlyList<string> Metrics,
    IReadOnlyList<string> LogLevels,
    IReadOnlyList<string> LogTemplates,
    IReadOnlyList<string> Routes,
    IReadOnlyList<string> TraceStatuses,
    IReadOnlyList<string> ChangeKinds);

// What to pin: the node(s) + (optionally) the evidence to layer onto them. RefId
// carries the finding's canonical typed id so the board keeps one identity per
// finding across every surface (see EvidenceItemDto).
public record EvidenceRefDto(string Kind, string Aspect, string? At, object? Payload, string? RefId = null);
public record PinTargetDto(IReadOnlyList<string> NodeIds, EvidenceRefDto? Evidence);

// A single typed, self-describing, pre-resolved-for-pinning search result.
public record SearchResultDto(string Type, string Id, string Title, string Subtitle, object? Payload, PinTargetDto Pin);

// search histogram: honest volume-over-time. Counts the FULL matching set
// bucketed by time — never the capped result page. Powers the chart-wall volume
// layer (logs|traces|changes). StartMs is epoch-ms for a numeric time axis; Ts
// is the bucket-start ISO for tooltips.
public record HistogramBucketDto(string Ts, long StartMs, int Count);
public record HistogramDto(string Scope, WindowDto Window, long BucketMs, int Total,
    IReadOnlyList<HistogramBucketDto> Buckets);

// node-evidence dossier
public record NodeSignalDto(string Metric, string ShapeCode, string Prose);
public record NodeLogGroupDto(string TemplateId, string Level, int Count, string Sample);
public record NodeEvidenceDto(
    ServiceDto Node,
    WindowDto Window,
    IReadOnlyList<NodeSignalDto> Signals,
    IReadOnlyList<NodeLogGroupDto> Logs,
    IReadOnlyList<ChangeEventDto> Changes,
    int TracesParticipated);


