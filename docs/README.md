# Loadout documentation

The [README](../README.md) says what Loadout does for you and how to install it.
This is everything behind it, in four groups. Guides walk you through one job
each. Plain-language pages cover the same jobs for people new to AI. Reference
pages have every option. Internals are for people changing Loadout itself.

## Guides

Step by step, one job each: numbered steps, the exact command, and what you
should see.

- [Installing](guides/installing.md) — download, check, install, and confirm it worked
- [Your first run](guides/first-run.md) — set up the workspace, register a repository, protect it, open the launcher
- [Launching a session](guides/launching.md) — the launch sheet, the task, the mode, and a dry run
- [Using the dashboard](guides/dashboard.md) — start it, open it, find your way round, answer a gate, stop a run
- [Running a team](guides/teams.md) — choose a team, say what done means, watch it, read what it delivered
- [Setting up accessibility](guides/accessibility.md) — choose a preset, turn it on, and what has and hasn't been verified

## Plain language

For people new to AI who aren't programmers. Short sentences, one idea per
step, and every term explained the first time it appears.

- [Loadout in plain words](plain/README.md) — what a coding agent is, what Loadout does, and what you need first
- [Installing Loadout](plain/installing.md) — starting from opening a terminal
- [Your first launch](plain/first-launch.md) — from setting up to your first session
- [Watching a team](plain/watching-a-team.md) — what a team is, and how to start, watch and stop one
- [Accessibility](plain/accessibility.md) — telling Loadout how you read

## Reference

- [Getting started](getting-started.md) — the first hour with Loadout, in order, and the week after
- [Working with a coding agent](agentic-coding.md) — the basics and the habits, by how long you have been at it
- [What you get](features.md) — every part of Loadout, with the commands
- [Recipes](recipes.md) — worked answers to the common jobs, with the commands to run
- [Installing](installing.md) — packages, verification, building your own, updating
- [What it needs](dependencies.md) — the tools Loadout drives, and the libraries it ships with
- [First run and configuration](first-run.md) — setup, `config.yaml`, environment and security profiles
- [Commands](commands.md) — the whole command surface, editors, sessions, MCP servers
- [The launcher](launcher.md) — the terminal UI, keys and navigation
- [Teams](teams.md) — several agents on one goal: the teams that ship, writing your own, watching a run
- [Accessibility](accessibility.md) — how you are written to and asked, and what was verified

### Instructions and context

- [The context budget](context-budget.md) — what loads when, and what each layer costs you
- [Context and instruction files](context.md) — project manifests, profiles, path-scoped rules
- [Specialists and skills](specialists.md) — how an instruction set gets composed for a task
- [Memory](memory.md) — recording, compressing and auditing the durable facts

### Watching the cost

- [Usage, telemetry and the status line](usage.md) — token accounting, the OTLP receiver, the status line

### Keeping repos clean

- [Repository cleanliness](repository-cleanliness.md) — protection, drift, undo and conflict recovery

## Internals

- [Architecture and building](architecture.md) — the platform seam, the build, testing, signing
