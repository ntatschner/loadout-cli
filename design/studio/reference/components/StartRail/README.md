# StartRail

StartRail is where a session starts: an accent tab asking what it should do, a wide input, Deploy, and the command that will run, updated as you type.

## What you provide

- `command(task)`: builds the full command from what is typed, including the project and mode (`$ loadout starstats --task "…" --mode investigate`).
- `onDeploy(task)`: called on Deploy or Enter with a non-empty task.
- Optional `label` (default "What should it do?"), `placeholder`, `action` (default "Deploy"), `value`.

## Do and don't

- Do put the project, mode and specialist pickers next to the rail, and fold their choices into `command`.
- Don't deploy an empty task; the rail ignores it.
- Don't hide the command while the person types. It is how they learn the CLI.

## Accessibility

- The tab is the input's `<label>`.
- Focus shows as a white (`focus`) bar on the left plus an accent ring around the field.
- The rail is `control-lg` (60px) tall, and the input is 20px text.
