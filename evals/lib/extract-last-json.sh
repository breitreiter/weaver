#!/usr/bin/env bash
# extract-last-json.sh <file> — print the candidate's final JSON answer.
# Strategy: (1) prefer the LAST fenced ```json block; (2) fall back to the last
# brace-balanced {...} object anywhere in the (ANSI-stripped) text — some harnesses
# (nb's TUI) re-render the model's ```json fence as their own divider box, so the
# literal fences don't survive into the captured transcript. Exit 1 if none parse.
set -uo pipefail
content="$(cat "${1:-/dev/stdin}")"

# print the last jq-parseable chunk from \x1e-separated candidates on stdin
pick_last() {
  mapfile -d $'\x1e' -t arr
  local i b
  for ((i=${#arr[@]}-1; i>=0; i--)); do
    b="${arr[i]}"
    [[ -z "${b//[$'\n\t ']/}" ]] && continue
    if jq -e . >/dev/null 2>&1 <<<"$b"; then printf '%s' "$b"; return 0; fi
  done
  return 1
}

# 1) fenced code blocks (``` or ```json), last-valid-wins
fenced="$(awk '
  /^[[:space:]]*```/ { if (inb) { printf "%s\x1e", buf; inb=0; buf="" } else { inb=1; buf="" } next }
  inb { buf = buf $0 "\n" }
' <<<"$content")"
if out="$(pick_last <<<"$fenced")"; then printf '%s' "$out"; exit 0; fi

# 2) fallback: strip ANSI, scan top-level {...} objects (string/escape aware)
objs="$(sed 's/\x1b\[[0-9;]*m//g' <<<"$content" | awk '
  { s = s $0 "\n" }
  END {
    n=length(s); depth=0; instr=0; esc=0; start=0
    for (i=1; i<=n; i++) {
      c = substr(s,i,1)
      if (instr) { if (esc) esc=0; else if (c=="\\") esc=1; else if (c=="\"") instr=0; continue }
      if (c=="\"") { instr=1; continue }
      if (c=="{") { if (depth==0) start=i; depth++ }
      else if (c=="}") { depth--; if (depth==0 && start>0) { printf "%s\x1e", substr(s,start,i-start+1); start=0 } }
    }
  }')"
if out="$(pick_last <<<"$objs")"; then printf '%s' "$out"; exit 0; fi
exit 1
