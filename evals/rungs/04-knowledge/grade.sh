#!/usr/bin/env bash
# hybrid — deterministic gate on the kn: id; the one-line summary is judge-scored.
# exit 2 = gate passed, prose pending judge.
a="$1"
id=$(jq -r '.snippet_id // empty' "$a")
norm="${id#kn:}"
if [[ "$norm" == "kn-payments-db-runbook-pool" ]]; then
  echo "gate ok (id) — summary needs judge"; exit 2
fi
echo "gate fail: want kn:kn-payments-db-runbook-pool; got $id"; exit 1
