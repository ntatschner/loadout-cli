# Usage, telemetry and the status line

Three related things: what the agents have already spent, what a running agent
reports about itself, and what is on screen while you work.

## What the agents have spent

```bash
loadout usage                        # last 30 days, by project
loadout usage --days 7 --by day      # by day instead
loadout usage --by model             # or by model, or by agent
loadout usage --project starstats    # one project only
```

Nothing has to be switched on first. The figures are read from the transcripts
the agents already write, so the history is there the moment you install the
launcher — including for sessions that ran before it.

`--by` takes `project`, `day`, `model` or `agent`. `--days` counts back from
today and defaults to 30. `--json` gives the same figures to a script.

### Cached input is not counted as fresh input

Reading from a prompt cache costs a fraction of sending the same tokens again,
and writing to one costs more than sending them once. A total that ignores the
difference is not slightly wrong — over a long session it is wrong by most of
the number.

So the columns are kept apart. Input, cache writes, cache reads and output are
counted and reported separately, and a cost estimate applies each rate to the
column it belongs to rather than treating every token alike.

### It says when a total cannot be trusted

Every source of usage data has its own way of being incomplete: a transcript
still being written, a file rotated away, an agent that records running totals
rather than per-turn ones.

Where a total is partial the report says so, and says why, rather than
presenting a smaller number as a complete one. A figure you cannot trust is
worse than no figure, because it gets quoted.

## The telemetry receiver

Some agents can emit OpenTelemetry metrics about their own usage. Loadout can
receive them locally:

```bash
loadout telemetry serve      # listen; stops on Ctrl+C
loadout telemetry status     # what has been collected, including reported cost
```

This is optional and additive. `loadout usage` works without it. The receiver
is for agents that report figures no transcript contains, and for watching a
session as it runs rather than after it has finished.

**Nothing leaves the machine.** There is no cloud service, no account and no
endpoint to configure. It listens locally, stores locally, and records counts
only — never repository contents, prompts, conversations, file contents,
secrets or argument values.

## The agent's status line

Claude Code renders a status line by running a command and printing what it
writes. It hands that command the session's working directory, model and
context window counts — but it does not know which registered project the
repository is, and it does not report the branch.

```text
loadout | src/Loadout.Core | main* | Opus 5 | 42% ctx
```

`loadout statusline install` writes the entry. `--global` applies it to every
session on this machine including ones started by hand; naming a project
instead writes into the workspace, so it travels to every machine that clones
it and applies only when the launcher starts the session.

Each segment can be switched off with `loadout config set statusline-git false`
and the rest keep working. A missing piece removes its own segment rather than
the line, and an unreadable payload falls back to the working directory: an
empty status line is indistinguishable from a broken one.

`loadout statusline show` previews the line and reports where it is installed.
It says `needs repair` rather than `installed` when the recorded command is not
the one an install would write today — which happens when the launcher moves,
or when an older version wrote an entry a newer one would write differently. A
status line command that cannot run draws nothing and reports nothing, so this
check exists to make that state visible instead of silent.

Codex has no equivalent mechanism, so the status line is Claude Code only.

## See also

- [The context budget](context-budget.md) — what loads on every launch, and what it costs
- [Memory](memory.md) — keeping the always-loaded set small
