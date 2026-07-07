#!/usr/bin/env bash
# deterministic — version exact + time within 60s of 09:03:47
a="$1"
ver=$(jq -r '.version // empty' "$a")
at=$(jq -r '.deployed_at // empty' "$a")
t=$(grep -oE '[0-9]{2}:[0-9]{2}:[0-9]{2}' <<<"$at" | head -1)
ok_time=false
if [[ -n "$t" ]]; then
  IFS=: read -r hh mm ss <<<"$t"
  secs=$((10#$hh*3600 + 10#$mm*60 + 10#$ss)); ref=$((9*3600 + 3*60 + 47))
  d=$((secs-ref)); ((d<0)) && d=$((-d)); ((d<=60)) && ok_time=true
fi
[[ "$ver" == "2.4.1" && "$ok_time" == true ]] && { echo "ok (2.4.1 @ $t)"; exit 0; }
echo "want 2.4.1 @ 09:03:47±60s; got version=$ver at=$at"; exit 1
