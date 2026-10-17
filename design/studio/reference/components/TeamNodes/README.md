# TeamNodes

TeamNodes draws a team's nodes the way RunShape draws a run: the lead at the top of the accent line, and the nodes it briefs branching from it, each with its role, mode, how many may run at once, whether each gets its own worktree, and what it hands back. A node that briefs others (a department lead in `product-company`) has its own branches.

## What you provide

- `nodes` from `loadout team show`: the lead first, then the rest, each with `delegates`.

## Accessibility

- Nested lists, each named "Briefed by lead". The dots are decoration.
