# TaskBoard

TaskBoard shows a project's tasks as three columns, **Open, Doing, Blocked**, with done and dropped folded away under "3 done, 1 dropped". It reads `loadout task list --project <slug> --all`: without `--all` the list leaves done and dropped out. Each card has the title, its id, kind, who declared it and when, and its note (in Bad on a blocked one).

Each card offers only the moves that state allows, each running `loadout task declare <id> <state> --project <slug>`:

| From | Moves |
|---|---|
| open | Mark it doing, It is blocked, Drop it |
| doing | Mark it done, It is blocked, Reopen it |
| blocked | Mark it doing, Drop it |
| done, dropped | Reopen it |

- The command under each move shows when the move is hovered or focused, so the cards stay calm.
- Given `project` and `onStart(task, withOptions)`, a card that is not dropped starts a session on its task first: **Start** (solid; `$ loadout launch <project> --task-id <id>`) and **Start with options…** (the launch sheet with the task chosen).
- **History**, or **Details and history** when the task has a full description (`hasDetail`), opens the task in full (`$ loadout task show <id> --project <slug>`): through `onRead(task)` (such as the page's address), or else in a TaskDetail dialog read with `read`. Without either it is not offered.
- A card marked `missing` (open or blocked work without a done-when) or `proposalWaiting` (one is proposed for you to confirm) says so in a Needs tag: "missing", "proposal waiting".
- A task moved here from another list says "Moved here from the workspace-wide list, 3 Oct". What moved away (`away`) is never one of the board's tasks: it is listed apart under **Moved away**, "Fix the importer moved to starstats, 4 Oct. Show it", with nothing to press but the way to where it is now.
- `mixed: true` for a board of more than one list's tasks: each card names its list first, and its moves, start and History are on that list (`--project <slug>` or `--global`). `paged: true` when the tasks are one page of a longer list: an empty column says "None on this page."
- Titles and notes are kept to two lines; History has them in full.
- With `onSession` and `onRun`, an open, doing or blocked task also offers **Start a session on it** (the launch sheet with the task picked: `--task-id`) and **Run a team on it** (Start a run with the task picked: `--task`, which moves it to doing, then done or blocked). Not on ideas.
- Each column heading carries its state's Signal and count.
- Every column heading has an id, so each column is a section named by it.
- When the board itself is narrower than 640px (a phone, a panel beside the terminals, a Pure Focus view) the columns stack.
