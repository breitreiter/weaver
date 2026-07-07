#!/usr/bin/env bash
# deterministic — exact error_rate onset order
a="$1"
got=$(jq -r '(.order // []) | map(ascii_downcase) | join(",")' "$a")
exp="payments-db,payments-api,checkout-api,web-gateway"
[[ "$got" == "$exp" ]] && { echo "ok"; exit 0; }
echo "want [$exp]; got [$got]"; exit 1
