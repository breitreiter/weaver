#!/usr/bin/env bash
# deterministic — the node (not the edge) whose error_rate moved hardest
a="$1"
svc=$(jq -r '.service // empty' "$a")
m=$(jq -r '.metric // empty' "$a")
[[ "$svc" == "payments-db" && "$m" == "error_rate" ]] && { echo "ok"; exit 0; }
echo "want payments-db / error_rate; got service=$svc metric=$m"; exit 1
