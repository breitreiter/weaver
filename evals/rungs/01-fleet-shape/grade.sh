#!/usr/bin/env bash
# deterministic — service count + subsystem set
a="$1"
sc=$(jq -r '.service_count' "$a")
subs=$(jq -r '(.subsystems // []) | map(ascii_downcase | gsub("^\\s+|\\s+$";"")) | sort | join(",")' "$a")
exp="analytics,cart,catalog,checkout,edge,fulfillment,identity,notifications,orders,payments,storefront"
[[ "$sc" == "28" && "$subs" == "$exp" ]] && { echo "ok (28 services, 11 subsystems)"; exit 0; }
echo "want 28 + the 11 subsystems; got count=$sc subs=[$subs]"; exit 1
