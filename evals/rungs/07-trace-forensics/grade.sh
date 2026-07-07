#!/usr/bin/env bash
# deterministic — payments-db holds self-time, mode=waiting, wait >> exec
a="$1"
svc=$(jq -r '.service // empty' "$a")
mode=$(jq -r '(.mode // "") | ascii_downcase' "$a")
# NB: read numbers directly — `// empty` would treat a correct exec_ms:0 as missing.
w=$(jq -r '.wait_ms' "$a")
e=$(jq -r '.exec_ms' "$a")
is_num() { [[ "$1" =~ ^[0-9]+([.][0-9]+)?$ ]]; }
ok=false
if [[ "$svc" == "payments-db" && "$mode" == wait* ]] && is_num "$w" && is_num "$e"; then
  # wait >> exec: exec 0 with wait>0, or ratio > 10
  awk -v w="$w" -v e="$e" 'BEGIN{ if ((e==0 && w>0) || (e>0 && w/e>10)) exit 0; exit 1 }' && ok=true
fi
[[ "$ok" == true ]] && { echo "ok (wait=$w exec=$e)"; exit 0; }
echo "want payments-db/waiting/wait>>exec; got service=$svc mode=$mode wait=$w exec=$e"; exit 1
