You are investigating a microservice system from its observed telemetry. You have
a command-line tool called `weaver` (already on your PATH) that exposes the
system's services, dependencies, metrics, logs, traces, and change events. A
backing API is already running — you don't need to start anything.

Run `weaver` with no arguments to see the full command list and help, then use the
commands to answer the task below.

Notes:
- Everything `weaver` reports is derived live from raw observed telemetry. There
  is no stored "status", "answer", or "root cause" — only the facts the commands
  print. Work only from what the tool shows you.
- Some list views abbreviate long ids for display. If a command rejects an id as
  unknown, re-run the listing command with `--json` to get the full id.

When you are done, finish your reply with a single fenced ```json code block that
matches the schema at the end of the task. Put your answer in that block; keep any
reasoning in prose above it. If you emit more than one JSON block, only the last
one is read.
