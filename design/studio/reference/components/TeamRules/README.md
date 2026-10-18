# TeamRules

TeamRules is a term and its value, with an optional command underneath: the rules a run of a team follows, as `loadout team show` prints them. It is also used for a remedy's record.

## Rows for a team

- **Posture**: manual, supervised or autonomous, and what that means in a sentence.
- **Budget**: money a run (or "no cap, on purpose"), turns a node, wall clock; with where the figure comes from (the run, then the team file, then the machine's `team-budget`).
- **Stops when**: the lead saying done, the budget, two rounds without progress, or you. These are hard-coded; a team file's `stop_when` is printed and read by nothing, so say the real four.
- **Gates**, **When nobody answers** (`take_recommendation_after`, `on_timeout`), **Done when** (the team's own) and **Held as well** (the project's own).

## Accessibility

- A description list. Below 520px each value sits under its term.
