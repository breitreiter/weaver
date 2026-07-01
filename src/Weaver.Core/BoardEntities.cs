namespace Weaver.Core;

// The board — writable user content, so it lives in its own store, NOT the
// read-only telemetry DB. EF owns this schema (EnsureCreated).
//
// A board carries pinned nodes + their evidence (the shoebox) and a co-edited
// markdown document (the synthesis surface — RCA / PIR / plan). A node is a service
// placed on the board; everything heterogeneous (anomalies, logs, traces, metrics,
// changes) is EVIDENCE layered onto a node. A node is identified within its board by
// its serviceId (node ↔ service is 1:1), so evidence references a service directly;
// relationships between findings live in the document's prose, not as graph edges.

public class BoardEntity
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string CreatedAt { get; set; } = "";

    // The co-edited document — the board's synthesis surface (RCA / PIR / plan),
    // co-written by the human and the agent. Markdown is the canonical artifact.
    // DocVersion is a monotonic counter for optimistic concurrency: a writer sends
    // the version it last saw; if the server has moved on, edits are 3-way merged
    // (DocMerge) using the writer's base text as ancestor. See co-edit-document.md.
    public string Doc { get; set; } = "";
    public int DocVersion { get; set; }
}

// A service placed on the board. One per (board, service). Pinning the same
// service again just adds evidence — the node is ensured, not duplicated.
public class BoardNodeEntity
{
    public string Id { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string ServiceId { get; set; } = "";   // the node's identity within the board
    public string? Label { get; set; }
    public string CreatedAt { get; set; } = "";
}

// A finding layered onto a node — the justification for caring about (service,
// time, aspect). Kind is an EVIDENCE kind: anomaly | log | trace | metric | change.
public class EvidenceEntity
{
    public string Id { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string ServiceId { get; set; } = "";   // the node this evidence hangs on
    public string Kind { get; set; } = "";         // anomaly | log | trace | metric | change
    public string Aspect { get; set; } = "";       // e.g. "latency_p99" / "db.pool.timeout" / "route:checkout"
    public string? At { get; set; }                // time t or window the interest is about
    public string Payload { get; set; } = "{}";    // json snapshot that justified the pin
    public string? Label { get; set; }
    public string? RefId { get; set; }             // canonical typed id (an:svc:metric, tr:…) — the @-reference handle
    public string CreatedAt { get; set; } = "";
}
