namespace Weaver.Core;

// The board ("wall of red string") — writable user content, so it lives in its
// own store, NOT the read-only telemetry DB. EF owns this schema (EnsureCreated).
//
// The board has ONE primitive: a node, which is a service placed on the wall.
// Everything heterogeneous (anomalies, logs, traces, metrics, changes) is
// EVIDENCE layered onto a node — never a wall object in its own right. A node is
// identified within its board by its serviceId (node ↔ service is 1:1), so edges
// and evidence reference a service directly.

public class BoardEntity
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string CreatedAt { get; set; } = "";
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
    public string CreatedAt { get; set; } = "";
}

public class BoardEdgeEntity
{
    public string Id { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string FromService { get; set; } = "";
    public string ToService { get; set; } = "";
    public string Kind { get; set; } = "causal";  // dependency (tool fact) | causal | temporal | custom (the red string)
    public string? Label { get; set; }
    public string DrawnBy { get; set; } = "agent"; // human | agent
    public bool CrossedOut { get; set; }           // the operator cut this thread (kept, struck through)
    public string CreatedAt { get; set; } = "";
}
