# Loadout

**A launcher for your coding agents. Pick a project, say what you're doing,
see what the agent will be told, go.**

[![CI](https://github.com/ntatschner/loadout-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/ntatschner/loadout-cli/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/ntatschner/loadout-cli)](https://github.com/ntatschner/loadout-cli/releases/latest)
[![Licence](https://img.shields.io/github/license/ntatschner/loadout-cli)](LICENSE)
[![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-informational)](https://github.com/ntatschner/loadout-cli/releases/latest)

Run `loadout`, pick a project, and the launch sheet asks the four things that
decide a session: which agent, what the task is, how to work, and which
profile or worktree. Underneath, it shows which specialists that task would
load and what they cost, before anything is spent on them. Enter starts the
agent with exactly that.

What makes that possible is where everything lives. Every coding agent wants
to leave something in your repository — instruction files, a rules directory,
MCP config, session state, notes it wrote to itself — and Loadout keeps all of
it in one workspace instead, a plain directory or a Git repo if you want it
versioned. The launcher reads your repo, works out which instructions apply,
compiles one context for the session, and deletes it when the agent exits.
Your repository goes back to being code.

It runs natively on Windows, Linux and macOS. No VM, no container, no "works
on Linux, should be fine elsewhere".

## Features

Everything that's in the box, grouped by the problem it exists for. Each line is
a thing that ships, not a plan.

### Choosing what the agent is told

- **77 specialists compiled in** — foundations, modes, languages, frameworks,
  databases, platforms, clouds, functional areas and 16 skills. Which ones apply
  is worked out from the repository and from the sentence you typed, so a
  project with three hundred `.rs` files gets the Rust guidance without anybody
  saying so.
- **Four modes** — `implement`, `investigate`, `review`, `advise` — that hold for
  the whole session and are never guessed from your wording.
- **`loadout instructions explain`** prints the set, the reason each part was
  picked, the token cost against the budget, and where their guidance overlaps.
  `--against-task` and `--against-mode` show only what changes between two ways
  of asking; `--without` rules one out.
- **Skills as procedures** — a review, a release check, a flaky test, closing a
  session — that activate on the task rather than loading always.
- **Path-scoped rules** so guidance about migrations doesn't load on frontend
  work, with `rules budget`, `rules audit` and `rules split` to find and fix what
  loads regardless.
- **Your own specialists, rules and skills** in the workspace or per project,
  resolved shipped → workspace → project, a later one replacing an earlier one
  of the same name.
- **Specialist packs** fetched from a Git remote and approved per machine, and
  `share candidates` for guidance that's escaped into one project and belongs to
  everybody.
- **Measurement rather than faith** — `instructions stats` says which specialists
  launches actually reached, `instructions probe` how often sessions did what one
  asked for.

### Launching

- **The launcher** (`loadout` on its own): project list, what a session there
  would start with, and whether anything stops a launch. Enter opens the launch
  sheet.
- **The launch sheet** asks the four things that decide a session — agent, task,
  mode, profile or worktree — with the specialists and their cost re-resolved as
  you type, by the same resolver the launch uses.
- **The task line fills itself in** from what the project declared with
  `loadout task declare`, because correcting a sentence is a much lower bar than
  composing one. Only when the field is empty.
- **`Ctrl+P` over every command**, found by what it's for: search `undo` and you
  get `backup restore`, search `broken` and you get `doctor`.
- **The build in the corner**, and `1.3.0 available` when there's a newer one —
  checked once a day in the background, never delaying the screen, never showing
  a failed lookup.
- **`--dry-run` describes the whole launch** — executable, working directory,
  compiled context, environment variables by name, full command line, every
  specialist and why — and starts nothing.
- **Same behaviour from the command line**: `loadout <project>`, `loadout here`,
  `--agent`, `--task`, `--mode`, `--profile`, `--worktree`, `--json`, and
  anything after `--` passed to the agent untouched.

### Memory that stays worth loading

- **Durable facts per project**, in the workspace rather than your repository, so
  a fact learned on one machine is there on the next and a wrong one gets
  corrected in a pull request.
- **Three scopes** — project, user and machine — because "the Restart Manager is
  disabled by policy" is true of a computer, not of a codebase, and a workspace
  that syncs must not carry it as though it were universal.
- **Only the index is loaded**, and the context names the occasions to go and
  look: before diagnosing a failure, before an unfamiliar error, before anything
  about how the project builds, tests, releases or is configured.
- **`loadout memory find`** ranks topics for a question in your own words, with
  the same ranking the agent's own tool uses.
- **`memory compress`** moves standing facts out of always-loaded instructions
  and into the store, verbatim, never reworded, and never removed from the source
  until they've been read back out.
- **`memory audit`** reports secrets, duplicates, oversize topics, dead index
  lines and facts that have gone stale — and a fact pinned to the day it was
  written holds up the verdict rather than sitting under a word saying the store
  is fine.
- **Credentials refused on write**, named by pattern and never by value, because
  the workspace gets pushed.
- **A second topic on the same ground is stopped** and the first one named, so
  memory doesn't quietly come to contradict itself.
- **`memory import` compares what topics say**, not their bytes, so two stores
  keeping the same names and disagreeing get told apart. Named and left alone —
  which copy is right is a judgement, not a merge.

### Keeping your repository yours

- **`loadout protect`** installs the pre-commit hook and Git excludes that keep
  agent tooling out of the repository, and a launch says when a fresh clone is
  unprotected, while it can still be acted on. It warns and never blocks.
- **`loadout migrate`** moves what's already scattered around into the workspace,
  showing the changes first and taking a snapshot you can restore.
- **`loadout drift`** reports where a project has wandered from what you
  configured; **`loadout repo check`** checks one repository for tracked agent
  files.
- **Undo** — every command that changes a file takes `--dry-run`, and anything
  that did change is in a snapshot `backup restore` can put back.
- **Checkpoints** mark where a project stands under a name you can return to.

### Sessions, handoff and history

- **Session listing and resume across agents**, attributed to projects neither
  agent records — and subagent transcripts are kept out of it, so the picker
  holds sessions you can actually resume.
- **Resume recompiles** rather than reopening a bare transcript: the task, mode,
  profile and worktree come back from the launch ledger.
- **Cross-agent handoff documents**, and a session that ran a while and left none
  is told so on the way out, with the command.
- **`loadout launches`** — what this machine started, in which posture, with which
  specialists, and how it ended. `unclosed` and `never ran` are distinct
  outcomes, because calling either a failure invents a result.
- **`loadout running`** — what's open now, and how long each has been quiet.

### What it costs

- **`loadout usage`** reads the transcripts your agents already write, so there's
  history from before you installed it. By project, day, model or agent, as
  Markdown or CSV.
- **Cache reads counted at their real rate**, which on a long session is most of
  the number, and an incomplete total says so rather than quietly reading low.
- **`loadout telemetry serve`** — a local OTLP receiver for agents that emit
  OpenTelemetry. Local means local: no service, no account, counts only.
- **Spend thresholds**, and a **status line** carrying project, branch and
  context usage.

### The agent can ask back

- **Nine MCP tools declared by every launch**: read a specialist in full, ask
  what this session was given and why, search what the project already knows,
  find where a symbol is declared, read the code map, record one screened fact,
  read and update what the project is working on, and change its own mode when
  the work changes shape.
- **Nothing that changes your machine or pushes to a remote**, and the context
  says so rather than leaving it to be worked out. A test asserts no tool is ever
  named for the rest.
- **Skills reach the session as commands it can invoke**, written into the
  per-launch runtime directory rather than into your repository or the agent's
  own configuration home.
- **`loadout task`** keeps what's being worked on — open, doing, done, blocked or
  dropped, each attributed and dated — and checks it against the repository,
  which is how "called done, and nothing has been committed since" gets said out
  loud.
- **`loadout docs find`** answers where a type or member is declared from a symbol
  index kept in step with the repository, and **`loadout docs audit`** reports
  where the documentation has come adrift from it.

### Workspace, machines and projects

- **One workspace** holding projects, instructions, rules, memory, profiles and
  MCP definitions — a plain directory, or a Git repository when you want it
  versioned and shared between machines.
- **Secrets in the OS credential store**, referenced by name. The reference is
  what gets committed; the value never reaches a log or a diagnostic bundle.
- **MCP servers managed per project**, with the whole set shown at once and the
  clashes named: the same service under two names, a shadowed name, a path that
  can't be right on another machine.
- **Editor handoff** — `loadout code <project>` opens the repo under the profile
  mapped to its agent.
- **Project templates, context profiles, worktrees**, and `project survey` for
  agent state on this machine that no project accounts for.
- **Code that isn't a repository yet** can be registered, and says so at launch
  instead of the first session finding out by running something.
- **`loadout doctor`** checks the lot, `--fix` mends what it safely can, and
  `--bundle` writes the findings to one screened file you can send somebody.

### Installing and updating

- **Native on Windows, Linux and macOS**, six runtime identifiers, no VM and no
  container. Where a platform genuinely can't do something it's reported as a
  missing capability rather than skipped quietly.
- **`loadout update`** reads a release feed — this project's own by default, or
  any JSON document or path you point it at. A published SHA-256 is required, the
  hash is checked before anything is put in place, the previous binary is kept,
  and nothing updates without being asked.
- **Packages** — `.msi`, `.deb`, `.rpm`, and archives with a checked install
  script.

## Agents and editors

| Agent | What you get |
| --- | --- |
| **Claude Code** | The compiled context as a system prompt, session listing and resume, MCP servers per project, and a status line with project, branch and context usage |
| **Codex** | The compiled context as `AGENTS.md` in an ephemeral `CODEX_HOME`, session listing and resume. No status line, because Codex has no equivalent |
| **Anything else** | Define it under `custom_agents` in `config.yaml` — executable, arguments and environment. No code change needed, and no wait for us |

Editor handoff is `loadout code <project>`, which opens the repo in the editor
under the profile you've mapped to that agent, so opening a project for Claude
and for Codex can give you different extensions and settings.

**VS Code**, **VS Code Insiders**, **VSCodium** and **Cursor** are recognised by
name, but open in their default profile: they won't open a folder and a profile
in the same launch, and `loadout code` tells you so rather than leaving you to
work out why nothing changed. **Neovim** is recognised too, and its profiles do
apply — `NVIM_APPNAME` names the configuration directory it loads, so the
mapping works end to end.

Any other editor works — `loadout config set editor-command <command>` — and if
it takes a profile, say how under `custom_editors` in `config.yaml`.

More agents and editors are coming. The generic adapter means you don't have to
wait for one: if it takes a directory and starts from a command, you can wire it
up today.

## Install

Grab your platform's archive from the
[latest release](https://github.com/ntatschner/loadout-cli/releases/latest).

### Linux and macOS

```sh
tar -xzf loadout-0.33.3-linux-x64.tar.gz
./install.sh          # goes to ~/.local/bin, no root
loadout setup
```

`install.sh` checks the SHA-256 first and won't install if it doesn't match.
There are `.deb` and `.rpm` packages if you'd rather.

### Windows

```powershell
msiexec /i loadout-0.33.3-win-x64.msi    # per-user, no elevation
loadout setup
```

That puts it in `%LOCALAPPDATA%\Programs\loadout`, adds it to your `PATH` and
makes a Start Menu entry. There's a plain `.zip` too.

[Installing](docs/installing.md) covers verification, system-wide installs and
the macOS Gatekeeper situation.

## Getting started

```sh
loadout setup                  # set up the workspace on this machine
loadout project add .          # register the repo you're standing in
loadout protect                # keep agent files out of it
loadout                        # launcher opens, pick a project, go
```

Run `loadout` with nothing after it and you get the launcher: a project list,
what a session there would start with, and Enter to open the launch sheet.
`loadout here` launches the agent for whatever repo you're in. `loadout
<project>` skips straight to a registered one, and takes the same choices as
flags: `--agent`, `--task`, `--mode`, `--profile`, `--worktree`. Add
`--dry-run` to see the whole launch described and start nothing.

## How it works

Two repositories instead of one. Yours holds source. The workspace holds
everything your agent needs to work on it, so a teammate who's never installed
Loadout sees a clean diff.

When you launch, Loadout reads your repo to see what it's made of, works out
which instructions apply to what you said you're doing, and builds one context
file for that session. The file goes in a directory only you can read and is
deleted when the agent exits.

Nothing is guessed silently. The launch sheet shows the set and why each part
was picked before you start; `loadout instructions explain` shows the same
from the command line. The rest is in **[what you get](docs/features.md)**.

## Documentation

The detail lives in **[docs/](docs/README.md)**:

| Page | What's in it |
| --- | --- |
| [Getting started](docs/getting-started.md) | The first hour, in order, and what's worth doing a week later |
| [Working with a coding agent](docs/agentic-coding.md) | The habits, by how long you've been at it |
| [What you get](docs/features.md) | Every part of it, with the commands and what they print |
| [Recipes](docs/recipes.md) | Worked answers to the common jobs, with the commands |
| [Installing](docs/installing.md) | Packages, verification, building your own, updating |
| [What it needs](docs/dependencies.md) | The tools Loadout drives, and the libraries it ships with |
| [First run](docs/first-run.md) | Setup, `config.yaml`, environment and security profiles |
| [Commands](docs/commands.md) | The whole command surface, editors, sessions, MCP |
| [The launcher](docs/launcher.md) | The terminal UI, keys and navigation |
| [The context budget](docs/context-budget.md) | What loads when, and what it costs |
| [Specialists and skills](docs/specialists.md) | How instructions get composed, and writing your own |
| [Memory](docs/memory.md) | Recording, compressing and auditing project facts |
| [Usage and telemetry](docs/usage.md) | Token accounting, the OTLP receiver, the status line |
| [Repository cleanliness](docs/repository-cleanliness.md) | Protection, migration, drift and undo |
| [Architecture](docs/architecture.md) | The platform seam, the build, testing, signing |

## Status

Milestones 1 to 4 are done, apart from macOS signing and notarisation.

CI runs the full suite on Windows x64, Ubuntu x64, macOS Apple Silicon, Windows
arm64 and Ubuntu arm64, and publishes all six runtime identifiers. The same
tests run everywhere. Where a platform genuinely can't do something it gets
reported as a missing capability rather than skipped quietly, and
`loadout doctor` prints the matrix.

The arm64 binaries are still cross-compiled from x64, which is a deterministic
build and a path that works. What changed is that the code in them is now run:
the suite executes on real arm64 runners on both Windows and Linux.

## Building

```sh
dotnet build Loadout.slnx
dotnet test tests/Loadout.Tests/Loadout.Tests.csproj
```

You need .NET SDK 10.0.303 exactly, which `global.json` pins. Package versions
are pinned and locked too, so the same commit builds the same binaries.

No OS-specific target frameworks anywhere, and a test that fails the build if
one appears. [Architecture](docs/architecture.md) has the layout and the rules
that keep the platform seam honest, and [CONTRIBUTING.md](CONTRIBUTING.md) has
the ones that will get a change sent back.

Found a security problem? [SECURITY.md](SECURITY.md) says how to report it
privately.

## Licence

MIT, see [LICENSE](LICENSE). Third-party notices are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and `build/licences.ps1`
fails the build if a dependency turns up that can't ship under it.
