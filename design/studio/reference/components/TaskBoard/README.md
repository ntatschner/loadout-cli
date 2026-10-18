# TaskBoard

TaskBoard shows a project's tasks as three columns, **Open, Doing, Blocked**, with done and dropped folded away under "3 done, 1 dropped". It reads `loadout task list --project <slug> --all`: without `--all` the list leaves done and dropped out. Each card has the title, its id, kind, who declared it and when, and its note (in Bad on a blocked one).

Each card offers only the moves that state allows, each running `loadout task declare <id> <state> --project <slug>`:

| From | Moves |
|---|---|
| open | Start it, It is blocked, Drop it |
| doing | Mark it done, It is blocked, Reopen it |
| blocked | Start it, Drop it |
| done, dropped | Reopen it |

- The command under each move shows when the move is hovered or focused, so the cards stay calm.
- With `onSession` and `onRun`, an open, doing or blocked task also offers **Start a session on it** (the launch sheet with the task picked: `--task-id`) and **Run a team on it** (Start a run with the task picked: `--task`, which moves it to doing, then done or blocked). Not on ideas.
- Each column heading carries its state's Signal and count.
- When the board itself is narrower than 640px (a phone, a panel beside the terminals, a Pure Focus view) the columns stack.
