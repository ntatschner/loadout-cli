# QueuePanel

QueuePanel is one team's queue of fully specified tasks: what waits, in order, and what would be refused now and why; what it has run; and what it is allowed. The draft pull request grant is said first when it is on, because it is the one thing here that reaches outside this machine, and it is turned off from the same line that says so. The Teams page shows one under **Queued for a team** for each queue that has anything in it. Ported from main's `studio/src/lib/components/QueuePanel.svelte` as built.

## What it shows

- **The team** and its limits: `$25 a window · 3 run(s) a window · at most 2 held`, with what it spent in this window or in the last day.
- **Its window**: `Worked through daily from 22:00 for 8h30m, autonomous · open now` with **No window** (`$ loadout team queue window <team> --off`), or *No window: its runs start only when you press Run the next one.* **Give it a window** / **Change the window** opens a form (opens at, stays open for) with **Save the window** (`$ loadout team queue window <team> --at 22:00 --for 8h`).
- **The pull request grant**: ready for review (**Stop that**, `--pr-mode none`, Danger), draft (**Stop that**, `--draft-prs off`, Danger) or none (**Let it open drafts**, `--draft-prs on`).
- What is running and how many are held, in Needs.
- **What waits**, numbered, with its project, what it waits for and `Would be refused: …`; **Up**, **Down** (`$ loadout team queue move <team> <task> <n>`, inactive and saying *already first* / *already last* at the ends) and **Take out** (`$ loadout team queue remove <team> <task>`).
- **Run the next one** (`run-next`), **What would run** (`plan`: the order, what is left out and why, and where it stops), **Add a task** and **Change its limits**.
- **Add a task**: project, task (ideas, done and dropped tasks are not offered; one with no done-when says so), after. A task with no done-when gets a field for its conditions, one a line, with **Propose them** (`$ loadout task propose-done-when <task> --project <slug>`) and **Save them** (`$ loadout task declare <task> <state> --project <slug> --done-when …`). **Add it**: `$ loadout team queue add <team> <task> --project <slug> [--after <task>]`.
- **Change its limits**: most a window may spend, most runs a window may start, runs that may wait at a gate. **Save them** sends only what changed: `$ loadout team queue settings <team> --window-usd 25`.
- **What it ran**: the last eight, newest first, with state, project, start, run id, cost, reason and a link to the run.

## What you provide

main's Props: `queue` (`team queue list <team> --json`), `projects`, `tasksOf(project)`, `propose(project, task)` (optional), `act(path, words, done)`, `readPlan()`, `disabledReason`, `now`. `act` returning `false` keeps the form open. It returns `{ el, set }`; `set({ queue })` after a new read keeps open forms and what is typed. `QueuePanel.whose(team)` gives `builders'` and `docs-crew's`.

## Do and don't

- Do say the pull request grant before anything else when it is on, with its way off on the same line.
- Do say why a task would be refused before anybody presses Run, and offer to write a missing done-when here rather than in a terminal.
- Don't send limits that did not change: saving one never rewrites another somebody set from a terminal.

## Accessibility

- The panel is a section labelled `builders' queue`; the window is a group, the grant a note, the waiting list, the runs list, the plan region and each form are labelled by the team.
- Up at the top and Down at the bottom are `aria-disabled` and say so in words.
- Every field has a visible label; focus stays where it was when the panel redraws.
- A disabled button says why in place of its command.
