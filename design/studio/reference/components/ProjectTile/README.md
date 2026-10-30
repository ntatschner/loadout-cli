# ProjectTile

The Projects page is a wall of tiles, one per project, instead of a list beside a page. A tile says what matters at a glance: name (and "pinned"), slug and path, its branch and whether it has uncommitted changes, its default agent, and counts: tasks doing, tasks blocked, findings to put right, ideas.

- A project that is not on this machine is drawn hollow and says "not on this machine".
- The picked project has an accent top edge and `aria-current="page"`.
- `place` makes a hollow "+" tile for the ways to add one: add a repository, make a new project, find repositories.
- Counts in Needs carry their word ("1 blocked", "2 to put right"); healthy says "✓ healthy".
- `waiting`: how many things wait on you there (sessions, runs, approvals, ideas with questions), from the one count; it comes first among the counts, as "2 waiting on you" in the needs style.
