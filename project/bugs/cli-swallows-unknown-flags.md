# CLI silently swallows unrecognized flags (incl. `--help` and typos)

Captured 2026-07-06 while calibrating the agent-eval ladder
(`project/plans/agent-evals.md`) against the live CLI.

**Symptom.** Any `--flag` a verb's handler doesn't explicitly read is accepted
and silently ignored — the command runs as if the flag weren't there, with no
error and no notice that the flag was dropped. Observed:

- `weaver anomalies --metric error_rate` → prints the full unfiltered anomaly
  set (all metrics). The user reasonably believes they filtered to error_rate;
  they didn't. This is the dangerous case — silent-ignore looks like a result.
- `weaver anomalies --help` → runs `anomalies`; no help text.
- `weaver anomalies --bogusflag xyz` → runs clean, no error.
- A typo like `--metirc` behaves identically to the correct spelling — no signal.

**Root cause.** `src/Weaver.Cli/Program.cs`:

- `ArgParser` (L945–960) parses `--tokens` permissively: `BoolFlags` (L939) is
  just `["json", "raw"]`; anything else is stuffed into `opts` (if a value
  follows) or `flags`, and **never validated against a per-verb allowlist**. So
  an unknown or misspelled flag is stored and forgotten.
- Each handler reads only the specific opts it cares about — e.g.
  `AnalysisQuery` (L895–902) reads `split`, `z`, `min-pct` and nothing else — so
  any other flag on `anomalies`/`timeline` is a no-op.

**Proposed fix.** Keep `ArgParser` syntactic and add one semantic validation
step immediately after parsing, before the `switch (verb)` dispatch and before
any API call:

```csharp
var argv = new ArgParser(args);
var verb = argv.Verb;
ValidateArgs(argv);
```

The validator should reject every parsed flag/option name that is not allowed
for the selected verb/subcommand, printing to stderr and exiting 2:

```text
weaver: unknown option '--splti' for anomalies. did you mean '--split'?
```

When there is no good suggestion, print the allowed options instead:

```text
weaver: unknown option '--bogusflag' for anomalies. allowed: --json --min-pct --split --z
```

Implementation shape:

- Expose the parser's consumed option names, e.g. `IEnumerable<string> Names =>
  opts.Keys.Concat(flags)`, or split `OptionNames` / `FlagNames` if the fixing
  pass wants stricter value-vs-boolean checks later.
- Add a small allowlist table keyed by verb. Do not make `limit` or `raw`
  globally valid just because the help groups them under "common flags"; allow
  them only where the handler actually reads them. `json` is the only broadly
  valid flag today.
- For verbs with subcommands, validate against `(verb, subcommand)` where the
  subcommand comes from `argv.Pos[0]`:
  - `board new`: `json` is not meaningful; no flags needed.
  - `board show` / default: `json`, `board`.
  - `board delete` / `rm`: `board`.
  - `doc show`: `json`, `board`.
  - `doc changes` / `diff`: `board`, `peek`.
  - `doc edit`: `board`, `find`, `replace`.
  - `doc append`: `board`, `text`.
- Initial top-level allowlist, matching current `Program.cs` reads:
  - `overview`: `json`
  - `service`: `json`
  - `metrics`: `json`, `raw`, `limit`, `kind`, `metric`, `from`, `to`
  - `logs`: `json`, `limit`, `level`, `grep`
  - `traces`: `json`, `limit`, `route`, `status`, `min-ms`
  - `trace`: `json`
  - `snippet`: `json`
  - `changes`: `json`, `from`, `to`, `target`
  - `blast-radius`: `json`
  - `anomalies`: `json`, `split`, `z`, `min-pct`
  - `timeline`: `json`, `split`, `z`, `min-pct`
  - `search`: `json`, `limit`, `grep`, `q`, `subsystem`, `kind`, `team`,
    `level`, `template`, `route`, `status`, `metric`, `service`, `source`,
    `min-ms`, `from`, `to`, `split`, `z`, `min-pct`
  - `facets`: `json`
  - `relationships` / `rel`: `json`
  - `evidence`: `json`, `limit`, `from`, `to`
  - `pin`: `board`, `ref`, `as`, `kind`, `aspect`, `evidence`, `note`, `at`,
    `label`
  - `chart`: `json`, `board`, `sql`, `title`, `type`, `x`, `y`, `pin`
  - `unpin`: `board`, `all`

Double-check the `raw` and `limit` entries during the fixing pass. Some current
verbs accept them accidentally but do not use them; the stricter fix should treat
those as invalid too, unless the handler is updated to make the option real.

Handle help as part of the same pass:

- Teach `ArgParser` to recognize `-h` as `help`, and either parse `--help` as a
  boolean flag or special-case it before value consumption.
- If `help` is present, print usage and exit 0 before validation rejects it. A
  single global `Help()` call is acceptable for the first fix; per-verb help can
  follow later.
- `weaver --help`, `weaver anomalies --help`, and `weaver anomalies -h` should
  never call the API.

Suggestion details:

- A Levenshtein/edit-distance helper over the selected command's allowed names
  is enough. Only print "did you mean" for distance <= 2 or for obvious
  transpositions; otherwise print the allowed set.
- Do not suggest options that belong to another verb unless the team explicitly
  wants cross-verb hints. For this bug, `weaver anomalies --metric` should error
  with the allowed anomaly flags, not imply `--metric` is valid there.

Regression tests to add with the fix:

- Prefer factoring `ArgParser` + `ValidateArgs` into a tiny testable internal
  type (or make internals visible to a new `Weaver.Cli.Tests` project), so most
  cases do not need a running API.
- Cover:
  - `anomalies --bogusflag xyz` exits 2 and mentions `--bogusflag`.
  - `anomalies --metirc error_rate` exits 2 and suggests `--metric` only if
    `--metric` becomes valid for anomalies; otherwise it must say `--metirc` is
    unknown and show allowed anomaly flags.
  - `anomalies --help` exits 0 and does not call `/api/anomalies`.
  - `logs --grep error --level warn` still validates.
  - `doc changes --peek` and `unpin ev_123 --all` still validate as boolean
    flags.

**Scope note.** This is the *validation* bug — flags should not vanish silently.
Whether `anomalies`/`timeline` should additionally **support** `--metric`
(filter the correlate view to one signal) is a separate design call, parked in
`TODO.md` under "Deferred / blocked" ("Correlate verbs honour `--metric`"). The
two are related but independent: fixing this bug makes a metric-scoped query on a
correlate verb *error out* (recoverable) instead of returning a misleading full
dump; the design item decides whether it should instead *filter*. Today `--metric`
works only via `search <scope>` (`SearchQuery`, L459).

**Why it surfaced.** In the eval, a model that correctly reasons "scope to
error_rate" and runs `anomalies --metric error_rate` gets misleading output with
no signal its flag was dropped — it can't tell filtering silently failed. An
error would let it recover. Eval rung 6 currently routes around this via
`search anomalies --metric error_rate`, so the ladder is unblocked regardless.
