# weaver — the case log (deferred)

**Status: deferred.** Not built. This records a design decision and the shape of
the feature that should replace it later. The trigger for writing it: we pulled
the edge **cross-out** feature, because it was the wrong home for the right idea.

## The decision: the board is a model, not a history

The board is **present-tense** — it is the current best model of *what happened*:
this is the suspect, here is the evidence, here is the line of causation. A
corkboard at the *end* of a case shows the solution and the surviving string; the
dead strings have been taken down.

Cross-out tried to make that one spatial artifact also carry **temporal history** —
every lead considered and killed, struck through but kept. That doesn't scale.
Store every theory and dead end forever on the graph and you don't get a story,
you get a hairball: not *"here's what happened and the evidence,"* but *"here's a
bunch of stuff someone looked at around 3am."* The two jobs pull against each
other on the same surface.

So:

- **Board** = live model. Drawing a line asserts a hypothesis; **deleting** it
  retracts the assertion. There is no struck-through state. The wall stays clean.
- **Case log** = append-only history. *Ruling a lead out* is a log event, not a
  board mutation. The reasoning is recorded where it can be searched, instead of
  smeared across the graph as clutter someone has to notice.

The edge `crossedOut` column, the `crossOut` API endpoint, and the CLI
`crossout` verb are left **dormant** (not removed) pending this feature, which
subsumes them. The board + evidence UI no longer expose cross-out — delete only.

## What the case log is

An append-only, timestamped, **attributed** (human vs. claude) event stream over
the investigation. The board is a *projection* of the current live state; the log
is the full history that produced it.

Event kinds (sketch): `hypothesis-raised`, `evidence-pinned`,
`line-drawn`, `line-ruled-out` (+ reason), `node-added`, `node-pruned`,
`note-added`. Each carries who, when, and a one-line human-readable summary
(same server-side renderer idiom as evidence cards, so both surfaces read it the
same way — cf. `cli-co-researcher.md`).

## Why it's worth building: the shift-change handoff

This is the payoff that cross-out was reaching for and couldn't deliver. The
operator rolls off at 6am; the morning crew comes in fresh:

> **Morning crew:** "this looks like a payment-processor issue."
> **Claude:** "we investigated that at 04:23 — dead end. The deploy correlated
> on time but the telemetry couldn't separate it from the demand surge; ruled
> out out-of-band: v2.4.1 was copy/UI only, didn't touch the DB."

That is the co-researcher *carrying memory across a shift change* — a far
stronger beat than a struck-through line, because the dead end is now
**queryable** instead of being one strikethrough someone has to spot and
re-interpret. It also resolves the flash-sale dataset's engineered red herring
cleanly: the deploy line gets **deleted** from the board once ruled out, and the
**log** holds the why (see `thursday-dataset-contract.md`).

## The richer shape: a debrief skill

The log shouldn't depend on the operator dutifully narrating every action — mid-
incident, tired, they won't. The natural capture point is a **skill** that
debriefs them the way you'd talk to a human colleague on a conference call, e.g.
after a burst of board changes or on a `weaver handoff`:

- **"Why did you just make these changes?"** — capture the *intent* behind a
  burst of pins/links/deletes the log otherwise only sees as mechanical events.
- **"Anything you've learned?"** — capture out-of-band facts the telemetry will
  never hold (the flash sale, what v2.4.1 actually shipped, who owns the pool).
  This is exactly the human-only knowledge that resolves the case.

The skill turns the raw event stream into a narrated case history: mechanical
events + the operator's reasoning + the out-of-band facts. That narrated log is
what the morning crew (and Claude) query against. It's symmetric — Claude can
append to the log too ("I checked the payment processor traces, nothing
anomalous") so the handoff works in both directions.

## Open questions (for when this is picked up)

- Storage: a new `case_events` table vs. deriving part of the log from existing
  board mutations. Probably a real append-only table — intent and out-of-band
  notes have nowhere else to live.
- Does the log live per-board, or is it the board's own history tab?
- How does `line-ruled-out` reference the (now-deleted) edge — by endpoint pair +
  kind, since the edge id is gone? Likely the same fuzzy edge reference the CLI
  already speaks (endpoint pair).
- Skill trigger: explicit (`weaver handoff` / `weaver debrief`) vs. nudged after
  N board changes. Start explicit.
