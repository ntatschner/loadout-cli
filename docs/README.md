# Loadout documentation

Start at the [project README](../README.md) for what Loadout is and how to
install it. These pages are the detail.

## Getting going

- [Installing](installing.md) — packages, verification, building your own, updating
- [First run and configuration](first-run.md) — setup, `config.yaml`, environment and security profiles
- [Commands](commands.md) — the command surface, editors, sessions, MCP servers
- [The launcher](launcher.md) — the terminal UI, keys and navigation

## Instructions and context

- [The context budget](context-budget.md) — the layered model, and what each layer costs
- [Context and instruction files](context.md) — project manifests, profiles, and path-scoped rules
- [Specialists and skills](specialists.md) — how an instruction set is composed for a task
- [Memory](memory.md) — recording, compressing and auditing the durable facts

## Watching the cost

- [Usage, telemetry and the status line](usage.md) — token accounting, the OTLP receiver, the status line

## Keeping repositories clean

- [Repository cleanliness](repository-cleanliness.md) — protection, drift, undo and conflict recovery

## Internals

- [Architecture and building](architecture.md) — the platform seam, the build, testing, signing
- [Specialist architecture](specialists-architecture.md) — why the library is shaped the way it is
