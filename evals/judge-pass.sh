#!/usr/bin/env bash
# judge-pass.sh <results.jsonl> — phase 2 of the eval.
#
# run.sh grades the deterministic rungs inline and marks the judged rungs (4/8/9/10)
# `needs-judge`. This driver scores exactly those rows with the off-distribution
# GLM-5.2 judge (lib/judge.sh, on Cloudflare via minrouter) and writes a sibling
# results-<stamp>.judged.jsonl with the final pass/fail folded in. Deterministic
# rows pass through untouched. Re-runnable: a row that errored (e.g. cf daily cap
# hit) becomes `judge-error` and is re-judged on the next pass over the .judged file
# or the original. Requires $MINROUTER_KEY.
set -uo pipefail

EVAL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
JUDGE="$EVAL_DIR/lib/judge.sh"
RESULTS="${1:?usage: judge-pass.sh <results.jsonl>}"
[[ -f "$RESULTS" ]] || { echo "no such results file: $RESULTS" >&2; exit 2; }
[[ -n "${MINROUTER_KEY:-}" ]] || { echo "MINROUTER_KEY not set (judge is GLM-5.2 on Cloudflare via minrouter)" >&2; exit 2; }

OUT="${RESULTS%.jsonl}.judged.jsonl"
: > "$OUT"
tmp="$(mktemp)"; err="$(mktemp)"
trap 'rm -f "$tmp" "$err"' EXIT

n_judged=0
while IFS= read -r row; do
  [[ -n "$row" ]] || continue
  status="$(jq -r '.status' <<<"$row")"
  # only needs-judge (and prior judge-error, so re-runs converge) get a judge call
  if [[ "$status" != "needs-judge" && "$status" != "judge-error" ]]; then
    printf '%s\n' "$row" >> "$OUT"; continue
  fi
  rung="$(jq -r '.rung' <<<"$row")"
  tpath="$(jq -r '.transcript_path' <<<"$row")"
  jq -c '.answer' <<<"$row" > "$tmp"

  if verdict="$(bash "$JUDGE" "$rung" "$tmp" "$tpath" 2>"$err")" && [[ -n "$verdict" ]]; then
    newrow="$(jq -c --argjson v "$verdict" '
      .judge   = ($v | {criteria, notes}) |
      .pass    = $v.pass |
      .status  = (if $v.pass then "pass" else "fail" end) |
      .reason  = ($v.notes // .reason)' <<<"$row")"
  else
    reason="judge error: $(head -c 200 "$err" | tr '\n' ' ')"
    newrow="$(jq -c --arg r "$reason" '.status="judge-error" | .reason=$r' <<<"$row")"
  fi
  printf '%s\n' "$newrow" >> "$OUT"
  n_judged=$((n_judged+1))
  printf '  judged %-22s run%s  %-11s %s\n' \
    "$rung" "$(jq -r '.run' <<<"$newrow")" "$(jq -r '.status' <<<"$newrow")" \
    "$(jq -r '.reason' <<<"$newrow" | head -c 80)" >&2
done < "$RESULTS"

echo >&2
echo "judged $n_judged row(s) -> $OUT" >&2
echo >&2
# final per-rung status line, same shape as run.sh's summary
jq -rs 'group_by(.rung)[] | .[0].rung + "  " + ([.[].status] | join(" "))' "$OUT"
