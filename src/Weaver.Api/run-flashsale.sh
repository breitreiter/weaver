#!/usr/bin/env bash
# Build + run the weaver API against the FLASH-SALE incident db
# (data/weaver-flash-sale.db) — the Thursday demo dataset. `weaver anomalies`
# lights up (payments-db pool exhaustion); `weaver changes` lists 5 records.
# Extra args pass through to `dotnet run` (e.g. ./run-flashsale.sh --urls http://localhost:5180).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DB="$REPO_ROOT/data/weaver-flash-sale.db"

if [[ ! -f "$DB" ]]; then
  echo "error: $DB not found." >&2
  echo "generate it: python3 tools/datagen/generate.py --topology data/topology-flashsale.yaml" >&2
  exit 1
fi

echo "weaver API -> FLASH-SALE incident dataset"
echo "  WEAVER_DB=$DB"
exec env WEAVER_DB="$DB" dotnet run --project "$SCRIPT_DIR" -c Debug "$@"
