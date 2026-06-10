namespace Weaver.Core;

// The board ("wall of red string") — writable user content, so it lives in its
// own store, NOT the read-only telemetry DB. A board is built by pinning foraged
// findings (items) and drawing edges between them. EF owns this schema
// (EnsureCreated), so no column mapping needed.

public class BoardEntity
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public class BoardItemEntity
{
    public string Id { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string Kind { get; set; } = "";        // service | edge | log | anomaly | trace | metric | note
    public string Ref { get; set; } = "";         // what was pinned (a service id, a log id, "anomaly:payments-db:latency_p99", …)
    public string Evidence { get; set; } = "{}";  // json snapshot that justified the pin (shape_code / log line / delta)
    public string? Label { get; set; }
    public double? X { get; set; }                // manual placement (the wall is arranged by hand)
    public double? Y { get; set; }
    public string CreatedAt { get; set; } = "";
}

public class BoardEdgeEntity
{
    public string Id { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string FromItem { get; set; } = "";
    public string ToItem { get; set; } = "";
    public string Kind { get; set; } = "causal";  // dependency (tool fact) | causal | temporal | custom (the red string)
    public string? Label { get; set; }
    public string DrawnBy { get; set; } = "agent"; // human | agent
    public string CreatedAt { get; set; } = "";
}
