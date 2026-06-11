# weaver — the coding-agent workflow (the opinion)

weaver's answer to the brief's graded question: *how do you enable
developers to solve incident triage when LLMs and coding agents are
critical parts of the workflow?* This is an opinion, stated as one.

## The problem, reframed

The bottleneck in a several-hundred-service outage is not data — it's
**cognition over a huge, correlated evidence space under time pressure.**
No human holds that dependency graph or cross-correlates metrics, traces,
and logs in their head at 3am. That is precisely an LLM's strength —
*if* you give it the right surface.

## The opinion, in one line

**Don't ask the model for the answer; give it a toolkit and let it
investigate.** weaver exposes observability as a composable,
agent-drivable toolkit — facts and computations, never verdicts — so an
agent runs the investigation loop a senior engineer would, and ends by
pinning a *reviewable hypothesis* a human adjudicates.

## Four claims

1. **The agent navigates; it does not ingest.** The wrong design dumps
   the system into a context window (blows up at hundreds of services) or
   precomputes the answer and has the model parrot it (a black box).
   weaver exposes *scoped* tools — this service's metrics in this window,
   anomalies vs. baseline, the blast radius of node X — so the agent
   pulls only what a hypothesis needs. It traverses like a debugger, not
   a data dump. That is what makes it scale.

2. **Cheap tool opinions, expensive agent reasoning.** `anomalies` /
   `timeline` / `blast-radius` do the mechanical correlation a human
   can't (change-points across 300 services) but stop short of "the
   cause." Causal judgment — separating an originating error from a
   downstream timeout — stays with the reasoner. That division of labor
   *is* the architecture.

3. **One surface, human or agent.** It's a REST API with a CLI over it.
   An agent drives the verbs; a human drives the same verbs or watches.
   Everything the agent concludes is reproducible and inspectable — trust
   comes from it *showing the evidence trail*, then pinning a curated View
   the human reviews.

4. **The output is a reviewable hypothesis, not an autonomous action.**
   "Likeliest candidate" + narrative + supporting subgraph. The developer
   still decides. The agent compresses 40 minutes of dashboard-hopping
   into a defensible two-minute hypothesis; it does not replace the page.

## The interface bet: a CLI over bash, not an MCP server

We demo on **Claude Code**, but the surface is deliberately model- and
agent-agnostic: **anything with a bash tool can drive it.**

- **A CLI is a portable contract.** Model tuning shapes how a model
  formats its *native* tool calls, but it doesn't get between a model and
  a plain CLI invoked through bash. Bash is the universal substrate every
  coding agent already has — in practice, CLIs are opinionated without
  being the kind of thing model tuning messes with.
- **Contrast with MCP / bespoke tool schemas.** Those couple you to
  per-agent integration and per-model tool-calling quirks. A CLI sidesteps
  all of it — and yields the same surface a human can run, paste, and
  script.
- **Inspectable and reproducible by construction.** Every step the agent
  took is a command you can re-run. The evidence trail is literally the
  shell history.

## The investigation loop

1. **Start at the user-facing symptom** — the outage is defined by
   end-user experience (a trace's root `status`, a spiking edge error
   rate). Work downstream from where it's felt.
2. **Hypothesize** a candidate region or service.
3. **Query a scoped slice** — selectors over metrics/traces/logs/topology;
   pull only what the hypothesis needs.
4. **Read the evidence.** The tools correlate (anomalies, timeline onset,
   blast radius) but never conclude.
5. **Narrow or re-hypothesize.** Repeat 2–4 until one candidate explains
   the most observed symptom with the earliest onset.
6. **Conclude** — rank the likeliest candidate, write the narrative.
7. **Build the wall** — `pin` the supporting findings (by typed id) and `link`
   them into a red string on the shared board, then hand over its URL. Clickable
   from the console, pastable to Slack, storable in a ticket; the human watches
   it assemble live and adjudicates (and can `crossout` a string that doesn't
   hold). See `sensemaking-pivot.md` / `cli-co-researcher.md`.

## How this shows up in the demo

Live-Claude drives the weaver CLI against a planted mystery, narrating
each step, and finishes by handing over a URL to a curated view. The
medium is the message: *this is how an LLM should do incident response —
watch it work.*
