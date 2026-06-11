# weaver — what the agent is for (and what it isn't)

How we expect a user to work with Claude (or Cursor, or whatever agent) inside
weaver. This is the positioning the whole product serves; the mechanics of
keeping the agent and the user aligned live in `cli-co-researcher.md`, and the
cross-shift memory that makes it durable lives in `case-log.md`.

## The agent is a partner in a supporting role

The agent is not the investigator of record. It is the most capable *support* a
responder has ever had on the bench. It shows up in two shapes:

**1. The active bench partner (synchronous, 1:1).** Someone you hand work to,
bounce ideas off, and ask technical questions of:

- *Delegate the legwork* — "pull every log like this one across the fleet," "diff
  what v2.4.1 actually shipped," "find the traces that touch payments-db after
  14:00." The tedious, parallelizable foraging.
- *Rubber-duck a hypothesis* — "I think the deploy did it — check me." The agent
  argues the evidence back, including the parts that don't fit.
- *Answer the technical question you'd otherwise Slack a teammate* — "what's the
  default pool size for this driver?" — without pulling that teammate out of bed.

**2. The tireless junior incident commander (sustained, multi-thread).** On a
long, sprawling incident the agent is the coordinator that never sleeps and never
loses the thread:

- holds the **state of every parallel thread** across a 36-hour incident —
  who's chasing what, what's been ruled out, what's still open;
- stays in the incident channel continuously, briefs each person who joins so
  nobody re-treads ground, and keeps the timeline straight while humans rotate
  in and out;
- carries the investigation across **shift changes** — the morning crew asks
  "did anyone look at the payment processor?" and gets a straight, sourced answer
  instead of scrollback archaeology (see `case-log.md`).

This is a *class of support that did not previously exist*. No human org could
staff a perfectly-attentive, perfect-recall coordinator who is also an on-demand
expert forager, awake for the entire incident. That's the new thing.

## The line we are deliberately holding

Right now — maybe not forever — the agent does **not**:

- **declare the root cause.** It assembles, argues, and rules out; the *judgment*
  call is the human's. weaver is a toolkit over a mystery, not an oracle
  (cf. the core-idea note). The decisive facts are often out-of-band — what the
  deploy *meant*, whether a sale was running — and those live with people.
- **ship the fix.** It can draft, explain, and prepare, but it does not open a PR
  and merge a production change on its own authority during an incident.

Why hold the line:

- **Accountability.** Someone with skin in the game owns the call to roll back,
  shed load, or page a team. An incident is not where you launder that ownership
  through automation.
- **Out-of-band truth.** The tie-break is frequently knowledge the telemetry
  never held. An agent that "concludes" from data alone will confidently pick the
  plausible coincidence over the real cause — exactly the trap the flash-sale
  dataset is built around (`thursday-dataset-contract.md`).
- **Trust is earned in support before it's granted in authority.** The value is
  already enormous at "superhuman support." Reaching for "autonomous resolver"
  before the support role is trusted spends credibility we haven't banked.

## The shape of a good interaction

The user drives; the agent multiplies. The user points at a suspicion, an
anomaly, a hunch ("the db thing," "that spike around 2:30"); the agent does the
foraging, lays the evidence on the board, argues both sides, and remembers
everything — so the human spends their scarce attention on **judgment**, not on
legwork or bookkeeping. The agent makes the human a better investigator; it does
not replace them as the investigator.

*Maybe someday the line moves. Not today, and the product is honest about that.*
