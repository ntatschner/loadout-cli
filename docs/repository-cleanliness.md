# Repository cleanliness

The launcher's central claim is that application repositories hold application
source, and agent state lives elsewhere. Three commands make that checkable
rather than aspirational:

```bash
loadout repo check          # what is tracked that should not be
loadout migrate --dry-run   # what would move, and where
loadout protect --global    # stop it happening again
```

`repo check` splits the answer three ways, and that split is most of the point.
A **tracked** agent file is a committed violation, so it exits 9. An
**untracked but visible** one is a single `git add .` from becoming one, so it
warns. An **ignored** one is the system working, and you won't hear about it.

`migrate` always shows its plan first, and **never deletes a tracked file**.
Removing something Git tracks rewrites the repository, and that's a commit you
should make and review yourself — so the launcher copies the file into the
workspace and tells you exactly what's still sitting there. Untracked files it
moves outright. Nothing committed them, and moving them is the only way the
repository actually gets clean.

`protect` installs a pre-commit hook written as POSIX shell, so Git runs it the
same way on all three platforms. It works the check out from Git rather than
calling back into `loadout`, which means it keeps working on a machine where
you've moved the launcher. A hook the launcher didn't write is never
overwritten or deleted. Hooks live in `.git/hooks`, so they're per-clone, and
`loadout doctor` tells you when the clone you're standing in has none.

## Drift

`loadout doctor` answers whether this machine is set up, wherever your shell
happens to be. `loadout drift` answers a different question: across every
project you've registered, what has quietly stopped being true.

```text
storefront-api
  + Remote  https://github.com/example/storefront-api.git
  x Agent files  1 agent file(s) are committed to this repository
  ! Pre-commit protection  not installed in this clone (fixable)
  ! Memory  3 topic(s) recorded on this machine the workspace does not hold (fixable)
```

Hooks are per-clone and untracked, so a fresh clone of a protected repository
has no protection until somebody notices. Memory an agent recorded locally dies
with the machine. Neither turns up in a repository nobody has opened this month,
which is why this is a sweep and not a check.

Anything marked `(fixable)` carries a remedy the launcher can apply. `--fix`
previews each one, asks once, applies, then **re-runs the checks** instead of
assuming the fix worked. Every remedy is idempotent.

Three things get reported and deliberately never fixed for you. Untracking
committed files rewrites the repository. Splitting an oversized instruction
layer is a judgement call. And a remote that disagrees with the registry could
be wrong on either side. A fix that has to guess what you meant isn't a fix,
it's a second problem.

## Undo

Every operation that rewrites files takes a snapshot first — `migrate`, `rules
split` — and prints the command that puts it back:

```text
Migrated 4 item(s) into the workspace.
Undo it with: loadout backup restore 20260823-141502-a1b2
```

Each set records a SHA-256 per file. A restore checks every digest before it
writes anything, so a corrupted set fails before it can leave your tree half
restored, and it snapshots first so you can undo the undo. Paths that didn't
exist when the snapshot was taken are recorded as absent, which is what lets a
restore *remove* the files an operation created instead of leaving them behind.

For structured files, the restore also says which keys you'd lose:

```text
Settings that would be lost (present now, absent in the backup):
  .claude/settings.json
    - toolSearch
```

That's the failure a file-level backup can't see. Every digest matches, the
restore reports success, and a setting you turned on last week is gone with
nothing to show it was ever there. Key paths only, never values, because a
settings file can hold a credential.

## Conflict recovery

When the local workspace and the remote have both moved, the launcher refuses to
fast-forward. Before anything else touches the clone, it labels the local state:

```text
Conflict  Local and remote workspaces have diverged.
          Local work is preserved on branch 'recovery/DEV-PC/2026-08-22-2114'.
```

HEAD never moves, and nothing is merged or reset. Spec section 47 says no data
loss is acceptable, and a branch costs nothing.
