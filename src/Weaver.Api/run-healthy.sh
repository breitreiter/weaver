#!/usr/bin/env bash
# Build + run the weaver API against the HEALTHY exemplar (data/weaver.db).
# A calm, nominal system — `weaver anomalies` reads quiet, `weaver changes` is empty.
# Extra args pass through to `dotnet run` (e.g. ./run-healthy.sh --urls http://localhost:5180).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DB="$REPO_ROOT/data/weaver.db"

if [[ ! -f "$DB" ]]; then
  echo "error: $DB not found." >&2
  echo "generate it: python3 tools/datagen/generate.py --topology data/topology.yaml" >&2
  exit 1
fi

echo "weaver API -> HEALTHY exemplar"
echo "  WEAVER_DB=$DB"
exec env WEAVER_DB="$DB" dotnet run --project "$SCRIPT_DIR" -c Debug "$@"
