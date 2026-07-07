#!/usr/bin/env bash
# deterministic — service + pool_max
a="$1"
svc=$(jq -r '.service // empty' "$a")
pm=$(jq -r '.pool_max' "$a")
[[ "$svc" == "payments-db" && "$pm" == "40" ]] && { echo "ok"; exit 0; }
echo "want payments-db / 40; got service=$svc pool_max=$pm"; exit 1
