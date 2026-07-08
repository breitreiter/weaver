#!/usr/bin/env bash
# judge.sh <rung-dir-name> <answer.json> [transcript.txt]
# Scores a judged rung (4/8/9/10) with the off-distribution GLM-5.2 judge on
# Cloudflare (via minrouter). Prints a JSON line: {rung, pass, criteria, notes}.
# The judge only emits per-criterion booleans; pass.jq applies the threshold here,
# so the pass bar stays deterministic. Requires $MINROUTER_KEY.
set -uo pipefail

EVAL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXTRACT="$EVAL_DIR/lib/extract-last-json.sh"
JUDGE_SYS="$EVAL_DIR/judge.md"
MR_URL="${MR_URL:-http://imp:8086}"
JUDGE_MODEL="${JUDGE_MODEL:-workers-ai/@cf/zai-org/glm-5.2}"

rung="$1"; answer_file="$2"; transcript_file="${3:-}"
rung_dir="$EVAL_DIR/rungs/$rung"
rubric="$rung_dir/rubric.md"
passjq="$rung_dir/pass.jq"
[[ -f "$rubric" && -f "$passjq" ]] || { echo "no rubric/pass.jq for $rung" >&2; exit 2; }
[[ -n "${MINROUTER_KEY:-}" ]] || { echo "MINROUTER_KEY not set" >&2; exit 2; }

# Build the judge's user message: rubric + candidate answer + (optional) transcript.
# Assembled into a temp file and passed to jq via --rawfile, NOT --arg: a long
# transcript (rungs 8-10 with a verbose harness run to 100KB+) as a jq --arg blows
# past the argv limit ("Argument list too long"). The transcript is capped to its
# tail — the answer JSON is already included in full, and the final synthesis is
# what corroborates the reasoning — so judge input stays bounded and cheap.
umsg="$(mktemp)"; trap 'rm -f "$umsg"' EXIT
{
  cat "$rubric"
  printf '\n\n--- CANDIDATE ANSWER (JSON) ---\n'; cat "$answer_file"
  if [[ -n "$transcript_file" && -f "$transcript_file" ]]; then
    printf '\n\n--- CANDIDATE TRANSCRIPT (tail) ---\n'; tail -c 40000 "$transcript_file"
  fi
} > "$umsg"

body="$(jq -n --arg model "$JUDGE_MODEL" --rawfile sys "$JUDGE_SYS" --rawfile usr "$umsg" \
  '{model:$model, temperature:0, max_tokens:1200,
    messages:[{role:"system",content:$sys},{role:"user",content:$usr}]}')"

# cf/GLM-5.2 is intermittently slow or errors transiently (seen: bad-format 2019,
# empty body, and outright hangs). Retry a few times with a hard per-call timeout so
# one bad call can't stall the matrix; judge-pass.sh re-runs any that still error.
content=""; resp=""
for attempt in 1 2 3; do
  resp="$(curl -s --max-time 75 -H "Authorization: Bearer $MINROUTER_KEY" \
    -H 'content-type: application/json' -d "$body" "$MR_URL/x/cf/compat/chat/completions")"
  content="$(jq -r '.choices[0].message.content // empty' <<<"$resp" 2>/dev/null)"
  [[ -n "$content" ]] && break
  sleep 2
done
if [[ -z "$content" ]]; then
  echo "judge call failed after $attempt tries: $(jq -c '.error // .' <<<"$resp" 2>/dev/null | head -c 200 || head -c 200 <<<"$resp")" >&2
  exit 1
fi

# The judge should reply with a fenced json block; fall back to raw-parse.
verdict="$(printf '%s' "$content" | "$EXTRACT" /dev/stdin 2>/dev/null)" \
  || verdict="$(jq -c . <<<"$content" 2>/dev/null)" \
  || { echo "judge returned unparseable output: $(head -c 200 <<<"$content")" >&2; exit 1; }

criteria="$(jq -c '.criteria // {}' <<<"$verdict")"
notes="$(jq -r '.notes // ""' <<<"$verdict")"
pass="$(jq -c -f "$passjq" <<<"$criteria")"

jq -nc --arg rung "$rung" --argjson pass "${pass:-false}" \
  --argjson criteria "$criteria" --arg notes "$notes" \
  '{rung:$rung, pass:$pass, criteria:$criteria, notes:$notes}'
