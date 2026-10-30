# NeedsYou

NeedsYou is the top of the Teams page: every run that wants you, and why. Four reasons put a run here (the budget one in two forms: spent, or projected to overrun), and **each one clears itself**. Nothing is remembered and nothing is dismissed by hand: each reason is worked out again on every read, so one that has gone is gone.

The reasons come live work first (`liveFirst`): a question or an approval that waits on you, then what is on a run still going (the lead's quiet round, a quiet node, a projected overrun), then what is for you to pick up (a spent budget, a stall, a paused rule, held events, a stopped source); the oldest first within each, by `since` or `at`.

| Reason | Word | What it says | Clears when |
|---|---|---|---|
| `asked` | waiting for you | the question itself, as a Gate | you answer it |
| `silent` | needs a decision | The lead took a round without asking for anything. Another one ends the run. | it asks for a node, or finishes |
| `quiet` | quiet | a node has said nothing for three times its own usual turn (two minutes at least; never on its first turn) | it says anything at all |
| `budget` | projected over | it is projected to overrun its budget | it slows, finishes, or you stop it |
| `spent` | limited | it has spent its budget | you raise it, or it stops |

A trigger rule wants you for four more, as main's `NEED_REASONS` has them; give its rule as the goal, "trigger rule" (or "trigger source") as the team, an empty `run`, and its own `text`:

| Reason | Word | What it says | Clears when |
|---|---|---|---|
| `approval` | needs approval | A trigger rule wants to start a run that writes. | you approve or reject it |
| `paused` | paused | It reached one of its caps and starts nothing until you resume it. | you resume it |
| `stale` | held too long | It held events past the 24-hour window. | you release them, or they are dropped after a week |
| `source` | stopped | A trigger source has stopped. | what stopped it is put right |

A link with no `href` goes to `#teams`, as main.

## What you provide

- `items`: one per reason, with the run, its goal and team, a link to its page, and the details (the node and how long it has been quiet, the projection).
- `gate` on an `asked` item; `actions` on the rest (look at the node, change the budget, say something to the lead).

## Do and don't

- Do put the count in the zone heading and in the side rail's fact for Teams ("2 waiting on you"), and in the page title.
- Do tell someone once per reason; a reason that clears and comes back is news again.
- Don't offer Dismiss. If a reason is shown, it is still true.

## Accessibility

- A `section` labelled by its heading, which carries the count. Each item says its reason in words beside the Signal.
