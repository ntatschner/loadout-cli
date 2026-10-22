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

### Time on the line

After the context figure and before spend, the line can say what the session
is doing and how long it has been at it. Claude Code's own screen shows you a
spinner, but not since when, and a session you left in another window gives no
sign whether it finished a minute ago or an hour ago.

The waiting segment reads the session's transcript and says one of these:

| What is happening | What the line says |
|---|---|
| Claude is mid-turn | `working since 14:02` |
| The turn ended and nothing is outstanding | `waiting for you since 14:02` |
| A tool call has no result yet | `on a tool since 14:02` |
| A tool call is waiting for your permission (Studio sessions only) | `permission since 14:02` |
| The turn ended with a wake-up scheduled | `wakes 14:30` |
| The wake-up time and its grace have both passed | `due 14:30` |
| The turn ended with background work still running | `background since 14:02` |

The rest are single readings:

| Segment | What the line says | Setting |
|---|---|---|
| Last write | `write 3m ago`, or `write 14:02` | `statusline-last-write` |
| Clock | `14:05`, or `2:05pm` | `statusline-clock` |
| Session | `session 1h12m`, or `started 09:10` | `statusline-session` |
| Last turn | `turn 2m14s` | `statusline-turn` |
| Last commit | `commit 3h ago`, or `commit 11:40` | `statusline-last-commit` |

Waiting and last write are on by default and the others are off, because those
are the questions you have when you come back to a session, and the rest are
there for whoever wants them. The waiting segment is `statusline-waiting`.
Relative times read `<1m ago`, `3m ago`, `2h05m ago` and `3d ago`; a time that
isn't today gets the day in front, as in `21 Oct 14:02`. Times are your local
time, 24-hour unless you set `statusline-time-24h false`, and
`statusline-time-absolute true` swaps every "ago" for the time itself. The last
turn is the length of the last completed one, so it can't go stale.

Waiting turns amber at 10m and red at 30m, and last write turns
amber at 30m, so a session that has been sitting on a question
stands out. Change the thresholds with `statusline-waiting-after 10m,30m` and
its siblings; a value that can't be read just means no colour change.

#### It needs a reinstall to keep ticking

Claude Code redraws the line on its own events, which stop when the session goes
quiet, and that is when "3m ago" matters. So `loadout statusline install` now
also asks Claude Code to redraw it every 30s, and says which it did: `ticks
every 30s`, `updates on redraw only` when your Claude Code isn't one known to
read the setting, or `no ticking (screen-reader profile)`. It writes the setting
only for Claude Code 2.1.308 and later, because that's the release it was
confirmed in, and how an older one treats a setting it doesn't know wasn't
checked; a settings file it refused would break the agent, not just the line.

An install from before this has no interval, so its times freeze while the
session is idle. `loadout statusline show` tells you so: `Reinstall with loadout
statusline install so relative times keep moving while the session is idle`.

#### What it can't tell you

- **A session in a plain terminal can't tell a permission prompt from a running
  tool.** Both look the same in the transcript: a tool call with no result. Only
  a session started from Studio gets `permission since`, because Studio sees the
  prompt itself. Elsewhere you'll see `on a tool since` for both.
- **It depends on Claude Code's transcript format.** The transcript is Claude
  Code's own file and isn't documented as stable. If a release changes its shape
  the waiting segment drops out rather than guessing, and the rest of the line
  carries on.
- **`due` waits out a two-minute grace.** A wake-up that fires a little late is
  normal, so the line keeps saying `wakes 14:30` until the grace has passed, and
  only then says `due 14:30`, in red.

#### With a screen reader

If your accessibility profile says nothing should redraw (`show-redraw never`,
which the screen-reader preset sets), the line holds still. Relative times
become absolute, the session reads `started 09:10`, the clock is left out, and
install writes no refresh interval, so nothing on the line changes unless
something has actually happened.

#### On a narrow terminal

When the line is longer than the terminal is wide, the time segments go first,
in the order clock, session, last turn, last commit, last write and waiting, so
spend and the rest are never pushed off. Claude Code doesn't hand the status
line the terminal width, so the line reads the `COLUMNS` environment variable,
and when that isn't set it doesn't trim anything. Whether Claude Code sets it
for the status line hasn't been checked, so on some terminals this guard does
nothing.

Codex has no equivalent, so the status line is Claude Code only.

## See also

- [The context budget](context-budget.md) — what loads on every launch, and what it costs
- [Memory](memory.md) — keeping the always-loaded set small
