# Gate

Gate is a question a team run stopped to ask, answered where it is shown: on the Teams page, on the run's page, on a RunCard and in NeedsYou. A run that has stopped to ask is **waiting for you**, its own state and not a kind of running.

## The kinds, and their answers

| Kind | What it asks | Answers | Think again |
|---|---|---|---|
| `question` | The lead's own question, with its options and its recommendation | Each option, the recommended one solid | Yes: `--think-again` |
| `readings` | Before any worker starts: how the lead reads each done-when | Accept these readings (recommended) | Yes: nobody is briefed this round, the lead restates them |
| `brief` | Manual runs only: a worker's brief, in a box you can change | Send it (`--answer yes`, plus `--instead "…"` when you changed it), Don't send it | No |
| `proposal` | No done-when given: what the lead proposes the run is judged on, one per line, with the project's own shown and held | Hold it to these (`--answer yes`, plus `--instead` when changed). Emptying the box is a real answer | No |
| `confirm` | The run asks you to confirm a step (the daemon's kind `confirm`): a push in a supervised release, a merge | Yes, No | No |
| `permission` | A node wants to do something its role does not allow | Yes, No | No |
| `outward`, `merge` | Something that leaves this machine, or a merge | Yes, No. These carry no recommendation and a timer never answers them | No |
| `budget` | It has spent its budget: "Give it more" | Raise it to $38, Raise it to $50, Take the cap off, or a figure of your own | No |

- Every answer is `$ loadout team gate <run> --gate <id> --answer "<answer>" --by dashboard`, the same line the daemon runs. An optional reason adds `--reason "…"` and is kept in the journal.
- The options are shown exactly as the run wrote them; they are matched against your answer.
- **Think again** is only for the lead's own questions. On a permission it would read as a yes to something nobody answered.

## The timer

When a run takes the lead's recommendation after a wait (`--take-recommendation-after`), the gate says when and what will happen, in words, and counts down: "12 minutes left, then it goes back to the lead to think again". It never times a permission, a merge, an outward action or a manual brief.

## Do and don't

- Do show the lead's recommendation in words above the answers, as well as making it the solid one.
- Do keep the gate in place once answered: it turns to Good, says what you chose, and leaves the run's page to move on.
- Don't offer a brief for changing in a supervised or autonomous run; that would be manual under another name.
- Don't let a typed figure under a budget question be anything but a figure or `none`.

## Accessibility

- The gate is a group named by its question. The reply after answering is a live status.
- The answers are numbered in their rings (1, 2), so the order can be said aloud.
- Needs is carried by the edge, the word "waiting for you" on the run, and the question's own text, never by the amber alone.
