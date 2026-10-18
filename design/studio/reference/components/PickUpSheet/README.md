# PickUpSheet

"Pick it up where it stopped" opens this in a Dialog. It asks for exactly what the run ran out of, because `team resume` refuses without it:

| It ended on | Asked for | Option |
|---|---|---|
| its budget | what it may spend in all from now on (more than it has spent), or none | `--usd` |
| its round limit | how many more rounds | `--rounds` |
| anything else | nothing; both are optional | |

An optional message tells the lead what to do next or what changed (`--message`). The exact `team resume` line is shown and updates as you type. **Pick it up** stays disabled, saying why, until what it needs is there.

Today Studio's pick-up sends no budget, rounds or message, and the daemon's `pickup` verb takes no rounds: both change (see `ROUND4.md`).
