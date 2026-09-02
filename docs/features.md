# What you get

The [README](../README.md) is the short version. This is each part of it, with
the commands.

### Instructions picked for the job

There are 72 specialists built into the binary: foundations, modes, languages,
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

`loadout protect` sets up the Git protections. `loadout migrate` moves whatever
is already scattered around into the workspace, showing you the changes first
and taking a snapshot you can restore. `loadout drift` tells you when a project
has wandered from what you configured.

### A launcher, not just a command line

![The launcher](images/launcher.svg)

Run `loadout` with nothing after it. Every row says whether you can work on that
project, and the panel on the right says what a session would start with before
you spend one. `Ctrl+P` opens a palette over every command the CLI has, found by
what it is for: searching `undo` reaches `backup restore`, `broken` reaches
`doctor`.

The launcher never implements a command itself. Anything you pick runs through
the same parser you would have typed at, which is asserted by a test rather than
by intent.

### The agent can ask back

A launched session is told `loadout` is on PATH and offered the same operations
as MCP tools: read a specialist in full, ask what this session was given and
why, record one fact worth having next time.

Nothing that changes the machine or pushes to a remote is offered, and the
context says so rather than leaving the omission to be inferred. Ask an agent to
review a repository and it has a procedure for it — `skill.repository-review` —
and somewhere to put what it learns.

### Undo

Every file-changing command takes `--dry-run` and shows you the change first.
Anything that did change is in a snapshot:

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
