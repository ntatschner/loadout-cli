# Loadout

**Your core loadout for working with AI — in one place, not scattered through every repository.**

[![CI](https://github.com/ntatschner/loadout-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/ntatschner/loadout-cli/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/ntatschner/loadout-cli)](https://github.com/ntatschner/loadout-cli/releases/latest)
[![Licence](https://img.shields.io/github/license/ntatschner/loadout-cli)](LICENSE)
[![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-informational)](https://github.com/ntatschner/loadout-cli/releases/latest)

`loadout` launches AI coding agents against your projects while keeping their
configuration, instructions, memory and runtime state **out of the application
repository** and in one central workspace you can take to any machine.

Windows, Linux and macOS are all Tier-1: each runs the whole launcher natively,
with no VM, container, remote host or compatibility layer standing in for
another platform.

---

## The problem

Agents leave things behind. Instruction files, rules directories, MCP
configuration, session state, notes to themselves — each tool in its own
dialect, each one in your repository, each one arriving in someone else's diff.
Move to another machine and none of it follows you. Publish the repository and
it all goes out with the code.

Loadout keeps that material in a central workspace — a plain directory, or a
Git repository if you want it versioned and shared across machines — and leaves
your project holding nothing but code.

## Install

Grab the archive for your platform from the
[latest release](https://github.com/ntatschner/loadout-cli/releases/latest).

### Linux and macOS

```sh
tar -xzf loadout-0.9.2-linux-x64.tar.gz
./install.sh          # installs to ~/.local/bin, no root required
loadout setup
```

`install.sh` verifies the SHA-256 before extracting and refuses to install on a
mismatch. There are `.deb` and `.rpm` packages too.

### Windows

```powershell
msiexec /i loadout-0.9.2-win-x64.msi    # per-user, no elevation
loadout setup
```

The MSI installs to `%LOCALAPPDATA%\Programs\loadout`, adds it to your `PATH`
and creates a Start Menu entry. A plain `.zip` is published as well.

See [Installing](docs/installing.md) for
verification, system-wide installs, and the macOS Gatekeeper note.

## Quick start

```sh
loadout setup                  # configure the workspace on this machine
loadout project add .          # register the repository you are standing in
loadout protect                # keep AI tooling files out of it
loadout                        # open the launcher, pick a project, go
```

`loadout` on its own opens the terminal UI. `loadout here` launches the agent
for the repository you are already in, and `loadout <project>` goes straight to
a registered one.

## What it does

### Instructions composed for the task, not one permanent prompt

71 specialists ship inside the binary — foundations, modes, languages,
frameworks, databases, platforms, clouds, functional areas and skills. Loadout
picks the ones a task actually needs, from the repository you are standing in
and from the words of the request, and tells you why each is there:

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

Every selection carries its reason, the whole set is costed in tokens before
anything launches, and overlaps are reported rather than silently resolved.
Nothing is a black box you have to accept.

### Project memory that stays small

`loadout memory` records the durable facts about a project — decisions,
constraints, the things that are not derivable from the code. `memory compress`
moves facts *out* of always-loaded instruction files into that store, so what
every session pays for stays small while what it can look up stays complete.
`memory audit` checks for secrets, duplicates, staleness and index rot.

### Usage you can actually read

`loadout usage` reports what your agents have spent, by project, day, model or
agent, reading the transcripts the agents already write. Cached input is
accounted at its real rate rather than counted as fresh, and when a total
cannot be trusted it says so instead of quietly reporting a smaller number.

`loadout telemetry` additionally runs a **local** OpenTelemetry receiver for
agents that emit OTLP. Nothing is sent anywhere: no cloud service, no account,
no repository contents, prompts or conversations — only counts.

### Repositories that stay clean

`loadout protect` installs the Git protections that keep AI tooling files out
of a repository. `loadout migrate` moves what is already there into the central
workspace, previewing every change first and taking a restorable snapshot
before it touches anything. `loadout drift` shows where projects have wandered
from their recorded configuration.

### The rest

Agent sessions you can list and resume, cross-agent handoff documents, MCP
server management per project, secrets in the platform credential store,
context profiles, editor integration, a status line showing project, branch and
context usage, and `loadout doctor` to check the lot.

Everything works from the CLI and the TUI, every command speaks `--json`, and
exit codes are stable.

## Documentation

Full documentation is in **[docs/](docs/README.md)**:

| Page | What is in it |
| --- | --- |
| [Installing](docs/installing.md) | Packages, verification, building your own, updating |
| [First run](docs/first-run.md) | Setup, `config.yaml`, environment and security profiles |
| [Commands](docs/commands.md) | The command surface, editors, sessions, MCP servers |
| [The launcher](docs/launcher.md) | The terminal UI, keys and navigation |
| [The context budget](docs/context-budget.md) | The layered model, and what each layer costs |
| [Specialists and skills](docs/specialists.md) | How an instruction set is composed, and writing your own |
| [Memory](docs/memory.md) | Recording, compressing and auditing project facts |
| [Usage and telemetry](docs/usage.md) | Token accounting, the OTLP receiver, the status line |
| [Repository cleanliness](docs/repository-cleanliness.md) | Protection, migration, drift and undo |
| [Architecture](docs/architecture.md) | The platform seam, the build, testing, signing |

## Status

Milestones 1 to 4 are implemented, less macOS signing and notarisation.

CI runs the full suite on Windows, Ubuntu and macOS Apple Silicon and publishes
all six runtime identifiers. The same test methods run on every platform; where
a platform genuinely cannot do something it is reported as a capability rather
than skipped in silence — `loadout doctor` prints the matrix.

`win-arm64` and `linux-arm64` are cross-compiled and **not** executed in CI,
because no hosted runner offers them. They are built, not tested.

## Building

```sh
dotnet build Loadout.slnx
dotnet test tests/Loadout.Tests/Loadout.Tests.csproj
```

.NET 10 SDK, no OS-specific target frameworks. See
[Architecture](docs/architecture.md) for
the layout and the rules that keep the platform seam intact.

## Licence

MIT — see [LICENSE](LICENSE). Third-party notices are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); `build/licences.ps1` checks
that every dependency can be shipped under it.
