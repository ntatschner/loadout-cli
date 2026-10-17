# Bin

Bin is where removed runs and teams wait before they go for good: `team runs remove`, `team runs prune` and `team remove` all move things here rather than deleting them.

## What it shows

- Each entry: run or team, its name, what it was, when it was removed, how many days it has left (Needs text at three or fewer) and how much disk it holds.
- Put it back: `$ loadout team runs restore <run>` or `$ loadout team restore <team>`. Neither restores over something already there.
- **Empty it for good**: the one action here with no way back. A danger Dialog lists everything it will delete, and focus starts on Cancel. `$ loadout team bin empty --yes`.
- **Clear out runs**, folded: an ending, an age and how many of the newest to keep, said back as a sentence ("Moves the failed runs older than 30d to the bin, never the newest 20, and never one still going or one that left a branch nothing merged."), with Say which first (`--dry-run`) and Clear them out, which asks before it goes.

## Do and don't

- Do say that a run's branches were never touched, so they are not in the bin.
- Don't decide in the browser which runs a prune takes; the command decides, and refuses a live run.
