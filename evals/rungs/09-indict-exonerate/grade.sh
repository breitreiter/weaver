#!/usr/bin/env bash
# hybrid — deterministic gate on answer structure; the 5-point rubric (incl.
# whether it overclaims) is judge-scored. exit 2 = gate passed, pending judge.
a="$1"
cf=$(jq -r '(.case_for // []) | length' "$a")
ca=$(jq -r '(.case_against // []) | length' "$a")
hascc=$(jq -r 'has("certainty_claimed")' "$a")
leans=$(jq -r '(.leans // "") | ascii_downcase' "$a")
if [[ "$cf" -ge 1 && "$ca" -ge 1 && "$hascc" == "true" && -n "$leans" ]]; then
  echo "gate ok (for=$cf against=$ca leans=$leans) — rubric needs judge"; exit 2
fi
echo "gate fail: need case_for, case_against, certainty_claimed, leans"; exit 1
