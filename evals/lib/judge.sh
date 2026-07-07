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
user="$(cat "$rubric")

--- CANDIDATE ANSWER (JSON) ---
$(cat "$answer_file")"
if [[ -n "$transcript_file" && -f "$transcript_file" ]]; then
  user="$user

--- CANDIDATE TRANSCRIPT ---
$(cat "$transcript_file")"
fi

body="$(jq -n --arg model "$JUDGE_MODEL" --arg sys "$(cat "$JUDGE_SYS")" --arg usr "$user" \
  '{model:$model, temperature:0, max_tokens:1200,
    messages:[{role:"system",content:$sys},{role:"user",content:$usr}]}')"

resp="$(curl -s -H "Authorization: Bearer $MINROUTER_KEY" -H 'content-type: application/json' \
  -d "$body" "$MR_URL/x/cf/compat/chat/completions")"
content="$(jq -r '.choices[0].message.content // empty' <<<"$resp")"
if [[ -z "$content" ]]; then
  echo "judge call failed: $(jq -c '.error // .' <<<"$resp" 2>/dev/null || echo "$resp" | head -c 300)" >&2
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
