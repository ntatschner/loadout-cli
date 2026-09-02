# What Loadout needs, and what it ships with

Loadout does a lot of its work by driving tools you already have rather than
reimplementing them. This is the whole list, so you know what has to be on the
machine and what comes in the box.

## You need

| | Why |
| --- | --- |
| **Git** | Everything to do with the workspace and your repositories: cloning, status, staging, commits, pushes, hooks. Loadout never reimplements Git — it runs it. If it's missing you get "git was not found on PATH", not a mystery. |
| **An agent** | At least one of `claude` or `codex` on `PATH`, or your own defined under `custom_agents`. Loadout starts them; it doesn't bundle them. |

That's it. The binaries are published self-contained, so **there's no .NET
runtime to install**, no VM, no container and no background service.

## Used when it's there, skipped when it isn't

| | What it's for |
| --- | --- |
| **GitHub CLI** (`gh`) | Only during `loadout setup`, and only if you're already signed in. An installed but unauthenticated `gh` is ignored rather than offered, because that route fails halfway through. |
| **An editor** | `loadout code` opens the project. Defaults to `code`; point it elsewhere with `loadout config set editor-command <command>`. |
| **A terminal emulator** | For launching an agent in its own window. Windows Terminal, PowerShell or Windows PowerShell on Windows; Ghostty, WezTerm, kitty, Alacritty, GNOME Terminal, Konsole or xterm on Linux; Terminal, iTerm2, Warp, Ghostty, WezTerm, kitty or Alacritty on macOS. |
| **`secret-tool`** (Linux only) | Reaching the login keyring for stored secrets. Install `libsecret-tools` on Debian and Ubuntu, or `libsecret` on Fedora and RHEL. Without it, use a different secret provider. |
| **PowerShell** (Windows only) | Creating the Start Menu shortcut, nothing else. |

## What it reads from the agent's own tooling

Loadout doesn't only start agents — it reads the state they keep, so adopting it
doesn't mean starting from nothing on the projects you've done the most work on.

| | |
| --- | --- |
| **Claude Code's memory** | `~/.claude/projects/<derived>/memory`, or `$CLAUDE_CONFIG_DIR` when you've moved it. `loadout project survey` finds it, `loadout memory import` brings it into the workspace. The directory name is derived the way Claude Code derives it — separators, colons and dots become hyphens — because it has to match exactly what the other tool already wrote. |
| **Agent transcripts** | Where `loadout usage` gets its token accounting. Nothing to switch on, and you have history from before you installed Loadout. |
| **`.claude/rules/`** | `loadout migrate` moves these into `projects/<slug>/rules/` in the workspace, because which instructions apply to which paths is true whichever agent reads them. |

Nothing here is written by Loadout in place. Memory is copied rather than moved,
so a bad import loses nothing, and removing the old copy is left to you.

## Platform features it uses directly

Nothing to install for these — they're part of the operating system.

| | |
| --- | --- |
| **Windows Credential Manager** | Secrets, through `advapi32`. |
| **macOS Keychain** | Secrets, through `/usr/bin/security`. |

## What ships inside the binary

| | What it does here |
| --- | --- |
| [Spectre.Console](https://spectreconsole.net/) and Spectre.Console.Cli | The command line: parsing, tables, prompts, colour |
| [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) | The launcher's full-screen screens |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | Reading and writing `config.yaml` and the project manifests |
| [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) | Serving Loadout's own tools to an agent over MCP |
| Microsoft.Extensions.DependencyInjection | Wiring the services together at startup |

Licences and full attribution are in
[THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

## When it uses the network

Only when you ask it to, and the commands that do say so:

- `loadout setup` — cloning or creating the workspace
- `loadout workspace save` and `workspace sync` — pushing and pulling it
- `loadout project clone` — fetching a registered repository
- `loadout update` — checking for and downloading a release

Everything else works offline, and `--offline` makes that explicit by using the
cached workspace. There's no telemetry endpoint, no account, and nothing is sent
anywhere. `loadout telemetry serve` is a receiver that listens on this machine
for agents that emit OpenTelemetry; it stores locally and sends nothing.

## See also

- [Installing](installing.md) — packages, verification, building your own
- [First run](first-run.md) — setup, `config.yaml`, secrets
