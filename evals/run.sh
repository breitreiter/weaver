#!/usr/bin/env bash
# evals/run.sh — one-shot agent-eval runner for weaver.
#
# Composes orientation.md + a rung's prompt.md, drives a harness in a throwaway
# /tmp sandbox, extracts the model's final JSON answer, grades it deterministically,
# and appends a result row to runs/results-<stamp>.jsonl. Judged rungs (4/8/9/10)
# report their deterministic GATE here; the prose verdict is a separate judge pass.
#
# Usage:
#   evals/run.sh --harness claude [--model sonnet] --rungs 1,2,3 -n 1
#   evals/run.sh --harness claude --rungs all -n 3
#
# Harnesses: claude | codex | nb   (see project/plans/agent-evals.md for the matrix)
set -uo pipefail

EVAL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$EVAL_DIR")"
ORIENT="$EVAL_DIR/orientation.md"
RUNGS_DIR="$EVAL_DIR/rungs"
OUT_DIR="$EVAL_DIR/runs"
EXTRACT="$EVAL_DIR/lib/extract-last-json.sh"

HARNESS="" ; MODEL="" ; RUNGS="all" ; N=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --harness) HARNESS="$2"; shift 2 ;;
    --model)   MODEL="$2";   shift 2 ;;
    --rungs)   RUNGS="$2";   shift 2 ;;
    -n)        N="$2";       shift 2 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done
[[ -n "$HARNESS" ]] || { echo "need --harness claude|codex|nb" >&2; exit 2; }

case "$HARNESS" in
  claude) MODEL="${MODEL:-sonnet}" ;;
  codex)  MODEL="${MODEL:-gpt-5.5}" ;;
  nb)     MODEL="${MODEL:-qwen-coder}" ;;
  *) echo "unknown harness: $HARNESS" >&2; exit 2 ;;
esac

all_rungs() { ls "$RUNGS_DIR" | sort; }
if [[ "$RUNGS" == "all" ]]; then
  mapfile -t RUNG_DIRS < <(all_rungs)
else
  RUNG_DIRS=()
  IFS=',' read -ra nums <<<"$RUNGS"
  for n in "${nums[@]}"; do
    d=$(all_rungs | grep -E "^0*${n}-" || true)
    [[ -n "$d" ]] && RUNG_DIRS+=("$d")
  done
fi
[[ ${#RUNG_DIRS[@]} -gt 0 ]] || { echo "no rungs matched '$RUNGS'" >&2; exit 2; }

# --- harness invocations (sandbox cwd = $2) --------------------------------
# claude sandboxes at the tool layer (only Bash(weaver:*), no Read). nb and codex
# have NO usable read boundary: nb under --trust reads any file the user can (the C#
# "sandbox" is a bypassable string check), and codex's workspace-write sandbox
# restricts writes/network but NOT reads — verified it `cat`s a canary outside its
# cwd. So BOTH run the whole process inside a bwrap read-jail that masks /home+/tmp
# (repo + answer key + other runs) and re-exposes only the weaver binary, the
# sandbox cwd, and the harness's own runtime.
NB_BIN="${NB_BIN:-$HOME/repos/nb/bin/Debug/net10.0/nb}"
# weaver is `dotnet run --project <repo>/src/Weaver.Cli` on the host, i.e. it lives
# INSIDE the repo the jail must hide. So the jailed legs use a self-contained weaver
# binary published OUTSIDE the repo — expose only that, keep the repo masked.
#   dotnet publish src/Weaver.Cli -c Release -r linux-x64 --self-contained \
#     -p:PublishSingleFile=true -o ~/.local/lib/weaver-eval
WEAVER_EVAL_DIR="${WEAVER_EVAL_DIR:-$HOME/.local/lib/weaver-eval}"
# jail <harness> <cwd> <cmd...> — run <cmd> in the bwrap read-jail. The base masks
# /home+/tmp and re-exposes the weaver binary + $cwd (writable, HOME); each harness
# adds only its own runtime dirs. codex gets a throwaway CODEX_HOME under $cwd so it
# can't read the real ~/.codex sessions/history either (caller seeds its auth.json).
jail() {
  local h="$1" cwd="$2"; shift 2
  local -a extra=(); local jpath="$WEAVER_EVAL_DIR:/usr/bin:/bin"
  case "$h" in
    nb)  local nb_dir; nb_dir="$(dirname "$NB_BIN")"
         extra+=( --ro-bind "$nb_dir" "$nb_dir" )
         [[ -d "$HOME/.dotnet" ]] && extra+=( --ro-bind "$HOME/.dotnet" "$HOME/.dotnet" ) ;;
    codex) extra+=( --ro-bind "$HOME/.npm-global" "$HOME/.npm-global"
                    --setenv CODEX_HOME "$cwd/.codex" )
           jpath="$WEAVER_EVAL_DIR:$HOME/.npm-global/bin:/usr/bin:/bin" ;;
  esac
  bwrap --ro-bind / / --dev /dev --proc /proc \
    --tmpfs /home --tmpfs /tmp \
    --ro-bind "$WEAVER_EVAL_DIR" "$WEAVER_EVAL_DIR" \
    "${extra[@]}" \
    --bind "$cwd" "$cwd" --setenv HOME "$cwd" \
    --setenv PATH "$jpath" --chdir "$cwd" \
    -- "$@"
}
run_claude() { ( cd "$2" && claude -p "$1" --model "$MODEL" --allowedTools 'Bash(weaver:*)' 2>&1 ); }
run_nb()     { jail nb "$2" "$NB_BIN" --trust "$1" 2>&1; }
run_codex() {  # codex reads outside cwd, so jail it; bwrap IS the sandbox → bypass codex's
  local cwd="$2"
  mkdir -p "$cwd/.codex"; cp "$HOME/.codex/auth.json" "$cwd/.codex/auth.json"
  jail codex "$cwd" codex exec -m "$MODEL" --skip-git-repo-check \
       --dangerously-bypass-approvals-and-sandbox "$1" </dev/null 2>&1
}

# --- preconditions ---------------------------------------------------------
if ! weaver overview >/dev/null 2>&1; then
  echo "precondition: 'weaver overview' failed — is the API up? (ask Joseph)" >&2
  exit 1
fi
if [[ "$HARNESS" == "nb" || "$HARNESS" == "codex" ]]; then
  command -v bwrap >/dev/null || { echo "precondition: bwrap required for the $HARNESS read-jail (it has no read boundary of its own)" >&2; exit 1; }
  [[ -x "$WEAVER_EVAL_DIR/weaver" ]] || { echo "precondition: no self-contained weaver at $WEAVER_EVAL_DIR/weaver — publish it (see run.sh)" >&2; exit 1; }
  # jail self-test: the repo (answer key, plans, graders, rubrics) MUST be masked,
  # AND weaver MUST be runnable inside the jail.
  st="$(mktemp -d /tmp/weaver-eval-jailtest-XXXX)"
  if jail "$HARNESS" "$st" test -e "$EVAL_DIR/orientation.md" 2>/dev/null; then
    rm -rf "$st"; echo "precondition: $HARNESS jail LEAKS the repo ($EVAL_DIR readable inside) — refusing to run $HARNESS" >&2; exit 1
  fi
  if ! jail "$HARNESS" "$st" weaver overview >/dev/null 2>&1; then
    rm -rf "$st"; echo "precondition: weaver not runnable inside the $HARNESS jail (binary missing or API down)" >&2; exit 1
  fi
  rm -rf "$st"
fi
if [[ "$HARNESS" == "nb" ]]; then
  ssh imp '~/.local/bin/swap-model status' 2>/dev/null | grep -qiE 'qcoder|qwen' \
    || { echo "precondition: qwen-coder not loaded on imp (ssh imp swap-model qcoder)" >&2; exit 1; }
fi
if [[ "$HARNESS" == "codex" ]]; then
  [[ -f "$HOME/.codex/auth.json" ]] || { echo "precondition: no ~/.codex/auth.json — run 'codex login'" >&2; exit 1; }
fi

mkdir -p "$OUT_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"
RESULTS="$OUT_DIR/results-$STAMP.jsonl"
echo "# harness=$HARNESS model=$MODEL rungs=[${RUNG_DIRS[*]}] N=$N"
echo "# -> $RESULTS"
echo

for rung in "${RUNG_DIRS[@]}"; do
  prompt="$(cat "$ORIENT"; echo; echo; cat "$RUNGS_DIR/$rung/prompt.md")"
  grader="$RUNGS_DIR/$rung/grade.sh"
  for ((run=1; run<=N; run++)); do
    sandbox="$(mktemp -d /tmp/weaver-eval-XXXXXXXX)"
    t0=$SECONDS
    case "$HARNESS" in
      claude) transcript="$(run_claude "$prompt" "$sandbox")" ;;
      codex)  transcript="$(run_codex  "$prompt" "$sandbox")" ;;
      nb)     transcript="$(run_nb     "$prompt" "$sandbox")" ;;
    esac
    wall=$((SECONDS - t0))
    tpath="$OUT_DIR/${rung}__${HARNESS}__${MODEL//\//-}__run${run}.txt"
    printf '%s\n' "$transcript" > "$tpath"

    if answer="$(printf '%s' "$transcript" | "$EXTRACT" /dev/stdin 2>/dev/null)"; then
      valid=true
    else
      valid=false; answer='null'
    fi

    if [[ "$valid" == true && -f "$grader" ]]; then
      printf '%s' "$answer" > "$sandbox/answer.json"
      reason="$(bash "$grader" "$sandbox/answer.json" 2>&1)"; gcode=$?
    else
      reason="no valid JSON answer block"; gcode=1
    fi
    case $gcode in
      0) status=pass ;;
      2) status=needs-judge ;;
      *) status=fail ;;
    esac

    jq -nc \
      --arg rung "$rung" --arg harness "$HARNESS" --arg model "$MODEL" \
      --argjson run "$run" --argjson valid "$valid" --arg status "$status" \
      --argjson wall "$wall" --arg reason "$reason" --arg tpath "$tpath" \
      --argjson answer "${answer:-null}" '
      {rung:$rung, harness:$harness, model:$model, run:$run,
       valid_answer:$valid, status:$status,
       pass:(if $status=="pass" then true elif $status=="fail" then false else null end),
       wall_secs:$wall, reason:$reason, answer:$answer, transcript_path:$tpath}' \
      >> "$RESULTS"

    printf '  %-22s run%d  %-11s %4ds  %s\n' "$rung" "$run" "$status" "$wall" "$reason"
    rm -rf "$sandbox"
  done
done

echo
echo "results: $RESULTS"
jq -rs 'group_by(.rung)[] | .[0].rung + "  " + ([.[].status] | join(" "))' "$RESULTS"
