# TaskPicker

TaskPicker lists the recorded tasks something can be for: **doing first, then open, then blocked**, newest first within each, then **Something else**. Each shows its title, id, who declared it and when, and its state's Signal. Ideas, done and dropped tasks are left out.

Used in:

- **Start a run** (TeamSheet's `tasks` option): picking a task fills the goal from its title and note and adds `--task <id>`, so the run moves the task to doing, then done or blocked. Something else keeps the goal free, and the run records a task of its own, as today.
- Studio's session launch sheet already has the same list (`LaunchSheet.svelte`); give it this look so the two match.
