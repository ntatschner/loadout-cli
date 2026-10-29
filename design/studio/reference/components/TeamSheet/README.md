# TeamSheet

TeamSheet is the three sheets that start things: **Start a run**, **Make a team…** and **Run one on a schedule…**. Each is a Dialog of its own rather than another fold under the runs, and each writes its command below the form as you fill it in.

## Start a run

- Team and Project are lists of what exists on this machine (`team list`, the projects). Picking a team says what it is: nodes, lead, posture, budget, its own done-when, and anything that stops it running here. A template is offered as something to copy, never to run.
- The project is required here: Studio has no directory you are standing in.
- What the run is for; posture (manual, supervised, autonomous); budget (a figure or `none`; zero is refused); rounds at most.
- Done when, one per line, each becoming its own `--done-when`. Left empty it shows the team's own in the box, or says the lead will propose some.
- The project's own criteria as switches, on by default; untick one for `--drop-project-done-when "…"`. Each one left out is named, even all of them, as main's start route takes them.
- When nobody answers: the wait (`--take-recommendation-after 30m`) and what then (`--on-timeout`, think-again-once by default).
- Start the run asks once more, naming the team and the goal, before anything starts. Then the sheet stays open and says what happened: what `onSubmit` answers (or resolves to), or main's words, "✓ Started. Its page opens as soon as the run is listed." While it is going the button says "sending…"; a refusal is said with a ✕, in Bad text.
- **Run it again** on an ended run opens this sheet filled from it (`preset`): team, goal, project, rounds, posture. It starts nothing.

## Make a team

- A name (lowercase and hyphenated), what to start from (an empty file, a team, or a template), and whether only one project sees it: `$ loadout team new <name> --from <team> --for-this-project --project <slug>`. It writes the file and says where.

## Run one on a schedule

- A name, a team (no templates), a goal, a project, and one of three ways to say when: a time of day (`--at 09:00`), an interval (`--every 24h`, at least five minutes) or something to watch (`--on commit`).
- Supervised or autonomous only. Manual is not offered, and the sheet says why.
- It says whether the daemon is running, because nothing fires without it, and that a missed run is not made up for.

## The command

- Every word is quoted as a shell needs it, as main's `sheetCommand` does (an empty project shows as `""`).
- It cannot go until: a team (not a template, nothing stopping it), a goal and a project; a schedule is not manual; a budget is a figure or none, and not zero; rounds is a whole number; a wait reads 30m, 2h or 1d. Each refusal is main's words.

## Do and don't

- Do keep the primary button unavailable with its reason in words until the command would be accepted.
- Do put the command in a `.ls-cmdline` under the form; it is what runs.
- Don't send anything until the confirmation for a run.

## Accessibility

- The sheet is a modal Dialog: focus is trapped, Escape closes it, and focus returns to the button that opened it. Every field has a visible label.
