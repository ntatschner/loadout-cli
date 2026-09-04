# What you get

The [README](../README.md) is the short version. This is each part of it, with
the commands.

### Instructions picked for the job

There are 73 specialists built into the binary: foundations, modes, languages,
frameworks, databases, platforms, clouds, functional areas and skills. Instead
of one enormous prompt that's mostly irrelevant, Loadout works out which ones
your task needs from the repo you're in and the words you used, then tells you
why it picked each one.

```console
$ loadout instructions explain "why is this postgres query so slow" --mode investigate

language
  + C#                     language.csharp        300 .cs files
framework
  + .NET                   framework.dotnet       Microsoft.Extensions. dependency declared
database
  + PostgreSQL             database.postgresql    task mentions "postgres"
function
  + Debugging              function.debugging     task mentions "why is"
  + Performance            function.performance   task mentions "slow"

Context
  Estimated instruction tokens: 2,403
  Budget: 12,000
  Usage: 20%

Where guidance overlaps
  C#: follow framework.dotnet over language.csharp (narrower scope composes last)
```

You can see the whole set, what it'll cost you in tokens, and where two
specialists disagree, before anything launches. If it picked something daft you
can rule it out with `--without`.

### Memory that doesn't grow forever

`loadout memory` keeps the durable facts about a project: decisions and why you
made them, constraints, the traps that keep catching people. The useful bit is
`memory compress`, which pulls facts out of always-loaded instruction files and
into the memory store. What every session pays for gets smaller; what it can
look up stays the same. `memory audit` goes looking for secrets, duplicates and
stale entries.

### Where the tokens went

```sh
loadout usage --days 7 --by day
```

No setup, no opt-in. It reads the transcripts your agents already write, so
there's history from the day you install it, including sessions from before.
Break it down by project, day, model or agent.

Cache reads are counted at their real rate rather than as fresh input, which
matters more than it sounds: on a long session the difference is most of the
number. If a total is incomplete it says so instead of quietly showing you a
smaller one.

There's also `loadout telemetry serve`, a local OTLP receiver for agents that
emit OpenTelemetry. Optional, and local means local: no service, no account,
nothing sent anywhere, counts only.

### Repos that stay clean

`loadout protect` sets up the Git protections. Because hooks live in
`.git/hooks` and never travel, a fresh clone is unprotected until somebody
notices — so a launch says so on the way in, while it can still be acted on. It
warns and never blocks: an unprotected repository is still one you may have good
reason to work in. A working tree keeps its hooks in the repository it was made
from, and there the launcher says nothing rather than reporting a problem you do
not have. `loadout migrate` moves whatever
is already scattered around into the workspace, showing you the changes first
and taking a snapshot you can restore. `loadout drift` tells you when a project
has wandered from what you configured.

### A launcher, not just a command line

![The launcher](images/launcher.svg)

Run `loadout` on its own. Every row tells you whether you can work on that
project, and the panel on the right shows what a session would start with, so
you know before you spend one.

`Ctrl+P` opens a palette over every command the CLI has, and it finds them by
what they're for. Search `undo` and you get `backup restore`. Search `broken`
and you get `doctor`. Nobody looking to undo a mistake searches for the words
"backup restore".

The launcher doesn't implement any command itself. Whatever you pick runs
through the same parser you'd have typed at, and a test checks that.

### The agent can ask back

The session you launch is told `loadout` is on PATH, and gets the same few
operations as MCP tools: read a specialist in full, ask what it was given and
why, search what the project already knows before working it out again, write
down one fact worth having next time, and change its own mode when the work
changes shape.

It isn't offered anything that changes your machine or pushes to a remote, and
the context says so rather than leaving it to be worked out. Ask an agent to
review a repository and it has a procedure to follow — `skill.repository-review`
— and somewhere to put what it finds.

### Undo

Every command that changes a file takes `--dry-run` and shows you the change
first. Anything that did change is in a snapshot you can put back:

```sh
loadout backup list
loadout backup restore 20260901-204044-fd3512
```

### Also in the box

Session listing and resume across agents, cross-agent handoff documents, MCP
servers managed per project, secrets in the OS credential store, context
profiles, project templates, editor integration, and a status line with project,
branch and context usage. `loadout doctor` checks the lot.


## See also

- [Recipes](recipes.md) — worked answers to the common jobs
- [The launcher](launcher.md) — the terminal UI in detail
- [Specialists and skills](specialists.md) — how an instruction set is composed
