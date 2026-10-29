# RunCard

RunCard is one team run in a list: its goal, its state, the team, project, posture and round, what it has spent and is projected to spend, and, when it is waiting for you, the question itself with its answers.

## What you provide

- `run`: id, goal, team, project, posture, the state word (`running`, `waiting for you`, `held after this round`, `done`, `failed`, `stopped`, `limited`), round, elapsed time, and the spend: `spent`, `cap` (or `uncapped: true` for no cap on purpose), `projected` and `rate`.
- `gate`: the question it is waiting on (see Gate). It is answered here.
- `reasons`: anything else that puts it in Needs you, each with what clears it.
- `badge`: what the daemon says of the process driving it, beside its word in the Bad colour and words (`.ls-liveness`, a `status`), as main's `livenessOf` words it: "not responding · pid 48213", "possibly not responding", "owner unreadable", "silent for an hour or more · pid 51022". Or give `liveness` (`not-responding`, `possibly-not-responding`, `unreadable`, `silent`) and `pid` and it is worded for you; `live` and `finished` say nothing, as main. `LoadoutStudio.livenessOf(liveness, pid)` gives the same words for a page's own header.
- `actions`: for a running run, Hold after this round (`team halt --pause`), Let it carry on (`--resume`) and Stop after this round (`team halt`); for one that ended, Run it again (fills Start a run, starts nothing), Pick it up where it stopped (`team resume`) and Forget it (`team runs remove`).

## The spend line

- "$6.12 of $25.00 · $0.15 a minute". The bar fills in Moving; a tick marks where it is projected to end.
- Projected past the budget, the line says so in words ("projected $26.30, past its budget") and the bar gains a Needs edge. Colour never carries it alone.
- No cap reads "no cap, on purpose", with no bar.
- The rate is said per hour once it drops below a penny a minute.

## Do and don't

- Do say "Stop after this round": stopping lands between rounds, and nothing is killed mid-turn.
- Don't offer Forget it on a run still going; the command refuses one anyway.

## Accessibility

- The card is an `article` named by its goal; the goal is the link to the run's page.
- The spend bar is a `meter` whose value text says the projection in words.
