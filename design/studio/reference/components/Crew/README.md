# Crew

Crew is "Who is on it": one row per node of a run, with its state, what it is doing now, its turns and cost, how many tool calls it was refused, and, when it has gone quiet, for how long against its own usual turn.

## What you provide

- `nodes`: node name (`implementer/1`), role, model when it was recorded, state word, what it is doing, turns, cost, refused, and `quiet` in words ("9 min, three times its usual 3").
- `onPick`: pressing a row opens that node's stream (What it said). The picked row has the accent edge.

## Do and don't

- Do measure quiet against the node's own average turn, with a two-minute floor, and never on its first turn.
- Do say refusals in Bad text with the word "refused"; many of them across runs is a team file's problem, which RoleRecord shows.

## Accessibility

- A list; each row is a button with `aria-pressed` when pickable. The row's text reads in order: name, role, state, doing, numbers.
