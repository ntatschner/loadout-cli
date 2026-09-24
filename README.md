# Loadout

**Before you start a session, Loadout shows you what your coding agent will be
told and what it'll cost. Everything the agent needs stays out of your
repository.**

[![CI](https://github.com/ntatschner/loadout-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/ntatschner/loadout-cli/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/ntatschner/loadout-cli)](https://github.com/ntatschner/loadout-cli/releases/latest)
[![Licence](https://img.shields.io/github/license/ntatschner/loadout-cli)](LICENSE)
[![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-informational)](https://github.com/ntatschner/loadout-cli/releases/latest)

Loadout starts the coding agent you already use — Claude Code or Codex — with
the right instructions for the job in front of you. It doesn't replace the
agent, and it won't install one for you.

You see which instructions a session will load, and what they cost, before it
starts, for one keystroke. The task line fills itself in, because people typed
it only twice in 122 launches.

## Start here

- **You already use Claude Code or Codex.** Read on, then follow
  [your first run](docs/guides/first-run.md). Four commands get you going.
- **You're new to AI and not a programmer.** Start with
  [Loadout in plain words](docs/plain/README.md). It explains what a coding
  agent is before it asks you to type anything.
- **You use a screen reader, magnification or another assistive technology.**
  Start with [setting up accessibility](docs/guides/accessibility.md), or its
  [plain-language version](docs/plain/accessibility.md). It says plainly what
  has been checked and what hasn't.

![The Loadout launcher. A list of three projects on the left, starstats selected. On the right, its branch, a clean working tree, how much instruction text loads, and nothing needing attention.](docs/images/launcher.svg)

## What it does for you

### The launcher: choose a project, see the session, press Enter

Run `loadout` and pick a project. Enter doesn't start anything yet — it opens
the launch sheet, which asks the four things that decide a session: which agent,
what the task is, how to work, and which profile or worktree. Underneath, it
shows which instructions that task would load and what they cost. Enter again
starts the agent with exactly that. The preview costs you one keystroke.

The task line fills itself in from what the project has already recorded,
because a blank line gets skipped: here it was typed twice in 122 launches, and
sat empty 120 times out of 122. Correcting a sentence is a much lower bar than
writing one. `Ctrl+P` finds any command by what it's for — search `undo` and you
get `backup restore`.

[Launching a session, step by step](docs/guides/launching.md)

### The dashboard: watch and steer team runs from your browser

`loadout team dashboard` serves a page on this machine showing what every team
run is doing, what's waiting for you and what it's spent. Runs that need you
come first, under their own heading. It has six screens — List, Office, Graph,
Timeline, Waiting and Terminal — and every button on it runs the same command
you'd type, so there's one behaviour rather than two that drift apart.

It listens on a loopback address, behind a token that changes every time it
starts, and it fetches nothing from the internet: no webfont, no stylesheet, no
image. A dashboard that needed the network wouldn't work on the machine that
most wants one.

[Using the dashboard, step by step](docs/guides/dashboard.md)

### Specialists and context: the right instructions, with the cost shown

Loadout doesn't hand your agent one enormous prompt that's mostly irrelevant.
There are 103 specialists compiled in, including 16 skills, and it works out
which ones apply from what your repository is made of and the sentence you
typed. It tells you why it picked each one.

```sh
loadout instructions explain "why is this postgres query so slow" --mode investigate
```

In the worked example on [what you get](docs/features.md), that set comes to
2,403 tokens against a 12,000 budget — 20%. Ask how a different task compares
and it shows only the difference: 2,403 to 1,655, 748 fewer, before you've
launched either. Five modes — `implement`, `investigate`, `review`, `advise`
and `coordinate`, which is for the lead of a team run — hold for the whole
session and are never guessed from your wording.
The compiled context is deleted when the agent exits.

[How specialists get chosen](docs/specialists.md)

### Memory: what one session learned, there for the next

`loadout memory` keeps the durable facts about a project — decisions, traps,
quirks of the build — in your workspace, so a fact learned on one machine is
there on the next. Credentials are refused on write and named by pattern, never
by value, because the workspace gets pushed.

Only the index reaches a session, so the context says when to go and look:
before diagnosing a failure, before an unfamiliar error. That's because an index
left to be noticed was acted on once in twelve thousand turns. The other
approach was measured and removed: a hook that searched memory on every prompt
spoke on 56% of 322 real prompts when only 18% had anything relevant to say.

[Memory](docs/memory.md)

### Teams: several agents on one goal, one report back to you

Give a team a goal in your own words. A lead splits it into requests, workers
do them in their own sessions, and you get one report. You can say what "done"
means up front with `--done-when`, and the report says which criteria were met.

Spending is capped. A run with no budget and no round limit is refused before it
starts anything, a run that goes two rounds without progress stops, and a run
stops when its budget is spent. In one real run it used four of its five rounds
and $20.84 of a $25 budget.

[Running a team, step by step](docs/guides/teams.md)

### Accessibility: say once how you read, and three things change

One setting changes how the agent writes to you, what it draws while it works,
and what Loadout itself prints. Nothing is detected — you turn it on, and the
first line of output tells you which profile is active.

```sh
loadout config set accessibility-preset screen-reader
```

There are six presets, named for what they reduce rather than for a condition.
The dashboard was built to WCAG 2.2 AA from its first commit, and axe-core
4.10.2 finds no violations in any of fifteen audited states across its two
pages.

**What hasn't been checked, said as plainly:** no screen reader has been
used with any of it, and no keyboard-only pass by a person has been recorded.
If you use one, what you hear is worth more than any of the above.

[Setting up accessibility, step by step](docs/guides/accessibility.md)

## Keeping your repository yours

Every coding agent wants to leave something in your repository — instruction
files, a rules directory, MCP config, session state. Loadout keeps all of it in
one workspace instead, a plain directory or a Git repository if you want it
versioned. `loadout protect` installs a pre-commit hook and Git excludes that
keep agent files out, and it warns rather than blocks. `loadout migrate` moves
what's already scattered around, shows you the change first and takes a
snapshot you can restore.

## Sessions, cost and the agent asking back

- **Sessions across agents.** List and resume them, with the task, mode and
  profile brought back rather than a bare transcript reopened.
- **What it costs.** `loadout usage` reads the transcripts your agents already
  write, so it needs no setup and has history from before you installed it.
- **The agent can ask back.** Every launch declares nine MCP tools: read a
  specialist, ask what it was given and why, search what the project knows, and
  more. None of them changes your machine or pushes to a remote.
- **One workspace for every machine.** Projects, instructions, memory and MCP
  definitions, with secrets in the OS credential store, referenced by name.

The whole list, with commands, is in [what you get](docs/features.md).

## Agents and editors

| Agent | What you get |
| --- | --- |
| **Claude Code** | The compiled context as a system prompt, session listing and resume, MCP servers per project, and a status line with project, branch and context usage |
| **Codex** | The compiled context as `AGENTS.md` in an ephemeral `CODEX_HOME`, session listing and resume. No status line, because Codex has no equivalent |
| **Anything else** | Define it under `custom_agents` in `config.yaml` — executable, arguments and environment. No code change needed |

`loadout code <project>` opens the repository in your editor under the profile
you've mapped to that agent. VS Code, VS Code Insiders, VSCodium, Cursor and
Neovim are recognised by name; [commands](docs/commands.md) says which of them
honour a profile.

## Install

It runs natively on Windows, Linux and macOS, across six runtime identifiers.
No VM and no container. Grab your platform's archive from the
[latest release](https://github.com/ntatschner/loadout-cli/releases/latest).

**Linux and macOS** — `install.sh` checks the SHA-256 before it extracts
anything, and installs to `~/.local/bin` without root:

```sh
tar -xzf loadout-0.49.0-linux-x64.tar.gz
./install.sh
```

**Windows** — the MSI installs per user, with no elevation:

```powershell
msiexec /i loadout-0.49.0-win-x64.msi
```

**Homebrew** — on macOS or Linux, the tap is rewritten by every release:

```sh
brew install thecodesaiyan/loadout/loadout
```

[Installing, step by step](docs/guides/installing.md) covers checking it worked,
`.deb` and `.rpm` packages, and macOS Gatekeeper.

## Getting started

```sh
loadout setup                  # set up the workspace on this machine
loadout project add .          # register the repo you're standing in
loadout protect                # keep agent files out of it
loadout                        # launcher opens, pick a project, go
```

Add `--dry-run` to any launch to see the whole thing described and start
nothing. [Your first run](docs/guides/first-run.md) walks through what each of
these prints.

## Documentation

Everything is indexed in **[docs/](docs/README.md)**, in four groups: step-by-step
guides, plain-language pages, reference, and internals.

## Status

CI runs the full suite on Windows x64, Ubuntu x64, macOS Apple Silicon, Windows
arm64 and Ubuntu arm64, and publishes all six runtime identifiers. Where a
platform genuinely can't do something it's reported as a missing capability
rather than skipped quietly, and `loadout doctor` prints the matrix. macOS
signing and notarisation aren't done yet.

## Building

```sh
dotnet build Loadout.slnx
dotnet test tests/Loadout.Tests/Loadout.Tests.csproj
```

You need .NET SDK 10.0.303 exactly, which `global.json` pins. Package versions
are pinned and locked too, so the same commit builds the same binaries.
[Architecture](docs/architecture.md) has the layout, and
[CONTRIBUTING.md](CONTRIBUTING.md) has the rules that will get a change sent
back.

Found a security problem? [SECURITY.md](SECURITY.md) says how to report it
privately.

## Licence

MIT, see [LICENSE](LICENSE). Third-party notices are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and `build/licences.ps1`
fails the build if a dependency turns up that can't ship under it.
