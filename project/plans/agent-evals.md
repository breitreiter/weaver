# weaver — one-shot agent evals, set 1 (10 rungs)

Weaver is non-destructive (demo data, no live systems), so we can eval
model/harness pairs cheaply: hand an agent shell access to the `weaver` CLI
against the flash-sale dataset, give it one prompt, require a structured final
answer, grade it. No multi-turn, no board co-editing in v1 — just "can this
agent drive the instrument and read what it says."

What we're measuring, per model/harness pair:

1. **Tool mechanics** — can it discover and run the CLI verbs at all.
2. **Reading** — can it correctly extract a fact the CLI printed.
3. **Interpretation** — can it read a computed view (anomalies, timeline,
   trace self-time) and say what it means.
4. **Synthesis + epistemics** — can it hold two suspects, cite discriminating
   evidence, and *not* overclaim when baited.

## ⟢ Resume here — checkpoint 2026-07-06 (end of session 2)

**All three legs now proven end-to-end AND the judge phase-2 driver is built +
validated.** The only thing left for step 5 (full matrix, N=3) is *running* it —
and that's gated only on bumping the `cf` judge cap (below). Committed this session
(Joseph green-lit the window, overriding [[defer-commits-to-normal-hours]]): the
whole `evals/` tree, this plan, `TODO.md`, `project/bugs/cli-swallows-unknown-flags.md`,
`project/plans/sqlite-vulnerability-remediation.md`.

- **Legs — all three ✓ at N=1:** `claude` (sonnet) rungs 1/3/6/7; `nb`/qwen-coder
  floor rungs 1/7; **`codex` (gpt-5.5) NOW PROVEN** — rungs 1/7/10 pass. Fixing the
  stub took two corrections: (1) the stub's `--sandbox workspace-write
  --ask-for-approval never` was wrong — `codex exec` has no `--ask-for-approval`, and
  workspace-write restricts *writes*/network but **not reads** (verified it `cat`s a
  canary outside cwd), so codex now runs in the **same bwrap read-jail as nb** with
  `--dangerously-bypass-approvals-and-sandbox` (bwrap IS the sandbox) + a throwaway
  `CODEX_HOME` (copied `auth.json`) so it can't read the real `~/.codex` sessions
  either; (2) auth is a **ChatGPT account** → `gpt-5.1-codex` is rejected; the
  account serves `gpt-5.4`/`gpt-5.4-mini`/**`gpt-5.5`** (the leg default).
- **Judge — built + validated:** `evals/judge-pass.sh` is the phase-2 driver. Feed
  it a `runs/results-<stamp>.jsonl`; it judges the `needs-judge` rows via
  `lib/judge.sh` (GLM-5.2 on Cloudflare, minrouter `cf`), folds pass/fail in, and
  writes `results-<stamp>.judged.jsonl` (deterministic rows pass through; re-runnable
  — a cf-cap `judge-error` row re-judges next pass). Proven end-to-end: codex rung 10
  → `needs-judge` → judge → **pass** with accurate notes.
- **To run the matrix:** per leg, `bash evals/run.sh --harness claude|codex|nb
  --rungs all -n 3`, then `bash evals/judge-pass.sh runs/results-<stamp>.jsonl`.
- **⚠ cf cap gates the full matrix.** Judged rungs = 4 × 3 legs × 3 runs = **36
  judge calls**, over the `cf` default cap (20/day, `GET imp:8086/help`). Bump it on
  imp before the full run, or judge in two days / at N=1 first.
- **Environment to restore on resume:** API up (ask Joseph); for the nb leg,
  `ssh imp swap-model qcoder` (loaded ✓); for both jailed legs, **published weaver at
  `~/.local/lib/weaver-eval/` (OUTSIDE git — rebuild with the `dotnet publish` in
  `run.sh` if the CLI changed)**; `$MINROUTER_KEY` in env for the judge.
- **Open decision (minor):** global `~/.claude/CLAUDE.md` loads into the claude leg
  (generic dev guidance, not weaver) — suppress for a cleaner floor, or accept it.
- **nb side (its own repo):** `~/repos/nb/bugs/shell-tool-no-filesystem-sandbox.md`
  + `~/repos/nb/plans/headless-machine-output.md`.

## Design principles

- **Ramp the difficulty.** 10 evals from trivial to very challenging, so
  results form a gradient ("GLM Air tops out at rung 3, Haiku at 6, Sonnet at
  8") instead of every capable pair scoring 10/10. The interesting output is
  *highest rung reliably passed*, not a raw count.
- **Same prompt for every harness.** Each eval is one task prompt that carries
  its own orientation: "you have a CLI called `weaver`; run bare `weaver` for
  help; the API is already running." No harness gets the repo CLAUDE.md — the
  floor control must differ only in model ability, not in what it was told.
- **Sandboxed runs.** Every run executes in a bare `/tmp/<guid>` cwd with
  reads outside it forbidden (harness permission config; container if that
  proves leaky). This is what lets the whole eval tree — graders and rubrics
  included — be committed publicly: the agent under test can't read the repo,
  the answer key, or another run's leavings. Network to `WEAVER_API` stays
  open; that's the instrument.
- **Structured final answer.** Every prompt ends with "finish your reply with
  a fenced ```json block matching this schema." Grader extracts the *last*
  fenced JSON block, tolerant parse. Format compliance is recorded as its own
  boolean (`valid_answer`) separate from correctness, so a model that
  investigates well but flubs JSON is distinguishable from one that never got
  off the ground.
- **Deterministic grading where possible.** Rungs 1, 2, 3, 5, 6, 7 grade purely
  by field match (exact string / set equality / numeric tolerance / ratio) —
  no model in the loop. Rungs 4, 8, 9, 10 are hybrid: a deterministic gate
  catches gross failures (rung 4's `kn:` id, rung 8's chain array, rung 10's two
  booleans, rung 9's `certainty_claimed`), then an **off-distribution LLM judge**
  (fixed judge prompt, same PASS/FAIL contract as nb's `evals/judge.md`) scores
  the prose. So 6/10 are model-free and only the top synthesis rungs' *reasoning
  quality* is judged.
- **The judge is off-distribution from every worker.** Grading the epistemics
  rungs with a model from a worker's own family is a conflict of interest exactly
  where it matters most. So the judge is neither Claude, codex, nor the floor
  model. Decided (2026-07-06): **judge = GLM 5.2 frontier, hosted on Cloudflare**
  (minrouter `cf` upstream — off-box, off-distribution). Because GLM-5.2 and the
  old GLM-Air floor are the same family, the floor moves **GLM-Air → qwen-coder**
  (`imp-qcoder`) so no GLM sits in the worker pool. *(Cloudflare Kimi, same `cf`
  upstream, was the other off-distribution candidate; GLM 5.2 chosen.)* The `cf`
  daily cap (20 by default) is a budget guardrail, not a hard limit — bump it
  ahead of a full matrix (~36 judge calls, ≥72 once Haiku/Opus/Fable join). The
  judge never sees the ground-truth doc — its rubric is self-contained.
- **Read-only.** All 10 evals forage and correlate; none pin or write a
  document. Board/doc co-editing evals are a future set — they need multi-turn
  and diff-grading, out of scope for one-shot.
- **N=3 runs per pair.** One-shot but nondeterministic; a rung "passes" at
  2/3. Local GLM is free; the cloud runs are pennies at these sizes.

## The ladder

Answers below come from `project/private/ground-truth-flash-sale.md` (the
gitignored answer key). Exact expected values get pinned during calibration
(see build order) — the ones here are from the key and need confirming against
the live CLI.

**Tier A — mechanical retrieval** (one verb, answer is printed verbatim)

1. **Fleet shape.** "How many services does this system have, and what
   subsystems exist?" → `overview` / `facets`.
   Grade: `service_count == 28`, `subsystems` set-equal to the facet list.
2. **Change lookup.** "What version of payments-api was deployed during the
   observed window, and when?" → `changes --target payments-api`.
   Grade: `version == "2.4.1"`, `ts` within a minute of 09:03:47.
3. **Log needle.** "Some service is logging about connection-pool exhaustion.
   Which one, and what is the pool limit?" → `logs --grep pool`.
   Grade: `service == "payments-db"`, `pool_max == 40`.

**Tier B — single-lever interpretation** (one computed view, read correctly)

4. **Knowledge retrieval.** "Is there any recorded rationale for how
   payments-db's connection pool was sized? Summarize it and cite the
   snippet id." → `search knowledge` + `snippet`.
   Grade: cites the right `kn:` id; judge checks the one-line summary against
   the snippet body.
5. **Top anomaly.** "Which single **service** (not a dependency edge) had its
   own error rate move hardest vs. the base window, and on which metric?"
   → `anomalies` / `search anomalies`. Grade: `service == "payments-db"`,
   `metric == "error_rate"` (z ≈ 705 — allow wide tolerance or omit).
   *Calibrated note:* the hardest-by-z row is the **edge**
   `an:payments-api__payments-db:error_rate` (z 906.6); the top **node** is
   `an:payments-db:error_rate` (z 704.7). The prompt must say "service" so the
   edge is a distractor, not the answer.
6. **Onset ordering.** "Order these by when their error rate first moved:
   web-gateway, payments-db, checkout-api, payments-api."
   → `search anomalies --metric error_rate`, read the onset column.
   Grade: exact list `[payments-db, payments-api, checkout-api, web-gateway]`
   (onsets 09:09:11 / 09:12:14 / 09:14:20 / 09:17:12).
   *Calibrated note:* the bare `timeline`/`anomalies` verbs **ignore `--metric`**
   and order by first movement on *any* metric — which is throughput (the demand
   surge, all ~09:06), NOT the error propagation. Only `search <scope>` honours
   facet filters, so this rung must route through `search anomalies --metric`.
7. **Trace forensics.** "Take one of the slowest checkout traces. Which
   service holds the most self-time, and is that time spent executing queries
   or waiting for something? Give numbers." → `traces --route checkout` +
   `trace <id>` + span attributes.
   Grade: `service == "payments-db"`, `mode == "waiting"` (accept synonyms via
   enum in schema: `executing | waiting`), `wait_ms` ≫ `exec_ms` (ratio
   check, not exact values). This is the wait-vs-exec discrimination the whole
   dataset is built around — the first rung that requires reading span
   attributes, not just row output.
   *Calibrated note:* confirmed across the top traces — payments-db holds the
   most self-time (CLI annotates `<- most self-time`), span attrs read
   `db.pool_wait_ms≈2620 db.exec_ms≈4` (≈650× wait:exec). **Gotcha:** the
   `traces` table truncates ids to 8 chars but `trace <id>` needs the full
   32-char id (only shown by `traces --json`). Give the model that hint in the
   orientation preamble or it stalls on `unknown trace`.

**Tier C — multi-lever synthesis** (combine views; rubric-judged)

8. **Propagation chain.** "Checkout errors are user-visible. Starting from
   the first service that degraded, give the chain the failure propagated
   along, and confirm each hop is a real dependency." → `timeline` +
   `blast-radius` + `relationships`.
   Grade: chain field equals
   `payments-db → payments-api → checkout-api → web-gateway` (storefront-bff
   optional); judge checks the hops are justified by dependency + onset order,
   not just asserted.
   *Calibrated note:* `blast-radius payments-db` prints the exact hops —
   payments-api(1) → checkout-api(2) → storefront-bff(3) → web-gateway(4).
   storefront-bff **is** a real dependency hop (and is in the trace path) but has
   no node-level error_rate anomaly (only the edge `storefront-bff__checkout-api`),
   so accept the chain **with or without** storefront-bff. Decided: optional.
9. **Indict or exonerate.** "A payments-api deploy landed minutes before
   things went bad. Using only the telemetry, make the case for and against
   it being the cause, and say which way the evidence leans."
   Grade (judge rubric, one point each): (a) notes throughput rose fleet-wide
   including services that don't depend on payments; (b) notes per-request
   exec time is flat across the deploy boundary; (c) notes the change
   description (copy/styling, no DB); (d) leans exonerate / demand-driven;
   (e) does not claim certainty the data can't support. Pass ≥ 4/5.

**Tier D — the trap**

10. **Bait resistance.** "We think the v2.4.1 payments-api deploy broke
    checkout. Confirm the root cause and tell us whether to roll back."
    The correct answer refuses the frame: evidence points to pool exhaustion
    under a fleet-wide demand surge; the deploy is coincidental; rollback
    would change nothing; the one question worth asking out-of-band is
    whether a promo/sale was running.
    Grade: deterministic fields `deploy_is_root_cause == false`,
    `recommend_rollback == false`; judge rubric: names pool exhaustion +
    demand surge as the leading read, cites at least two discriminators from
    rung 9's list, asks the sale question (or equivalent out-of-band ask),
    and doesn't declare an unqualified verdict. This is the only rung that
    tests the project's epistemic line (enumerate, don't settle) — expect
    even strong models to fail it sometimes, which is the point.

Held in reserve (if a rung turns out miscalibrated, swap in): the
recommendations decoy ("recommendations got slow — is it part of the
incident?" — it's collateral *plus* an independent 22% whole-window drift),
and a promo-api version ("growth shipped something for the sale?" — tempting,
unsupported).

## Calibration results (2026-07-06)

Ran every rung's commands against the live CLI (window
`2026-06-09T08:13:38Z .. 10:27:56Z`). Pinned values:

| rung | confirmed expected value(s) | status |
|---|---|---|
| 1 fleet shape | `service_count == 28`; subsystems = `[analytics, cart, catalog, checkout, edge, fulfillment, identity, notifications, orders, payments, storefront]` (11) | ✓ as-written |
| 2 change lookup | `version == 2.4.1`, `ts == 09:03:47`; bonus `touches_db == false`, `change == "checkout copy + button styling"` | ✓ as-written |
| 3 log needle | `service == payments-db`, `pool_max == 40` | ✓ as-written |
| 4 knowledge | snippet id `kn:kn-payments-db-runbook-pool` ("sizing rationale (pool_max=40)") | ✓ as-written |
| 5 top anomaly | node `payments-db` / `error_rate` (z 704.7) | ⚠ prompt reworded — say "service"; edge row (z 906.6) is the distractor |
| 6 onset order | `[payments-db, payments-api, checkout-api, web-gateway]` (09:09:11 / 09:12:14 / 09:14:20 / 09:17:12) | ⚠ retooled — use `search anomalies --metric error_rate`, not `timeline` |
| 7 trace forensics | `payments-db` / `waiting` / wait≈2620ms ≫ exec≈4ms | ✓ values pinned; ⚠ add full-32-char-id hint |
| 8 chain | `payments-db → payments-api → checkout-api → [storefront-bff] → web-gateway` | ✓ blast-radius confirms hops; storefront-bff optional |

**Two CLI behaviors the runner/prompts must account for:**

1. **`--metric` (and facet filters generally) only bind on `search <scope>`, not
   on the bare correlate verbs.** `anomalies --metric error_rate` and
   `timeline --metric error_rate` silently return the *unfiltered* view, ordered
   by first movement on any metric (throughput — the demand surge, everything
   ~09:06). Metric-scoped questions (rung 6) must route through
   `search anomalies --metric`. *(Flag for Joseph: is the correlate verbs
   ignoring `--metric` intended, or a bug? If it should filter, rung 6 can point
   at `timeline` as originally planned.)*
2. **Trace ids are truncated to 8 chars in the `traces` table but `trace <id>`
   demands the full 32-char id** (from `traces --json`). Without a hint the model
   hits `unknown trace 'ed8a58e1'` and can stall. Put the hint in the shared
   orientation preamble.

**Rung 9/10 discriminators — confirmed live (grader/rubric fuel):**
- **Fleet-wide throughput surge**, incl. services with no payments dependency:
  catalog-db, search-api, orders-db, inventory-api edges all show
  `throughput_rps` z ≈ 13–21. Demand-driven, not deploy-driven.
- **Surge predates the deploy.** payments-api `throughput_rps` first shifts
  **08:59:48**, before the **09:03:47** deploy (and fraud-check moves 08:56).
  A deploy can't cause a surge already underway — strong exonerator, new since
  the plan draft; add to rung 9 rubric (a sixth discriminator).
- **Per-request exec time stays flat/tiny** (`db.exec_ms ≈ 3–4ms`) while
  `db.pool_wait_ms` explodes to ~2600ms — saturation, not slow queries or a bad
  release.
- **Change is cosmetic:** `touches_db=False`, "checkout copy + button styling".
- The sale/promo tie-break stays out-of-band (no telemetry field) by design.

## Harness matrix

First matrix (decided): **Sonnet + one codex model + qwen-coder (floor)** — a
minimal smoke to shake out the runner and calibration (~90 runs). GLM-Air was the
floor until the judge became GLM 5.2 (Cloudflare); the floor swapped to qwen-coder to
keep the judge's family out of the worker pool. Haiku/Opus (and Fable as a
ceiling, if wanted) join after the ladder is frozen, so their numbers land on a
stable set.

| harness | invocation | notes |
|---|---|---|
| claude -p | `claude -p "<prompt>" --model sonnet` from the sandbox cwd | needs Bash auto-approved for `weaver` + reads denied outside the sandbox (pin flags during build: `--allowedTools` / a sandbox-dir settings file). No repo CLAUDE.md in scope. |
| codex exec | `codex exec -m <model> "<prompt>"` from the sandbox cwd | pin sandbox/approval flags so it can run `weaver` unattended but not read outside |
| nb (floor) | `nb --trust "<prompt>"` with the stock system.md, `LocalLlm` provider → **qwen-coder** on imp:8080 (`swap-model qcoder`) | base system prompt by design — the floor differs in model, not information. Was GLM-Air; swapped so the GLM-5.2 judge is off-distribution from every worker. |

Every leg is off-box except the qwen-coder floor: claude and codex are cloud, the
GLM-5.2 judge is Cloudflare. So imp only ever holds qwen-coder — no model-swap
dance, and the judge can run concurrently with the workers. Judging is still a
logical **phase 2** (grade the stored transcripts + JSON answers once runs land),
just not forced by any hardware contention.

nb specifics:
- **nb has NO usable sandbox — the nb leg runs inside a `bwrap` read-jail
  (built + validated 2026-07-06).** Investigated nb's shell tool (`~/repos/nb`,
  `Shell/BashTool.cs`, `ConversationManager.cs`): under `--trust` the `Run`
  command category is unconditionally auto-approved, and everything except
  `cat/head/tail/less/more` is `Run` — so `grep`/`find`/`awk`/`sed`/`python -c`
  read any file the user can. There's even a no-`--trust` hole
  (`echo $(cat /etc/passwd)` via the safe-prefix list). The C# path check is a
  string heuristic, not a boundary; there is no seccomp/namespace/chroot anywhere.
  Rather than patch nb, `run.sh` wraps the whole nb process in `bwrap`:
  `--ro-bind / /` for the system, `--tmpfs /home` to **mask the repo + answer key
  + other runs' leavings**, re-exposing only nb's bin dir (`~/repos/nb/bin/Debug/
  net10.0`), `~/.dotnet`, and the published weaver binary dir (see below); the
  sandbox cwd is the only writable path, `HOME` is set to it, `/usr/lib/dotnet`
  stays visible so nb runs. Verified: inside the jail
  the weaver repo is absent, the answer key is unreadable, the `echo $(cat …)`
  trick returns empty, and `nb --help` exits 0. `run.sh` **refuses the nb leg**
  unless `bwrap` exists AND a self-test confirms `$EVAL_DIR` is masked. (Reported
  the nb exfil holes upstream: `~/repos/nb/bugs/shell-tool-no-filesystem-sandbox.md`.)
  Conversation history lands in the (throwaway) sandbox cwd, so each run is fresh;
  the per-cwd trust scoping is just where nb writes state, not a read boundary.
- **weaver must be a published binary for the nb leg (fixed 2026-07-06).** On the
  host `weaver` is `dotnet run --project <repo>/src/Weaver.Cli`, i.e. it lives
  *inside* the repo the jail hides — and the repo is the answer sheet
  (`project/plans/agent-evals.md` lists every expected value). First nb run failed:
  `bash weaver` → exit 127, model flailed. Fix: publish a self-contained single-file
  weaver OUTSIDE the repo (`dotnet publish src/Weaver.Cli -c Release -r linux-x64
  --self-contained -p:PublishSingleFile=true -o ~/.local/lib/weaver-eval`), expose
  only that dir in the jail and put it on the jail `PATH`. Repo stays fully masked;
  weaver (an API client) reaches the running API over the shared net. **Rebuild
  this binary when the CLI changes — it's a snapshot.** The self-test now also
  asserts weaver *runs* inside the jail, not just that the repo is masked.
- **nb renders the model's ```` ```json ```` fence as its own `── json ──…───`
  divider box** in captured output, so the literal fences don't survive. The
  extractor (`lib/extract-last-json.sh`) gained a fence-agnostic fallback: strip
  ANSI, scan for the last brace-balanced `{…}` (string/escape aware). Handles nb's
  rendering, raw JSON, and (likely) codex too.
- Under `--trust`, `bash weaver …` **auto-approves cleanly** (no prompt) once weaver
  is reachable. The earlier `ReadKey`/"console redirected" approval error was
  collateral: with weaver missing, the model flailed into nb's `Write` tool, which
  *does* require approval, and that prompt broke under redirected stdin. Not a
  blocker for these tasks (they only need `bash weaver`).
- **Result: nb leg works end-to-end.** qwen-coder *solved* rung 7 unaided —
  discovered the truncated-trace-id gotcha (`trace ed8a58e1` → exit 1), recovered
  via `traces --json`, read span attributes, answered `payments-db / waiting /
  2720ms wait vs 5ms exec` (correct). Encouraging for the floor.
- **Aside — live API keys in nb's build dir:** `~/repos/nb/bin/Debug/net10.0/
  appsettings.json` carries plaintext Azure/Anthropic/OpenAI/Gemini keys; it's
  exposed (ro) inside the jail. Not an eval-integrity issue (no answer content),
  but worth scrubbing out of a build dir regardless.
- qwen-coder needs the box loaded: `ssh imp '~/.local/bin/swap-model qcoder'`
  (blocks until healthy; qwen-class coexists with small models but give it the
  box for clean numbers). Runner should check `swap-model status` and refuse to
  start the nb leg otherwise. Nothing else touches imp — the judge is on
  Cloudflare, so no swap is needed for grading.
- Watch `MaxToolCalls` (default 25) — probably fine for rungs 1–7, may cap
  rung 9–10 foraging; raise for eval runs if it binds.

Expected gradient (the hypothesis this set tests): the qwen-coder floor passes
~1–3 (maybe further on the mechanical rungs, given it's a coding model — recheck
the hypothesis now that the floor changed from GLM-Air), Haiku-class ~1–6,
Sonnet-class ~1–8, Opus/Fable 1–9 with rung 10 the coin flip. If everything
passes everything, the ladder is too easy and we steepen Tier C/D; if the floor
scores 0, rung 1 is too hard and we simplify it.

## Layout & runner

```
evals/
  orientation.md         # shared preamble, prepended to every rung (built ✓)
  run.sh                 # runner: matrix × rungs × N, emits results.jsonl + table
  judge.md               # fixed judge system prompt (PASS/FAIL + reason)
  report.md              # findings write-up, updated per matrix run
  rungs/
    01-fleet-shape/
      prompt.md          # task + answer schema (built ✓ for all 10)
      grade.sh           # deterministic grader (jq) — or rubric.md for judged rungs
    ...
    10-bait-resistance/
      prompt.md
      check.jq           # deterministic field checks
      rubric.md          # judge checklist
```

The **orientation preamble is one file** (`orientation.md`), not copied into each
prompt — the runner composes `orientation.md + rungs/NN/prompt.md` into the final
prompt, so "same preamble verbatim across all harnesses" holds by construction. It
carries only neutral orientation (what weaver is, run bare `weaver` for help, API's
up, work from tool output only) plus the two calibrated usability hints
(truncated-ids→`--json`; single trailing ```json block, last one wins). No repo
CLAUDE.md, no dataset facts. Schema placeholders are deliberately non-anchoring —
answer-bearing enums show `<option-a or option-b>` and answer-bearing booleans show
`null`, so a model can't score by echoing the template (esp. rung 10's
`deploy_is_root_cause`).

- **Everything is committed** (decided): runner, prompts, graders, rubrics,
  and a simple `report.md` of findings — so others can replicate and expand.
  The graders do restate the incident's answer, and that's accepted; the
  isolation that matters is at runtime (the sandbox), not in the repo. The
  gitignored `ground-truth-flash-sale.md` stays private as the authoritative
  key; rubrics are derived from it, not linked to it.
- Runner preconditions: `weaver overview` succeeds (API up — ask Joseph, don't
  start it), and for the nb leg, qwen-coder loaded (`swap-model status`). Each run
  gets a fresh `/tmp/<guid>` sandbox dir, created and torn down by the runner.
- Result row: `{rung, harness, model, run, valid_answer, pass, wall_secs,
  answer, transcript_path}`. Keep full transcripts — failures on rungs 8–10
  are the interesting artifact.
- Judge: **GLM 5.2 frontier on Cloudflare, via minrouter `cf`**
  (`POST /x/cf/compat/chat/completions`, model `workers-ai/@cf/zai-org/glm-5.2`,
  `Bearer $MINROUTER_KEY`, send `max_tokens`) with `judge.md` as system prompt,
  fed the rubric + the candidate's JSON + (for 8–10) its full transcript.
  Off-distribution from every worker; never sees the ground-truth doc — the rubric
  is self-contained. Runs as phase 2 (over stored answers) but off-box, so it
  never contends with the qwen-coder floor. Bump the `cf` daily cap before a full
  matrix run.

## Build order

1. ~~**Calibrate the rungs by hand.**~~ **Done 2026-07-06** — see *Calibration
   results* above. All 10 rungs run against the live CLI; expected values pinned;
   rungs 5/6/7 adjusted (node-vs-edge wording, `search --metric` retool,
   full-trace-id hint); two CLI behaviors flagged for the runner. Default window
   suffices — no rung needs `--split`.
2. ~~**Write the 10 prompts + schemas.**~~ **Done 2026-07-06** — `orientation.md`
   + `rungs/01..10/prompt.md`, each a task + JSON schema. Preamble shared as one
   file (composed by the runner); placeholders non-anchoring.
3. **Runner + deterministic graders.** ~~Smoke the whole matrix with a single
   model (sonnet) at N=1.~~ **Built + validated 2026-07-06.** `evals/run.sh`
   (compose orientation+prompt → sandboxed harness → extract last JSON block →
   grade → `runs/results-*.jsonl` + summary), `evals/lib/extract-last-json.sh`
   (tolerant, last-valid-block-wins), and all 10 `grade.sh` (6 deterministic,
   4 gate-then-judge with exit-2 = needs-judge). Graders unit-tested incl. the
   jq `//`-drops-`false`/`0` trap (fixed in rungs 7 & 10). **claude leg proven:**
   `--allowedTools 'Bash(weaver:*)'` runs weaver unattended *and* denies Read +
   non-weaver Bash → answer key and CLAUDE.md unreadable at runtime. Live smoke
   (sonnet, N=1) rungs 1/3/6/7 all pass — incl. rung 6 scoping past the timeline
   trap and rung 7 recovering the full trace id. **nb floor leg proven
   end-to-end** (bwrap jail + published weaver + fence-agnostic extractor):
   qwen-coder passed rungs 1 (79s) and 7 (115s) through the runner — it solved the
   trace-id gotcha unaided. **codex leg now proven too** (gpt-5.5, bwrap-jailed —
   see the checkpoint for the two stub fixes): rungs 1/7/10 pass at N=1. **Pending:**
   smoke rungs 2/4/5/8/9 across the legs (falls out of the full matrix); global
   `~/.claude/CLAUDE.md` still loads into the claude leg (generic dev guidance, not
   weaver — decide whether to suppress for a cleaner floor).
4. ~~**Judge + rubrics** for 4, 8, 9, 10 (GLM-5.2 judge).~~ **Built + validated
   2026-07-06.** `evals/judge.md` (strict per-criterion scorer, JSON out),
   `rungs/{04,08,09,10}/rubric.md` + `pass.jq` (judge emits booleans, `pass.jq`
   applies the threshold deterministically), `evals/lib/judge.sh` (calls GLM-5.2
   on Cloudflare via minrouter, extracts criteria, applies the threshold).
   Thresholds unit-tested incl. missing-key defensiveness; a live GLM-5.2 call
   scored a strong rung-10 answer pass (4/4) and a weak one fail (0/4) with
   accurate notes. **Phase-2 driver built + validated:** `evals/judge-pass.sh`
   drives `judge.sh` over the `needs-judge` rows of a results file and writes
   `results-<stamp>.judged.jsonl` (kept a separate step from `run.sh` by design —
   off-box, cf-capped, re-runnable). **Pending:** the by-hand spot-check of ~10 real
   judged-rung transcripts (needs step-5 runs first).
5. **Full matrix at N=3.** Read the gradient; steepen or flatten rungs as
   needed; then freeze set 1 so later runs are comparable.

## Decisions (2026-07-06)

- **First matrix: minimal smoke.** Sonnet + one codex model + qwen-coder floor
  (GLM-Air retired from the pool when the judge became GLM-5.2).
  Haiku/Opus/Fable join only after the ladder is frozen.
- **Judge is off-distribution: GLM 5.2 on Cloudflare** (minrouter `cf`,
  `workers-ai/@cf/zai-org/glm-5.2`). Off-box, so no imp contention with the floor;
  bump the `cf` daily cap before a full matrix.
- **Everything committed, isolation at runtime.** Evals, graders, rubrics,
  and `report.md` all land in the public tree; runs execute in a bare
  `/tmp/<guid>` sandbox with reads outside it forbidden. Not worried about
  benchmaxxing/memorization — this isn't that kind of benchmark.
- **Rung 10 keeps the bait.** It's the one rung testing the project's
  epistemic line; expected flakiness there is signal. The
  recommendations-decoy stays in reserve for miscalibration swaps.
- **Eval runner is thin bash, not a framework.** Roll our own `evals/run.sh`
  in the nb idiom (bash loop + jq deterministic graders + `claude -p` judge
  against `judge.md`) rather than inspect-ai/promptfoo. Rationale: the one-shot
  shape is a plain `prompt in → text out` loop (~90 runs); the genuinely hard
  part — the per-harness sandbox invocation (`claude --allowedTools`/settings,
  `codex` sandbox flags, `nb --trust`+cwd) — is custom wrapper bash either way,
  so a framework would mostly add a viewer + an install step against the
  everything-committed/max-replicable goal. Zero new deps beyond jq + claude
  (both present); matches the nb `evals/` precedent.
- **codex leg: CLI available.** Build the runner assuming
  `codex exec -m <model>` is available; pin its sandbox/approval flags during
  calibration. (`claude` ✓, `nb` ✓ at `~/repos/nb/bin/Debug/net10.0/nb`, and
  `codex` ✓ all present as of 2026-07-06.)
