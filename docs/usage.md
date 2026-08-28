# Usage, telemetry and the status line

Three things that all answer "what is this costing me": what's already been
spent, what a running agent says about itself, and what's on screen while you
work.

## What the agents have spent

```bash
loadout usage                        # last 30 days, by project
loadout usage --days 7 --by day      # by day instead
loadout usage --by model             # or by model, or by agent
loadout usage --project starstats    # just the one project
```

There's nothing to switch on. The numbers come out of the transcripts your
agents already write, so you have history from the moment you install Loadout,
including sessions that ran before it existed.

`--by` takes `project`, `day`, `model` or `agent`. `--days` counts back from
today, default 30. `--json` if something else needs to read it.

### Cache reads aren't fresh input

Reading from a prompt cache costs a fraction of sending the same tokens again.
Writing to one costs more than sending them once. Add those together as if a
token is a token and you don't get a slightly wrong total, you get a wrong one:
over a long session the cache reads *are* most of the number.

So the columns stay separate. Input, cache writes, cache reads and output are
counted apart and shown apart, and cost estimates apply each rate to the column
it belongs to.

### It'll tell you when a number is incomplete

Every source of usage data has its own way of being partial. A transcript
that's still being written. A file that got rotated away. An agent that records
running totals instead of per-turn ones.

When a total is missing something, the report says so and says why. Showing you
a smaller number and staying quiet about it would be worse, because you'd go
and quote it.

## The telemetry receiver

Some agents can emit OpenTelemetry metrics about their own usage. Loadout will
listen for them:

```bash
loadout telemetry serve      # listens, Ctrl+C to stop
loadout telemetry status     # what's been collected, including reported cost
```

This is optional. `loadout usage` works fine without it. The receiver is there
for agents that report figures no transcript contains, and for watching a
session while it runs instead of after.

Local means local. No service, no account, no endpoint to point it at. It
listens on this machine, stores on this machine, and records counts only, never
repository contents, prompts, conversations, file contents, secrets or argument
values.

## The status line

Claude Code draws its status line by running a command and printing whatever
comes back. It hands that command the working directory, the model and the
context window counts, but it has no idea which registered project you're in
and it doesn't tell you the branch.

```text
loadout | src/Loadout.Core | main* | Opus 5 | 42% ctx
```

`loadout statusline install` writes the entry. `--global` applies it to every
session on this machine, including ones you start by hand. Name a project
instead and it goes in the workspace, so it follows you to every machine that
clones it, but only applies when the launcher starts the session.

Turn any segment off with `loadout config set statusline-git false` and the
rest carry on. A missing piece drops its own segment rather than the whole
line, and an unreadable payload falls back to the working directory, because a
blank status line looks identical to a broken one.

`loadout statusline show` previews the line and says where it's installed. It
reports `needs repair` rather than `installed` when the recorded command isn't
the one an install would write today, which happens if the launcher moves or an
older version wrote something a newer one spells differently. A status line
command that can't run draws nothing and reports nothing, so this check exists
to make that visible.

Codex has no equivalent, so the status line is Claude Code only.

## See also

- [The context budget](context-budget.md) — what loads on every launch, and what it costs
- [Memory](memory.md) — keeping the always-loaded set small
