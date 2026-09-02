# Loadout

**One place for everything your AI agents need, so none of it ends up in your repo.**

[![CI](https://github.com/ntatschner/loadout-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/ntatschner/loadout-cli/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/ntatschner/loadout-cli)](https://github.com/ntatschner/loadout-cli/releases/latest)
[![Licence](https://img.shields.io/github/license/ntatschner/loadout-cli)](LICENSE)
[![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-informational)](https://github.com/ntatschner/loadout-cli/releases/latest)

Every coding agent wants to leave something in your repository. Instruction
files, a rules directory, MCP config, session state, notes it wrote to itself.
Each tool spells it differently, none of them clean up, and all of it turns up
in someone else's diff eventually.

Loadout keeps that stuff somewhere else. You get one workspace, a plain
directory or a Git repo if you want it versioned, holding the config, memory
and instructions for every project you work on. Your repository goes back to
being code.

It runs natively on Windows, Linux and macOS. No VM, no container, no "works
on Linux, should be fine elsewhere".

![Your repository feeds Loadout, which holds instructions, memory, token
accounting, repo hygiene, sessions and the launcher, and starts your
agent](docs/images/features.svg)

## Install

Grab your platform's archive from the
[latest release](https://github.com/ntatschner/loadout-cli/releases/latest).

### Linux and macOS

```sh
tar -xzf loadout-0.14.0-linux-x64.tar.gz
./install.sh          # goes to ~/.local/bin, no root
loadout setup
```

`install.sh` checks the SHA-256 first and won't install if it doesn't match.
There are `.deb` and `.rpm` packages if you'd rather.

### Windows

```powershell
msiexec /i loadout-0.14.0-win-x64.msi    # per-user, no elevation
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

Run `loadout` with nothing after it and you get the terminal UI. `loadout here`
launches the agent for whatever repo you're in. `loadout <project>` skips
straight to a registered one.

## How it works

Two repositories instead of one. Yours holds source. The workspace holds
everything your agent needs to work on it, so a teammate who's never installed
Loadout sees a clean diff.

When you launch, Loadout reads your repo to see what it's made of, works out
which instructions apply, and builds one context file for that session. The file
goes in a directory only you can read and is deleted when the agent exits.

Nothing is guessed silently. `loadout instructions explain` shows you the whole
set and why each part was picked, before you spend anything on it. The rest is
in **[what you get](docs/features.md)**.

## Documentation

The detail lives in **[docs/](docs/README.md)**:

| Page | What's in it |
| --- | --- |
| [What you get](docs/features.md) | Every part of it, with the commands and what they print |
| [Recipes](docs/recipes.md) | Worked answers to the common jobs, with the commands |
| [Installing](docs/installing.md) | Packages, verification, building your own, updating |
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

CI runs the full suite on Windows, Ubuntu and macOS Apple Silicon, and publishes
all six runtime identifiers. The same tests run everywhere. Where a platform
genuinely can't do something it gets reported as a missing capability rather
than skipped quietly, and `loadout doctor` prints the matrix.

`win-arm64` and `linux-arm64` are cross-compiled but never executed, because no
hosted runner offers them. They're built, not tested, and I'd rather say so than
imply otherwise.

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
