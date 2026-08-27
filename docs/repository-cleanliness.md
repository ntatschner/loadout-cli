# Repository cleanliness

The launcher's central claim is that application repositories hold application
source and agent state lives elsewhere. Three commands make that verifiable
rather than aspirational:

```bash
loadout repo check          # what is tracked that should not be
loadout migrate --dry-run   # what would move, and where
loadout protect --global    # stop it happening again
```

`repo check` distinguishes three states, and the distinction is the substance
of it. A **tracked** agent file is a committed violation and exits 9. An
**untracked but visible** one is a single `git add .` away from becoming one,
so it warns. An **ignored** one is the system working, and is not reported.

`migrate` always shows its plan first and **never deletes a tracked file**.
Removing something Git is tracking rewrites the repository, which is a commit
the user should make and review themselves; the launcher copies it into the
workspace and tells them exactly what is still there. Untracked files are moved
outright, because nothing committed them and moving them is the only way the
repository actually becomes clean.

`protect` installs a pre-commit hook written as POSIX shell, which Git runs the
same way on all three platforms. It re-derives the check from Git rather than
calling back into `loadout`, so it keeps working on a machine where the
launcher has been moved. A hook the launcher did not write is never overwritten
or deleted. Hooks live in `.git/hooks` and so are per-clone — `loadout doctor`
reports when the clone you are standing in has none.

## Drift

`loadout doctor` answers whether this machine is set up, for wherever the shell
is standing. `loadout drift` answers a different question: across every
registered project, what has quietly stopped being true.

```text
storefront-api
  + Remote  https://github.com/example/storefront-api.git
  x Agent files  1 agent file(s) are committed to this repository
  ! Pre-commit protection  not installed in this clone (fixable)
  ! Memory  3 topic(s) recorded on this machine the workspace does not hold (fixable)
```

Hooks are per-clone and untracked, so a fresh clone of a protected repository
has no protection until somebody notices. Memory an agent recorded locally is
lost the day the machine is rebuilt. Neither shows up in a repository nobody has
opened this month, which is why this is a sweep rather than a check.

Findings marked `(fixable)` carry a remedy the launcher can carry out.
`--fix` previews each, asks once, applies, then **re-runs the checks** rather
than trusting that the fix worked. Every remedy is idempotent.

Three things are reported and deliberately never fixed automatically:
untracking committed files rewrites the repository, splitting an oversized
instruction layer is a judgement call, and a remote that disagrees with the
registry could be wrong on either side. A fix that has to guess what was meant
is not a fix, it is a second problem.

## Undo

Every operation that rewrites files takes a snapshot first — `migrate`,
`rules split` — and prints the command that reverses it:

```text
Migrated 4 item(s) into the workspace.
Undo it with: loadout backup restore 20260823-141502-a1b2
```

Each set records a SHA-256 per file. A restore verifies every digest before
writing anything, so a corrupted set fails before it can leave the tree half
restored, and it takes its own snapshot first so undoing an undo is possible.
Paths that did not exist at capture time are recorded as absent, which is what
lets a restore *remove* the files an operation created rather than leaving them
behind.

For structured files, the restore also reports which keys it would take away:

```text
Settings that would be lost (present now, absent in the backup):
  .claude/settings.json
    - toolSearch
```

That is the failure a file-level backup cannot otherwise see. Every digest
matches, the restore reports success, and a setting somebody turned on last week
is gone with nothing to show it existed. Key paths only, never values, because a
settings file can hold a credential.

## Conflict recovery

When the local workspace and the remote have both moved, the launcher refuses
to fast-forward. Before anything else touches the clone it labels the local
state:

```text
Conflict  Local and remote workspaces have diverged.
          Local work is preserved on branch 'recovery/DEV-PC/2026-08-22-2114'.
```

HEAD is never moved and nothing is merged or reset. Spec section 47 says no
data loss is acceptable, and a branch costs nothing.

