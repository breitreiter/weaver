#!/usr/bin/env bash
# hybrid — deterministic gate on chain shape (storefront-bff optional); the
# hop-justification prose is judge-scored. exit 2 = gate passed, pending judge.
a="$1"
chain=$(jq -r '(.chain // []) | map(ascii_downcase) | join(",")' "$a")
first=$(jq -r '(.chain // []) | (map(ascii_downcase))[0] // empty' "$a")
last=$(jq -r '(.chain // []) | (map(ascii_downcase))[-1] // empty' "$a")
pi=$(jq -r '(.chain // []) | map(ascii_downcase) | index("payments-api")' "$a")
ci=$(jq -r '(.chain // []) | map(ascii_downcase) | index("checkout-api")' "$a")
if [[ "$first" == "payments-db" && "$last" == "web-gateway" \
      && "$pi" != null && "$ci" != null && "$pi" -lt "$ci" ]]; then
  echo "gate ok ($chain) — hop justification needs judge"; exit 2
fi
echo "chain gate fail; got [$chain]"; exit 1
