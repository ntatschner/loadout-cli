# Handover

Handover is what a run handed to you because only you could do it: a push, a CI run or a check by hand. Each is ticked off here once it is done, which the journal keeps as review evidence in your name. The last tick judges the run again, with no turn for the lead. Main shows it near the top of a run's page once the run has stopped. Ported from main's `studio/src/lib/components/Handover.svelte` as built.

## What it shows

- **How many are still yours**: "handed over · 2 of 3 still yours", or "all checked".
- **The question line**: "It handed these over: only you can do them."
- **Each item**: the criterion with a chip for what it needs (a push, a CI run, your check), what to do, and either "✓ checked by nigel" or **Done: tick it off**.
- **Where the work is**: "The work is on run-0412/implementer-1."

Each button runs `$ loadout team handover <run> --check <n>`; on the last one the line adds "· the last one, so it is judged again".

## What you provide

- `run`, `items` (`criterion`, `needs`, `todo`, `checkedBy`) and `branches`, from the run's read.
- `oncheck(number)`, returning a promise. While it is out, the other buttons say "ticking one off" and do nothing.
- `disabledReason` when Studio is only watching.
- `set(patch)` with the items reread, `checkedBy` filled in.

## Do and don't

- Do show it only on a run that has stopped and has items.
- Do reread the run after a tick: the last one starts a judgement the daemon does not wait out.
- Don't tick on someone's behalf: the tick is evidence in the name of whoever pressed it.

## Accessibility

- A `role="group"` named by its question line.
- A disabled button says why in place of its command; it is never greyed out silently.
- Focus stays on a tick button across a redraw, or moves to the next one when the one pressed is done.
