# ForgetProject

ForgetProject replaces "Stop knowing it" on a project. It opens in a Dialog titled "Forget <project>" from a **Forget project…** button (danger), and from the project's actions in Pure Focus.

## Forget it

| Choice | Runs | What happens |
|---|---|---|
| **On this machine only** (the default) | `loadout project remove <p>` | Loadout forgets where it is here. It stays in the workspace, listed as not on this machine; other machines keep it. |
| **From the Loadout workspace** | `loadout project remove <p> --from-workspace` | Every machine forgets it and it leaves the list. Drawn with a Bad edge. |

The repository and its code are never touched, either way. That is the command's own promise, and the dialog says it first.

## Also clean up

Each cleanup is a real command, shown before it runs. All of them are off until ticked:

| Cleanup | Runs | Offered |
|---|---|---|
| Take the Git protections out of its repository | `loadout protect <p> --remove` | when the project is on this machine |
| Its tasks | `loadout task remove <id> --project <p>`, once each | only from the workspace |
| Its ideas | `loadout idea remove <id> --project <p>`, once each | only from the workspace |
| Its memory topics | `loadout memory delete <topic> <p> --yes`, once each; a backup of each is taken first | only from the workspace |

A cleanup that is not offered stays in its place, unticked, with why: "Only when forgetting it from the workspace: on this machine only, the workspace keeps them for your other machines", "It has none", or "It is not on this machine".

**Kept whatever you tick:** `project remove --from-workspace` leaves the project's folder of instructions, rules and memory in the workspace, and says where. The dialog says so too, with the path and file count: "Loadout never deletes it; delete it yourself if you meant to lose it." No button deletes it, because no command does.

## Running it

- **What will run, in order** lists every exact line: cleanup first, while the project is still known, then `project remove`. It updates as you choose.
- **Preview** runs every line with `--dry-run` and changes nothing.
- **Forget it here** runs them. **Forget it on every machine** must be pressed twice within five seconds: the first press arms it and says what will happen, in Bad. The CLI asks this question itself in a terminal; Studio runs commands without prompts, where the flag counts as the confirmation, so the page asks instead.
- Runs stop at the first command that fails.
- `onRun` may return a promise. The pressed button says "forgetting…" (or "checking…" for Preview) until it settles; `false`, a throw or a rejection says "✕ why" (`failed` when there is no reason) and the page forgets nothing.
