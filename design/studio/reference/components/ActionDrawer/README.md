# ActionDrawer

ActionDrawer is how a page offers a `loadout` command that is not its main action. It sits closed, one line: the title in words, `loadout <path>` under it, and its effect. Opened, it is the command's form, the exact line it will run (updated as you type), and its button. It replaces the open-ended "Do" lists: a page shows its main actions in place and keeps the rest in drawers, grouped.

## Effects

| Effect | Tag | Button |
|---|---|---|
| `reads` | reads only | the label, with "reads only" under it |
| `changes` | changes something (accent) | the label, solid, with "changes something"; a **Preview** first when the command has `--dry-run` |
| `changes` with `changesOnlyWith` | changes something | reads until one of those options is ticked (`backup restore` without `--apply` is a read) |
| `session` | starts a session (Needs) | **Open it in a terminal**, "opens a terminal here, hosted by the daemon" |
| `terminal` | terminal only, with a lock | none: a TerminalOnly block with why |

The effect comes from Studio's coverage list (`StudioCoverage`), never from the page.

## Fields

- An argument is a bare name (`id`, `topic`); an option starts with `--`. Arguments are written in field order, then options, quoted the way a shell needs.
- `fixed` fields show what the page already knows (the project) and cannot be changed.
- `joined` writes `--answer=<prose>` for text that may start with a dash.
- A `required` field that is empty disables the button and says what is missing.

## Watch-only

Pass `disabledReason` ("Studio is only watching: type the command in a terminal"). Reads and Preview stay available.

## Accessibility

The top is a button with `aria-expanded`; the body is a labelled group. The line is `aria-live="polite"`, so a screen reader hears it change.
