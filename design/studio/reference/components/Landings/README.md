# Landings

Landings is the Teams page's **Waiting to land** list: the branches waiting to land. That is a queued run's own branch with its pull request, and each node branch its gate did not merge, pending. Each can be opened, marked ready, or squash-merged with a message the person writes, through the same commands as the terminal. Ported from main's `studio/src/lib/components/Landings.svelte` as built.

## What it shows

- One row per branch: its state as a chip (`pending`, `draft`, `ready`, `merged`, `abandoned`), the branch name, and "gone from the repository" in the Needs colour when it is.
- A line under it: the project, where it came from ("implementer-1 in run 20261028-0914-ab12", "run …" or "no run"), the task when there is one, and how long it has waited ("under an hour", "3 hours", "2 days").
- The pull request's address as a link, once it has one, and "Merges once its required checks pass." once a merge has been asked for.
- What can be done next, one step at a time:
  - **pending**: Open as a draft (`$ loadout pr open <branch> --draft`) and Open for review (`$ loadout pr open <branch>`);
  - **draft** with a pull request: Mark ready (`$ loadout pr ready <branch>`);
  - **ready** with a pull request and no merge asked for: Merge…, which opens the squash message: a Subject (one line, as git log will show it) and a Body (left empty, the commit has no body), and **Squash-merge when checks pass** (`$ loadout pr merge <branch> --subject …`). Until a subject is written it says "Write the squash commit’s subject" in place of its command. Not now closes it.
- With nothing waiting: "Nothing is waiting to land." The source line is always there: From `$ loadout pr list`.

## What you provide

- `landings`: what `loadout pr list --json` gives.
- `act(path, words, done)`: runs `loadout <path> <words…>` and refreshes; return `false` when it did not go through, and the merge message stays open. Leave it out and the list is read-only.
- `disabledReason`: when Studio is only watching, every button shows this instead of its command.
- `now`, for a fixed clock; `set(patch)` for a reread, which keeps a merge message half written.
- `Landings.mergeWords(one, subject, body)` gives the words for `loadout pr merge`, joined with `=` so a message that starts with a dash is still read as the value, and leaving out a body nobody wrote.

## Do and don't

- Do put it under a heading (main's is the zone heading "Waiting to land").
- Do offer only the next step for each state, as here.
- Don't merge without the person's own subject: the button stays disabled until there is one, and a subject is one line.

## Accessibility

A plain list. The merge message is a form labelled "Squash-merge <branch>", with labelled fields. Every button names what it does and shows its command, or why it cannot. "Gone from the repository" is words, not only colour. Links open in a new tab with `rel="noreferrer"`.
