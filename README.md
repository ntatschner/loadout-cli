# Agent Workspace Launcher

`agentctl` launches AI coding agents against development projects while keeping
agent configuration, context, prompts, skills and runtime state **out of the
application repository**.

Windows, Linux and macOS are Tier-1: each runs the complete launcher natively,
with no VM, container, remote host or compatibility layer standing in for any
other platform.

## Status

Milestones 1 to 3 are implemented. The platform seam, project registry, Git
integration, agent detection, context compiler, profiles, preflight, handoffs,
repository policy, migration, conflict recovery, CLI and TUI all work on all
three platforms. Packaging and an owned pseudo-terminal are the remaining
milestone; see [Roadmap](#roadmap).

## Install

Download the archive for your platform, verify it, and install:

```bash
tar -xzf agentctl-0.1.0-linux-x64.tar.gz
./install.sh                       # installs to ~/.local/bin, no root needed
agentctl setup
```

`install.sh` verifies the SHA-256 before extracting anything and refuses to
install on a mismatch. On macOS it also clears the download quarantine
attribute from the installed binary — until the binary is signed and notarised
Gatekeeper would otherwise block it, and clearing the attribute on one file is
the honest fix. The documentation never tells anyone to disable Gatekeeper,
which spec section 85 forbids.

On Windows, extract the zip and put `agentctl.exe` somewhere on `PATH`.

### Building a release locally

```bash
pwsh ./build/package.ps1 -Runtime osx-arm64 -Version 0.1.0
```

Produces the archive and its checksum in `artifacts/`. Unix archives are built
with the executable bit set even when packaged from Windows, where the
filesystem has no mode to preserve — without that the extracted binary would not
run. This needs the GNU `tar` that ships with Git; the `bsdtar` built into
Windows cannot set the bit and the script says so rather than producing a
quietly broken archive.

## Build and run

```bash
dotnet build
dotnet run --project src/AgentWorkspace.Cli -- doctor
dotnet test
```

Publish a self-contained binary:

```bash
dotnet publish src/AgentWorkspace.Cli -c Release -r osx-arm64 --self-contained
```

Supported runtime identifiers: `win-x64`, `win-arm64`, `linux-x64`,
`linux-arm64`, `osx-x64`, `osx-arm64`.

## Commands

| Command | Purpose |
|---|---|
| `agentctl` | Interactive project selector, or first-run setup |
| `agentctl setup` | Configure the launcher on this machine |
| `agentctl <project>` | Launch the project's default agent |
| `agentctl here` | Launch the agent for the current repository |
| `agentctl doctor` | Platform, Git, workspace, secret and agent diagnostics |
| `agentctl status` | Summary of workspace, projects and agents |
| `agentctl project add\|list\|show\|remove\|discover\|open` | Manage project registration |
| `agentctl project clone\|relocate <project>` | Get a registered project onto this machine |
| `agentctl config list\|get\|set\|edit` | Read and write launcher settings |
| `agentctl workspace status\|sync\|save\|open` | Manage the central workspace clone |
| `agentctl desktop` | Install the Start Menu or `.desktop` entry |
| `agentctl secret set\|test\|remove` | Manage credentials in the OS keystore |
| `agentctl repo check` | Check a repository for tracked AI tooling files |
| `agentctl protect` | Install a pre-commit hook, or `--global` Git excludes |
| `agentctl migrate` | Move existing AI tooling files into the workspace |
| `agentctl project worktrees <project>` | List a project's working trees |
| `agentctl handoff <project>` | Create, show or list cross-agent handoffs |
| `agentctl profile list <project>` | Show a project's context profiles |
| `agentctl completion <shell>` | Emit a completion script |

Every command accepts `--json`, and everything after a bare `--` is passed to
the agent untouched:

```bash
agentctl starstats --agent claude --profile database -- --verbose
```

Exit codes are stable and documented in
[`ExitCode.cs`](src/AgentWorkspace.Models/ExitCode.cs).

## Context

Each project gets a manifest in the central workspace at
`projects/<slug>/project.yaml`. It lists the instruction files that make up the
project's context, plus any named profiles:

```yaml
context:
  global:
    - global/instructions/engineering.md
  project:
    - context/architecture.md

profiles:
  database:
    description: Audit and schema work
    context:
      - context/database.md

environment:
  ANTHROPIC_API_KEY:
    secret: anthropic/default
```

At launch the compiler assembles those into one file, ordered from general to
specific — organisation policy, then project, then the agent's own
instructions, then the profile, then any handoff — so where two sources
disagree the agent reads the narrower one last. The file lands in a per-launch
runtime directory with owner-only permissions and is deleted when the agent
exits.

Only the launching agent's instructions are included, so a Claude session is
never handed Codex's notes. Adapters differ only in delivery: Claude gets the
file as a system prompt, Codex gets an ephemeral `CODEX_HOME` seeded from the
workspace with the compiled context as its `AGENTS.md`. The workspace clone is
never written to by an agent.

Secret references resolve through the platform keystore during preflight and
reach the child process only. The reference is what gets committed; the value
never is, and it is never written to a log or a diagnostic report.

## First run

```bash
agentctl setup
```

Running `agentctl` with no arguments on an unconfigured machine goes here too,
because an empty project list tells a new user nothing about what to do next.

The wizard offers the three choices of spec section 61 as equals — point at an
existing central workspace, create a new one, or **run without central
storage**. The last is a real way to use the tool, not a degraded mode: it
creates the same directory layout locally, so adopting a shared workspace later
is a matter of pushing what you already have.

It then checks Git is present before asking anything, sets a Git identity if
none exists (without one, every workspace commit later fails with "Author
identity unknown"), picks a secret provider that actually works on this machine,
offers the global Git excludes, and lists repositories it found in your
development roots so you can register them.

## Environments and security profiles

A project can define environments, and selecting one changes both which
credentials resolve and how much the agent is allowed to do:

```yaml
environments:
  production:
    description: Production investigation
    security_profile: production
    environment:
      DATABASE_URL:
        secret: starstats/production-db
```

```bash
agentctl starstats --environment production
```

Security profiles are expressed in the launcher's own vocabulary — filesystem,
network, approvals, tool lists — and each adapter translates them into whatever
its agent actually supports. A project says "production work is read-only"
once, and Claude and Codex each honour it as far as they can:

| Profile filesystem | Claude | Codex |
|---|---|---|
| `Repository` | agent default | `--sandbox workspace-write` |
| `ReadOnly` | `--permission-mode plan` | `--sandbox read-only` |
| `Restricted` | `--permission-mode manual` | `--sandbox read-only` |

**A profile can only ever tighten.** There is no value that loosens an agent's
defaults, and the adapters never emit `--dangerously-skip-permissions`,
`bypassPermissions`, `danger-full-access` or their equivalents. A profile lives
in a shared repository; if one could loosen a sandbox, anyone who could edit
that repository could switch off somebody else's safety controls. Tests assert
this over every built-in profile.

Naming an environment that does not exist stops the launch rather than falling
back — someone who typed `--environment prod` meaning `production` must not
quietly get development's permissions.

Where an installed agent does not advertise the option needed to enforce part of
a profile, the launcher says so instead of proceeding silently.

## Saving what a session produced

Agents change workspace files during a session — context notes, decisions,
handoffs. When the agent exits, the launcher applies the exit policy from
`config.yaml`:

| `sync-exit` | Behaviour |
|---|---|
| `prompt` (default) | Offers save-and-sync, save-locally, review, or leave |
| `always` | Commits and pushes without asking |
| `never` | Leaves the changes alone |

Commits follow the format in spec section 46, so a workspace history reads as a
record of which machine did what:

```
agent-workspace: update StarStats context

Project: StarStats
Agent: claude
Machine: DEV-PC
```

A session that only read produces no commit. If a push fails the commit has
already happened, and the message says so rather than implying the work went
nowhere. The fourth option is deliberately "leave them uncommitted" rather than
"discard": the launcher has no business deleting work somebody just did.

The prompt lives in the CLI, not in core. Core decides whether a person needs
to be asked; it never asks, because spec section 37 forbids a menu appearing in
a pipe or a CI job. Non-interactively the changes are left in place and
`agentctl workspace save` is suggested.

## Repository cleanliness

The launcher's central claim is that application repositories hold application
source and agent state lives elsewhere. Three commands make that verifiable
rather than aspirational:

```bash
agentctl repo check          # what is tracked that should not be
agentctl migrate --dry-run   # what would move, and where
agentctl protect --global    # stop it happening again
```

`repo check` distinguishes three states, and the distinction is the substance
of it. A **tracked** agent file is a committed violation and exits 9. An
**untracked but visible** one is a single `git add .` away from becoming one,
so it warns. An **ignored** one is the system working, and is not reported.

`migrate` always shows its plan first and **never deletes a tracked file**.
Removing something Git is tracking rewrites the repository, which is a commit
the user should make and review themselves; the launcher copies it into the
workspace and tells them exactly what is still there. Untracked files are moved
outright, because nothing committed them and moving them is the only way the
repository actually becomes clean.

`protect` installs a pre-commit hook written as POSIX shell, which Git runs the
same way on all three platforms. It re-derives the check from Git rather than
calling back into `agentctl`, so it keeps working on a machine where the
launcher has been moved. A hook the launcher did not write is never overwritten
or deleted. Hooks live in `.git/hooks` and so are per-clone — `agentctl doctor`
reports when the clone you are standing in has none.

## Conflict recovery

When the local workspace and the remote have both moved, the launcher refuses
to fast-forward. Before anything else touches the clone it labels the local
state:

```
Conflict  Local and remote workspaces have diverged.
          Local work is preserved on branch 'recovery/DEV-PC/2026-08-22-2114'.
```

HEAD is never moved and nothing is merged or reset. Spec section 47 says no
data loss is acceptable, and a branch costs nothing.

## Architecture

```
AgentWorkspace.Models      Records, DTOs, config classes. No logic.
AgentWorkspace.Platform    Abstractions + Windows / Linux / macOS / Unix implementations.
AgentWorkspace.Core        Projects, Git, Workspace, Configuration, Security, Diagnostics.
AgentWorkspace.Agents      Claude, Codex and generic adapters; the launch pipeline.
AgentWorkspace.Cli         The agentctl executable.
AgentWorkspace.Tui         The interactive selector.
```

The rule that makes cross-platform parity hold is that **`Core`, `Agents` and
`Tui` depend on `Platform.Abstractions` only**. Exactly one file —
[`PlatformServices.cs`](src/AgentWorkspace.Platform/PlatformServices.cs) —
branches on the operating system. Two tests in
[`ArchitectureTests.cs`](tests/AgentWorkspace.Tests/Architecture/ArchitectureTests.cs)
enforce this: one reads each assembly's type-reference table to prove no shared
assembly touches a platform implementation, the other proves no project carries
an OS-suffixed target framework.

### Where things are stored

| | Windows | Linux | macOS |
|---|---|---|---|
| Config | `%APPDATA%\AgentWorkspaceLauncher` | `$XDG_CONFIG_HOME/agent-workspace-launcher` | `~/Library/Application Support/AgentWorkspaceLauncher` |
| State | `%LOCALAPPDATA%\AgentWorkspaceLauncher` | `$XDG_DATA_HOME/agent-workspace-launcher` | `…/Application Support/AgentWorkspaceLauncher/state` |
| Cache | `…\cache` | `$XDG_CACHE_HOME/agent-workspace-launcher` | `~/Library/Caches/AgentWorkspaceLauncher/cache` |
| Logs | `…\logs` | `$XDG_STATE_HOME/agent-workspace-launcher/logs` | `~/Library/Logs/AgentWorkspaceLauncher` |
| Secrets | Credential Manager | Secret Service (libsecret) | Keychain |

macOS uses native conventions by default. Set `AGENTCTL_USE_XDG=1` to place
launcher files under the XDG roots instead.

`config.yaml` is portable user preference. `machines.yaml` holds this machine's
absolute paths and never leaves it — the same project definition works unchanged
on a Windows desktop, a Linux workstation and a Mac.

### Capabilities, not silent gaps

Anything a platform cannot do is reported rather than quietly skipped. Run
`agentctl doctor` to see the full matrix; each unavailable capability carries
the reason. Known gaps today:

- **Pseudo-terminal** — the launcher does not own a PTY yet. Agents inherit the
  current terminal, which gives correct signals, resize and exit codes for
  terminal launches. An owned PTY is needed only for desktop launch.
- **macOS desktop integration** — `agentctl desktop` installs a Start Menu
  shortcut on Windows and a `.desktop` entry on Linux. On macOS the `.app`
  bundle is not built yet, so the command says so and declines. Every feature
  stays reachable from the CLI and TUI.

## Testing

```bash
dotnet test
```

The suite is deliberately structured so most of it runs everywhere:

- **Shared acceptance tests** exercise registration, resolution, discovery and
  Git against real repositories, with identical assertions on all three
  platforms.
- **Path layout tests** verify the Windows, Linux and macOS layouts from *any*
  host by injecting the environment, so no layout is left unverified on a given
  CI leg.
- **Platform tests** (Credential Manager, Unix mode bits) skip rather than
  silently pass off their platform, so the run summary shows what did not apply.

## Roadmap

- **M1 — done.** Platform seam, registry, Git, agent detection, CLI, TUI, doctor.
- **M2 — done.** Context compiler, profiles, Claude and Codex invocation, preflight, handoffs.
- **M3 — done.** Repository policy, Git protection, migration, conflict recovery, worktrees.
- **M4 — partly done.** Setup wizard, workspace save-on-exit, desktop
  integration, environments, security profiles and release packaging are in.
  Native installers (MSI, `.deb`, `.rpm`), the update system, an owned PTY and
  macOS signing and notarisation remain.
