# weaver — design principles (the why behind the surface)

Not a build-plan and not a brand doc — the **design rationale** that everything
visible answers to: the house style (`TODO.md` → house style), the graph redesign
(`graph-redesign.md`), the panel layout (`sensemaking-pivot.md`), the cross-surface
parity (`cli-co-researcher.md`). It starts from *who the user is and how they
think*, derives principles from the constraints that are actually true, and leaves
the look-and-feel as the downstream expression. Brand identity is *answerable to
this*, not the other way round.

## The user and the work

- **An expert.** A responder, often mid-incident. Knows the domain; doesn't need
  hand-holding, onboarding, or a forgiving novice path. Will *learn* a dense
  surface and expect it to stay put.
- **Building a case.** They are gathering and curating evidence to assemble — and
  rule out — explanations. The output is a reviewable argument, not a verdict
  (`analysis-architecture.md`).
- **Working iteratively.** Troubleshooting is laps, not a line: forage → pin →
  relate → doubt → re-forage. The design must make each lap cheap and never lose
  the user's place between laps.
- **On a desktop, at a full monitor.** Not mobile, not a glance-and-go. The
  problem space — a microservice architecture under fault — is mentally
  comprehensive enough to *warrant the whole screen*.
- **Collaborating with Claude.** Half the hands on the board are the agent's. The
  surface is a shared workspace, addressable so both point at the same thing.

## The cognitive model: Pirolli & Card, extended to the pixels

The layout already encodes Pirolli & Card's sensemaking loop — **forage** (left:
search → filter → read → a shoebox of maybe-relevant findings) and **sensemake**
(right: arrange findings into a schema / case — the red string), with **pinning as
the bridge** (`sensemaking-pivot.md`). This doc extends the same theory *down to
the visual layer*: their core lever is the **cost structure** of information work —
the design's job is to lower the cost of each operation in the loop (locate, read,
recognize, relate, recall, revisit). Every principle below is a cost reduction on
that loop, not a matter of taste.

## Constraints → principles

**1. Density is warranted — spend the whole monitor.**
Expert user + finite screen + not mobile + a problem that fills the mind ⇒ screen
real estate is the *budget*, and the problem earns it. No responsive collapse, no
hiding-to-look-clean, no chrome that buys breathing room at the cost of evidence.
We design for one large viewport and fill it with the case, not with whitespace.
Density is a feature here; the bar is **dense, not cluttered** — earned by
hierarchy (see #2/#4), not by subtraction.

**2. Encode for the eye — visual search beats reading and recall.**
The eye scans faster than it reads, and recognition is cheaper than recall. So
*meaning must live in stable visual properties* — colour, icon, shape, position —
that a glance decodes without reading a label or remembering a rule. This is
Pirolli's **information scent**: visual cues that predict where the value is, so
the forager spends attention well. It is also the cognitive mandate for the
**semantic-token system** (`TODO.md` → house style): tokens aren't branding, they
are the encoding that makes the eye fast. One colour = one meaning is a
*performance* requirement, not an aesthetic one.

**3. One finding, one identity — object constancy across every surface.**
A single piece of evidence appears in many places: a search-result card, a node on
the board, an evidence card in the drawer, a line in the CLI's `board show`. The
user must recognise *"that's the same anomaly"* across all of them, instantly,
through an iterative session. So each finding carries **one identity everywhere** —
same kind-colour, same icon, same typed id, same one-line summary — and the same
server-side renderer drives every surface (`cli-co-researcher.md`: one renderer,
one vocabulary). **This is what "visual consistency" means here: keeping the user
from losing the thread, not brand uniformity.** Brand consistency is a happy
side-effect; evidence-tracking is the point.

**4. Honest over clean — no hidden state.**
Expert + iterative + a surface used as external memory ⇒ you cannot track a case
through state you can't see. The house rule: **persistent or quiet, never hidden;
hover may *emphasise*, never *reveal*.** Three kinds of "reveal" are not the same —
*disclosure* (content that doesn't exist until summoned — banned), *relocation*
(info in a stable, always-visible home — fine), *feedback* (already-visible
content highlighted on hover — encouraged). This is the interaction-layer twin of
the no-verdict rule: the tool shows its workings and trusts the expert with the
density. Recede ambient detail by hierarchy, don't conceal it.

**5. Cheap laps — the work is iterative.**
Troubleshooting revisits. So every operation in the loop is cheap and reversible:
pinning and unpinning, re-foraging, re-linking, ruling out. Prefer the reversible
move (pin-and-explain over withhold). The board is a **present-tense model**, not a
history log — prune freely, keep it the current best picture
(`weaver-board-is-model-not-history`). And state *persists across laps*: the user
never loses their place when they go back to forage again.

**6. The surface is external memory, shared with Claude.**
The board and drawer hold the user's working memory so their scarce attention goes
to judgment, not bookkeeping (`agent-role.md`). The *current state of thinking*
must be legible at a glance — what's pinned, what's linked, what's been ruled out.
And because the agent shares the workspace, everything is addressable by **id /
URL**, never by screen position (`weaver-ui-rules`): the spatial layout is the
human's; the ids are the shared language.

## Where brand identity sits

Downstream. The "honest instrument" aesthetic — dense, dark, rationed colour,
typographically led, reference class of Bloomberg / glass cockpit / pro IDE — is
the *expression* of these principles, not a parallel exercise. When a visual
choice and a brand instinct disagree, the principle wins: visual consistency is
**functional first** (cross-surface evidence tracking, #3), expressive second.

## Settled / open

| Principle | State |
|---|---|
| Density warranted — full desktop viewport, no responsive collapse | ✅ settled |
| Visual encoding for fast scan (semantic tokens = performance) | ✅ settled |
| One finding, one identity across all surfaces (one renderer) | ✅ settled — extends the shipped parity work |
| Honest over clean / no hidden state (hover emphasises, never reveals) | ✅ settled |
| Cheap, reversible laps; board as present-tense model | ✅ settled |
| Surface as shared external memory, id/URL-addressed | ✅ settled |
| The visual identity that *expresses* the above | ⬜ open — house-style work (`TODO.md`) |

The line, restated for the design layer: the surface helps the expert **track
their own thinking and collaborate with Claude** — it lowers the cost of every lap
of the sensemaking loop, and it never hides what the case is made of.
