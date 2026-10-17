# NoteDump

NoteDump is one batch of notes pasted from elsewhere (`loadout idea dump add`), after an agent has split it into pieces. Each piece has its number, a title, the words it came from, and its kind (idea or task). Tick the pieces to record; recorded ones are ticked, greyed and say so.

| Action | Runs |
|---|---|
| **Record** | `loadout idea dump apply <id>`, with `--only 1,3` when not every piece is ticked |
| **Split it again** | `loadout idea dump split <id>` |
| **Read it whole** | `loadout idea dump show <id>`, which reads only |
