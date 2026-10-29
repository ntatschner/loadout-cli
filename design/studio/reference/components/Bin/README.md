# Bin

Bin is where removed runs and teams wait before they go for good: `team runs remove`, `team runs prune` and `team remove` all move things here rather than deleting them.

## What it shows

- Each entry: run or team, its name, what it was, when it was removed, how many days it has left (Needs text at three or fewer) and how much disk it holds.
- Put it back: `$ loadout team runs restore <run>` or `$ loadout team restore <team>`. Neither restores over something already there.
- **Empty it for good**: the one action here with no way back. A danger Dialog lists everything it will delete, and focus starts on Cancel. `$ loadout team bin empty --yes`.
- **Clear out runs**, folded: an ending, an age and how many of the newest to keep, said back as a sentence ("Moves the failed runs older than 30d to the bin, never the newest 20, and never one still going or one that left a branch nothing merged."), with Say which first (`--dry-run`) and Clear them out (`--yes`), which asks before it goes. The endings are main's: any ending, failed, stopped, abandoned, limited, blocked, done, needs-decision, unrecorded and stalled.

## What you provide

- `entries`, `keptDays` (30), `disabledReason` (Studio only watching).
- `onRestore(entry)` and `onEmpty()`: each may answer `false` (or resolve to it) where it was not done, and nothing changes, as main.
- `onPrune(command, dry, prune)`: as main's `onprune(prune, dry)`. Given a second parameter it is asked for the dry run as well, and what it answers, what would move and why it keeps the rest, is said ("✓ Would move 3 runs…"); the real one says its answer, or "Cleared out. They are in the bin." A one-argument `onPrune(command)` is only called for the real one, as before.

## Do and don't

- Do say that a run's branches were never touched, so they are not in the bin.
- Don't decide in the browser which runs a prune takes; the command decides, and refuses a live run.
