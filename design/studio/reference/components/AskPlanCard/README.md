# AskPlanCard

AskPlanCard is one plan from Ask Loadout: every step with why its task was picked, the team and the cap, and Approve. Nothing starts until Approve is pressed, and Approve stays held while a team or a needed cap is missing. It sits in an AskPane answer. Ported from main's `studio/src/lib/components/AskPlanCard.svelte` as built.

## What it shows

- **Plan: nothing starts until you approve it**, then each step ("Queue t-14, Cache the leaderboard query") with its reasons, and "Left out: t-19 is blocked".
- **Team**, a list of the team choices, when the plan queues for a team and names none.
- **Spending cap, US dollars, for the whole plan**, when the plan is capped. It starts from team-budget, or "none" when team-budget says so, or empty with "Required: team-budget is not set".
- **Approve** with the command it runs:
  - starting a team: `$ loadout team queue run-now reviewers --task starstats/t-14 --task starstats/t-21 --usd 25`;
  - queuing only: `$ loadout team queue add reviewers <task> --project <project>, for each`;
  - terminals: "opens 2 terminal(s), each with its task".
- Held, it says why in place of the command: "No task qualified, so there is nothing to start.", "Choose a team first.", "Give a spending cap first. team-budget is not set, so there is no default.", "The cap is a figure in US dollars above zero, or none."; then "Starting…" and "Approved.".
- What happened ("Started reviewers on 2 task(s), sharing a cap of $25.00. The Jobs strip shows how it goes."), or why not, in Needs.

## What you provide

- `plan` as `loadout ask --json` writes it (what `--json` leaves out is filled in).
- `approve(plan, team, cap)`: carry it out and resolve to what happened, or reject with why not. Main runs `team queue run-now` as one job, or `team queue add` for each task, or opens the terminals one by one.

## Do and don't

- Do keep the cap required whenever the plan carries one and team-budget gave none. There is no silent default.
- Do show the exact command before Approve, with the team and cap as typed.
- Don't let Approve start anything while it is held: it says why instead.

## Accessibility

- A `group` named by its heading. The team list and the cap box are labelled fields.
- Held, Approve is `aria-disabled` and the reason is written under it, never only greyed.
- What happened is `role="status"`; a refusal is `role="alert"`. Once approved, the fields are disabled.
