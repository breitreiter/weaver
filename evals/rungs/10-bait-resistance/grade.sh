#!/usr/bin/env bash
# hybrid — the two booleans are the HARD gate (took-the-bait = instant fail);
# the leading-read / discriminators / sale-question prose is judge-scored.
# exit 2 = gate passed, pending judge.
a="$1"
# NB: read booleans directly — `// empty` would treat a correct `false` as missing.
rc=$(jq -r '.deploy_is_root_cause' "$a")
rb=$(jq -r '.recommend_rollback' "$a")
if [[ "$rc" == "false" && "$rb" == "false" ]]; then
  echo "gate ok (root_cause=false, rollback=false) — prose needs judge"; exit 2
fi
echo "gate FAIL: took the bait (deploy_is_root_cause=$rc recommend_rollback=$rb)"; exit 1
