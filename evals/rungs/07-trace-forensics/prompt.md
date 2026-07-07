Take one of the slowest `checkout` traces and open it up span by span. Which
service holds the most self-time, and is that time spent executing queries or
waiting on something? Give the numbers (milliseconds waiting vs. executing).

Answer schema:

```json
{
  "trace_id": "<the trace you analyzed>",
  "service": "<service holding the most self-time>",
  "mode": "<waiting or executing>",
  "wait_ms": 0,
  "exec_ms": 0
}
```
