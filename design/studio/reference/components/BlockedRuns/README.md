# BlockedRuns

BlockedRuns lists the runs that ended short of their done-when, each with what Loadout suggests and why, and every other action open to it. Nothing is done until a person confirms: the suggestion is picked first, the person can pick another, and the Confirm button shows the command it runs. A raise is filled in with the same again, beside what the run used, and can be changed before confirming; a refusal against a rule's cap says by how much and where the rule's page is. Main shows it on the Teams page under Blocked, from `loadout team blocked list`. Ported from main's `studio/src/lib/components/BlockedRuns.svelte` as built.

## What it shows

For each run, in a bordered box:

- **Its id, team, project and task**, and how it ended (blocked, failed, limited, unrecorded, dead, handover).
- **The goal**, then a calm line: how it ended, how far it got ("4 of 6 round(s) · $5.00 of $5.00 · 0 merged, 1 left on branches"), the rule that started it, and "run 3 in a row on this task".
- **What the lead last said**: "Blocked on: …", and each question it asked.
- **The suggestion and why**: "Suggested: Raise the limit and carry on." For a run that handed over, "It handed over what only you can do." with a link to tick them off on its page.
- **What to do**: every action open to it as radio buttons, the suggestion marked.
- **The fields the choice needs**: the raise figure (rounds or dollars, with what it used); for Edit the task, a new title, detail and done-when.
- **A note**: "What to tell the next lead" for carry on and raise, "A note, kept on the run" otherwise.
- **Confirm**, with `$ loadout team blocked resolve <run> --action <action> …`, updated as you type. Until the choice is complete it says what is missing instead ("say how much it may spend").
- **What the command said**, "✓ …", or why it refused.

With no runs: "No runs are waiting on you: every run that ended has been dealt with or got there."

## What you provide

- `runs`: `loadout team blocked list --json`, as main's `BlockedRead`.
- `onresolve(run, choice)`: run `team blocked resolve` with the choice and resolve to what it said, or reject with why it refused. Reread the list after.
- `disabledReason` when Studio is only watching.
- `set({ runs })` on a reread: what was being typed is kept by run id.

## Do and don't

- Do leave the suggestion picked: it is where most people should start, and it says why.
- Do show the refusal as the command gave it, with the rule's page.
- Don't resolve anything without the person pressing Confirm.
- Don't offer an action the run does not list: Confirm refuses it ("that is not open to this run").

## Accessibility

- A list named "Runs that ended short of their done-when"; each choice is a fieldset with the legend "What to do".
- Every field has a label; the figure field uses a decimal keyboard.
- Confirm is never silently disabled: the reason is written where its command would be.
- What the command said is a `role="status"` line; a refusal says "Refused" in its words.
