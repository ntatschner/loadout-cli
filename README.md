# Loadout

`loadout` launches AI coding agents against development projects while keeping
agent configuration, context, prompts, skills and runtime state **out of the
application repository**.

Windows, Linux and macOS are Tier-1: each runs the complete launcher natively,
with no VM, container, remote host or compatibility layer standing in for any
other platform.

### Renamed from Agent Workspace Launcher

The tool was called `agentctl` while it was a launcher. It is not only that any
more — most of it is about what an agent is told and what that costs — so it is
now `loadout`: the kit assembled for a job, chosen rather than carried wholesale.
This departs from spec section 17, which names the binary `agentctl` with the
alias `aiw`; the section is superseded rather than overlooked.

Nothing from the old name is orphaned. The data directory is renamed in place on
first run, the `AGENTCTL_SECRET_*` environment variables are still read, and a
repository marked `agentctl.project` is still recognised —
`loadout project link --all` re-marks it and clears the old key, so the fallback
is a bridge rather than a permanent second answer.

## Status

Milestones 1 to 3 are implemented. The platform seam, project registry, Git
integration, agent detection, context compiler, profiles, preflight, handoffs,
repository policy, migration, conflict recovery, CLI and TUI all work on all
three platforms. Packaging and an owned pseudo-terminal are the remaining
milestone; see [Roadmap](#roadmap).

## Install

Download the archive for your platform, verify it, and install:

```bash
tar -xzf loadout-0.1.0-linux-x64.tar.gz
./install.sh                       # installs to ~/.local/bin, no root needed
loadout setup
```

`install.sh` verifies the SHA-256 before extracting anything and refuses to
install on a mismatch. On macOS it also clears the download quarantine
attribute from the installed binary — until the binary is signed and notarised
Gatekeeper would otherwise block it, and clearing the attribute on one file is
the honest fix. The documentation never tells anyone to disable Gatekeeper,
which spec section 85 forbids.

On Windows, extract the zip and put `loadout.exe` somewhere on `PATH`.

### Native installers

A release also carries an `.msi`, a `.deb` and an `.rpm` for people who would
rather not manage a `PATH` entry by hand:

```powershell
msiexec /i loadout-0.1.0-win-x64.msi        # per-user, no elevation
```

```bash
sudo dpkg -i loadout_0.1.0_amd64.deb        # or: sudo rpm -i loadout-0.1.0-1.x86_64.rpm
```

The MSI installs per user into `%LOCALAPPDATA%\Programs\loadout`, adds that
directory to the user `PATH` and creates a Start Menu entry. It installs
somewhere other than the launcher's own data directory on purpose: they would
otherwise share a parent, and an uninstall that tidied up its install root a
little too enthusiastically would take the workspace clone and backup sets with
it. Uninstalling removes the binaries, the `PATH` entry and the shortcut, and
leaves everything under `%LOCALAPPDATA%\Loadout` alone.

The Linux packages put the self-contained build under `/usr/lib/loadout` with
a symlink at `/usr/bin/loadout`, rather than emptying a hundred-file publish
directory into `/usr/bin`.

macOS has archives only. A `.pkg` needs signing and notarisation to be
installable without the user fighting Gatekeeper, and that needs a Developer ID
and a Mac to verify it on; until both exist, shipping an unsigned installer
would be worse than shipping none.

### Building a release locally

```bash
pwsh ./build/package.ps1 -Runtime linux-x64 -Version 0.1.0     # archive
pwsh ./build/installer.ps1 -Runtime win-x64 -Version 0.1.0     # native installer
```

The installer script builds each format with the tooling that owns it — WiX for
the MSI, `dpkg-deb` and `rpmbuild` for the Linux packages — so it refuses to
build a Linux package on Windows rather than assembling the container format by
hand. A `.deb` written by an `ar` writer of our own would work right up until it
did not, and would then fail inside somebody else's package manager where the
error would make no sense to them.

The MSI needs WiX 5 (`dotnet tool install --global wix --version 5.0.2`). The
pin is deliberate: WiX 6 and later require accepting the Open Source Maintenance
Fee agreement, which is a decision for whoever owns this project rather than one
a build script should make on their behalf.

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
dotnet run --project src/Loadout.Cli -- doctor
dotnet test
```

Publish a self-contained binary:

```bash
dotnet publish src/Loadout.Cli -c Release -r osx-arm64 --self-contained
```

Supported runtime identifiers: `win-x64`, `win-arm64`, `linux-x64`,
`linux-arm64`, `osx-x64`, `osx-arm64`.

## Commands

| Command | Purpose |
|---|---|
| `loadout` | Interactive project selector, or first-run setup |
| `loadout setup` | Configure the launcher on this machine |
| `loadout <project>` | Launch the project's default agent |
| `loadout here` | Launch the agent for the current repository |
| `loadout doctor` | Platform, Git, workspace, secret and agent diagnostics |
| `loadout status` | Summary of workspace, projects and agents |
| `loadout project add\|list\|show\|remove\|discover\|open` | Manage project registration |
| `loadout project clone\|relocate <project>` | Get a registered project onto this machine |
| `loadout project survey [--adopt]` | Find agent state no project accounts for, and take on what it can |
| `loadout project link [project]` | Record inside a repository which project it belongs to |
| `loadout config list\|get\|set\|edit` | Read and write launcher settings |
| `loadout workspace status\|sync\|save\|open` | Manage the central workspace clone |
| `loadout desktop` | Install the Start Menu or `.desktop` entry |
| `loadout update` | Check the release source and install a newer build |
| `loadout secret set\|test\|remove` | Manage credentials in the OS keystore |
| `loadout repo check` | Check a repository for tracked AI tooling files |
| `loadout protect` | Install a pre-commit hook, or `--global` Git excludes |
| `loadout migrate` | Move existing AI tooling files into the workspace |
| `loadout project worktrees <project>` | List a project's working trees |
| `loadout handoff <project>` | Create, show or list cross-agent handoffs |
| `loadout profile list <project>` | Show a project's context profiles |
| `loadout rules list\|budget\|audit <project>` | Inspect the instruction rules and what they cost |
| `loadout rules split <project>` | Break an oversized instruction file into scoped rules |
| `loadout memory list\|write\|audit\|reindex <project>` | Record and check durable project facts |
| `loadout memory import <project>` | Bring in memory an agent recorded outside the workspace |
| `loadout memory audit --clean <project>` | Remove empty topics, exact repeats and dead index lines |
| `loadout backup list\|restore` | Undo an operation that changed files |
| `loadout completion <shell>` | Emit a completion script |

Every command accepts `--json`, and everything after a bare `--` is passed to
the agent untouched:

```bash
loadout starstats --agent claude --profile database -- --verbose
```

Exit codes are stable and documented in
[`ExitCode.cs`](src/Loadout.Models/ExitCode.cs).

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

## Instructions that scale

An instruction file that is loaded on every session is paid for on every turn,
whatever the task. Two mechanisms keep that cost proportionate.

**Rules** are Markdown files under `global/rules/` and `projects/<slug>/rules/`
carrying frontmatter that says when they apply:

```markdown
---
description: Migrations and schema work
globs: src/Data/**, **/*.sql
alwaysApply: false
---
```

A rule with `alwaysApply: true` is inlined into every compiled context. A scoped
rule is not: the context lists its name, scope and path, and the agent reads it
when the work touches those paths. A project rule overrides a workspace rule of
the same name, so a project can depart from the house style without editing it.

`loadout rules budget` reports what loads regardless of the task.
`loadout rules audit` reports the defects that cost tokens invisibly — an
instruction written in two places, a rule that declares globs *and*
`alwaysApply` (the globs are decorative; it loads always), two rules claiming
the same paths, and `@import` lines whose size appears in nobody's budget.

`loadout rules split` breaks an existing instruction file apart. It needs a map
saying which sections belong to which rule and what each rule's scope is —
that judgement is about the project and the tool will not guess it — so start
with `loadout rules split --write-map`, fill in the globs, then preview:

```
$ loadout rules split --from instructions.md
instructions.md  6.4KB today

  authentik   authentik/**, **/blueprints/**   1.5KB
    from  Authentik Blueprints (version 2025.12)
    from  Auth Patterns
  networking  **/docker-compose*.yml, traefik/**  932B
  secrets     **/.env*, secrets/**             1011B

  Always loaded  3KB  was 6.4KB
  On demand      3.4KB

Every line is accounted for.
```

Content moves verbatim, never reworded, and every non-blank line in the source
must appear at least as often across the outputs or the split is refused rather
than applied. A file that has already been split cannot be split again, because
the second pass would rebuild the rules from a source whose content has already
moved out of it.

## Memory

Memory holds durable facts a session should not have to rediscover:
architecture, decisions and why they were made, non-obvious build behaviour,
traps that keep catching people. It lives in the workspace repository at
`projects/<slug>/memory/`, so a fact learned on one machine is available on the
next and a wrong one can be corrected in a pull request like any other mistake.

```bash
loadout memory write starstats build-quirks \
  --description "things that surprise people about the build" \
  --fact "The first build after a clean takes four minutes; the analyzers warm up."
```

Only the index reaches the compiled context. Topics stay on disk with their
paths listed, because a project accumulates memory for years and inlining all of
it would make every session pay for every fact anyone ever recorded.

### Which project is this?

Every registered repository records its project in its own Git config, under
`loadout.project`. That file, `.git/config`, is per-clone and is never
committed, so the mark adds nothing to the repository's contents — the rule that
application repositories hold application source only is about what gets
committed, and a tracked marker file would breach it.

It is written whenever a project is registered, cloned or relocated;
`loadout project link --all` fills it in for repositories registered before the
mark existed.

Resolution takes the recorded path first, then the mark, then the canonical
remote. The order matters: the path is this machine's own record of where a
project lives, so a directory copied from elsewhere cannot use its inherited
mark to answer to another repository's name. The mark earns its place in the
case the path cannot cover — a repository that has been moved is still
recognised, rather than looking like one the launcher has never seen.

`loadout project survey` reports agent state on this machine that no project
accounts for, and says what each piece appears to belong to:

```
D:\git\GateConquestRepos  7 topic(s)
  holds 2 repositories so this was recorded across all of them
    GateConquestFlask
    GateConquestWeb
  decide which project it belongs to, then: loadout memory import <project> --from ...
```

`--adopt` takes on what can be taken on without a judgement call: importing
memory for a project that already exists, and registering a repository that is
plainly one repository before importing its memory. It previews first, asks per
repository, and takes a backup before writing, so one can be accepted and its
neighbour declined.

It deliberately never touches the other cases. A directory holding several
repositories needs somebody to say which one the state describes; a directory
that is not a repository at all cannot be registered, and suggesting it would
send you to a command that cannot succeed.

That last case is the one worth having. Agents key their state by the directory
they were started in, which is not always a repository: work done across several
repositories from their parent accumulates memory against the parent, where it
describes all of them and belongs to none. The launcher names the candidates and
stops. Picking one would be a guess presented as a fact, and the wrong guess
files a repository's hard-won notes under its neighbour.

### Adopting a project that already has memory

Several repositories were managed with an agent's own tooling before this
launcher existed, and their accumulated facts sit in a machine-local directory
nothing here reads. `loadout doctor` reports when it finds any, and
`loadout memory import` brings it across:

```bash
loadout memory import starstats                 # finds the agent's own layout
loadout memory import gateconquest --from <dir> # or point at it directly
```

Topics are copied verbatim, never overwriting one already in the workspace, and
one holding something credential-shaped is refused rather than committed — the
workspace is a Git repository, so importing a token would publish it on the next
push. The original is copied rather than moved, so nothing is lost if the import
is wrong; removing the old copy is left to you.

Repositories organised this way also arrive with their instructions already
split into `.claude/rules/`. `loadout migrate` moves those to
`projects/<slug>/rules/` rather than into the agent's own directory: which
instructions apply to which paths is true whichever agent reads them, and the
rule loader only looks in the project's own rules directory. And `rules split`
refuses a file that something else has already split, recognising it by the fact
that it points at rule files rather than containing the detail itself — splitting
it again would rebuild those rules out of the summary left in their place.

Two checks keep memory worth loading:

- **Credentials are refused on write.** Memory is committed to a shared
  repository, so writing a token and flagging it afterwards would mean the
  disclosure had already happened. Findings name the *pattern* that matched and
  never the value.
- **Facts that will rot are reported.** An account of a change ("added a retry
  to the upload step") belongs in the repository history and reads as present
  tense forever; a fact dated to the day it was written ("the highest migration
  is 0052") misleads within weeks. `loadout memory audit` reports those along
  with duplicates, oversize topics, stale entries and index rot.

`loadout memory audit --clean` removes what can be removed without judgement:
topics holding no facts, facts repeated word for word, and index lines pointing
at files that are gone. It never rewrites prose and never merges two facts that
merely say similar things — deciding which wording is the right one is the
judgement a tool should not be making on somebody's behalf. A backup is taken
first, and `--apply` is required to change anything.

## Undo

Every operation that rewrites files takes a snapshot first — `migrate`,
`rules split` — and prints the command that reverses it:

```
Migrated 4 item(s) into the workspace.
Undo it with: loadout backup restore 20260823-141502-a1b2
```

Each set records a SHA-256 per file. A restore verifies every digest before
writing anything, so a corrupted set fails before it can leave the tree half
restored, and it takes its own snapshot first so undoing an undo is possible.
Paths that did not exist at capture time are recorded as absent, which is what
lets a restore *remove* the files an operation created rather than leaving them
behind.

For structured files, the restore also reports which keys it would take away:

```
Settings that would be lost (present now, absent in the backup):
  .claude/settings.json
    - toolSearch
```

That is the failure a file-level backup cannot otherwise see. Every digest
matches, the restore reports success, and a setting somebody turned on last week
is gone with nothing to show it existed. Key paths only, never values, because a
settings file can hold a credential.

## First run

```bash
loadout setup
```

Running `loadout` with no arguments on an unconfigured machine goes here too,
because an empty project list tells a new user nothing about what to do next.

Every question can also be answered up front, so provisioning a machine needs no
one sitting at it:

```bash
loadout setup --create-new --github --name agent-workspaces   --register-discovered --migrate --global-excludes --non-interactive
```

Both routes run the same code — an interactive run is just one where nothing was
answered in advance — so the scripted path cannot drift from the one people see.
Anything genuinely unanswerable stops before doing any work and names the flag
that would settle it, rather than failing halfway through a setup.

If you choose to create a new workspace and the GitHub CLI is installed and
signed in, it offers to create the private repository and push for you. That is
a convenience for one common host, not a dependency: the launcher is
provider-agnostic (spec section 10), the other option takes any Git URL, and
Forgejo, GitLab, Azure DevOps or a bare SSH repository all work the same way.
The repository is always created private — a workspace holds project context,
decisions and handoffs, and making that public is an irreversible disclosure
that should not be one keystroke away.

The wizard offers the three choices of spec section 61 as equals — point at an
existing central workspace, create a new one, or **run without central
storage**. The last is a real way to use the tool, not a degraded mode: it
creates the same directory layout locally, so adopting a shared workspace later
is a matter of pushing what you already have.

It then checks Git is present before asking anything and sets a **global** Git
identity if none exists — global specifically, because a plain config read
resolves through whatever repository you happen to be standing in, and a local
identity in an unrelated project must not be mistaken for one the workspace can
use. Without it every workspace commit fails with "Author identity unknown".

It picks a secret provider that actually works on this machine, lists the
repositories it found in your development roots, offers to register them, and
then offers to migrate any agent files out of them.

Migration runs **before** the global Git excludes are installed, and the order
matters: installing the excludes first would make the very files migration
exists to move become ignored, so setup would protect the repository and then
report nothing to migrate. Clean up first, then stop it happening again.

## Updating

```bash
loadout config set updates-source https://internal.example/loadout/feed.json
loadout update --check
loadout update
```

The source is any JSON document reachable over HTTP, or a path — a directory on
a share is a perfectly good internal release source (spec section 79), and no
service has to answer:

```json
{
  "schemaVersion": 1,
  "version": "0.2.0",
  "notes": "What changed.",
  "artifacts": {
    "osx-arm64": {
      "url": "https://internal.example/loadout/loadout-0.2.0-osx-arm64.tar.gz",
      "sha256": "985daa42...",
      "size": 31110221
    }
  }
}
```

Replacing the binary somebody is about to run is the most dangerous thing the
launcher does, so:

- **A published SHA-256 is required.** A feed that will not commit to a hash can
  hand over anything, and that download becomes the binary you run next. The
  update is refused with exit 9.
- **The hash is checked before anything is put in place**, and a mismatch leaves
  the working binary exactly where it was.
- **The previous binary is kept** as `loadout.previous`, so a bad update can be
  undone by hand rather than reinstalled.
- **Nothing updates without being asked.** `--yes` or a prompt; non-interactively
  it refuses rather than swapping the binary out from under a script.
- **A malformed or older version is never treated as newer**, so a rolled-back or
  broken feed cannot walk you backwards.

The running executable is moved aside rather than overwritten, because Windows
will not let a running image be replaced but will let it be renamed.

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
loadout starstats --environment production
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
`loadout workspace save` is suggested.

## Repository cleanliness

The launcher's central claim is that application repositories hold application
source and agent state lives elsewhere. Three commands make that verifiable
rather than aspirational:

```bash
loadout repo check          # what is tracked that should not be
loadout migrate --dry-run   # what would move, and where
loadout protect --global    # stop it happening again
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
calling back into `loadout`, so it keeps working on a machine where the
launcher has been moved. A hook the launcher did not write is never overwritten
or deleted. Hooks live in `.git/hooks` and so are per-clone — `loadout doctor`
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
Loadout.Models      Records, DTOs, config classes. No logic.
Loadout.Platform    Abstractions + Windows / Linux / macOS / Unix implementations.
Loadout.Core        Projects, Git, Workspace, Configuration, Security, Diagnostics.
Loadout.Agents      Claude, Codex and generic adapters; the launch pipeline.
Loadout.Cli         The loadout executable.
Loadout.Tui         The interactive selector.
```

The rule that makes cross-platform parity hold is that **`Core`, `Agents` and
`Tui` depend on `Platform.Abstractions` only**. Exactly one file —
[`PlatformServices.cs`](src/Loadout.Platform/PlatformServices.cs) —
branches on the operating system. Two tests in
[`ArchitectureTests.cs`](tests/Loadout.Tests/Architecture/ArchitectureTests.cs)
enforce this: one reads each assembly's type-reference table to prove no shared
assembly touches a platform implementation, the other proves no project carries
an OS-suffixed target framework.

### Where things are stored

| | Windows | Linux | macOS |
|---|---|---|---|
| Config | `%APPDATA%\Loadout` | `$XDG_CONFIG_HOME/loadout` | `~/Library/Application Support/Loadout` |
| State | `%LOCALAPPDATA%\Loadout` | `$XDG_DATA_HOME/loadout` | `…/Application Support/Loadout/state` |
| Cache | `…\cache` | `$XDG_CACHE_HOME/loadout` | `~/Library/Caches/Loadout/cache` |
| Logs | `…\logs` | `$XDG_STATE_HOME/loadout/logs` | `~/Library/Logs/Loadout` |
| Secrets | Credential Manager | Secret Service (libsecret) | Keychain |

macOS uses native conventions by default. Set `AGENTCTL_USE_XDG=1` to place
launcher files under the XDG roots instead.

`config.yaml` is portable user preference. `machines.yaml` holds this machine's
absolute paths and never leaves it — the same project definition works unchanged
on a Windows desktop, a Linux workstation and a Mac.

### Capabilities, not silent gaps

Anything a platform cannot do is reported rather than quietly skipped. Run
`loadout doctor` to see the full matrix; each unavailable capability carries
the reason. Known gaps today:

- **Pseudo-terminal** — the launcher does not own a PTY yet. Agents inherit the
  current terminal, which gives correct signals, resize and exit codes for
  terminal launches. An owned PTY is needed only for desktop launch.
- **macOS desktop integration** — `loadout desktop` installs a Start Menu
  shortcut on Windows and a `.desktop` entry on Linux. On macOS the `.app`
  bundle is not built yet, so the command says so and declines. Every feature
  stays reachable from the CLI and TUI.

## Verifying the Linux build without Linux

Everything below the platform seam is untestable from the host it was not
written for, and "it compiles" is not the same claim as "it works". The Unix
pseudo-terminal in particular allocates a tty, spawns into it and drives a real
session; none of that is exercised by building it.

```powershell
pwsh ./build/verify-linux.ps1                      # linux-x64
pwsh ./build/verify-linux.ps1 -Architecture arm64  # linux-arm64, emulated
```

That builds a container, runs the whole suite there, packages the archive, the
`.deb` and the `.rpm`, installs the package, runs the installed command by name
and removes it again. It is a development convenience only — spec section 1
forbids a container from being any part of how the launcher runs, and CI still
runs these tests natively on its Ubuntu leg.

It earns its keep. Running it the first time found four defects that a Windows
machine cannot see: a `waitpid` call that reaped unrelated child processes, a
library that resolves under a name only present with development packages
installed, a pre-commit hook test that proved nothing because a fake stood in
for the executable bit, and an assertion about Windows paths that could only
ever pass on Windows.

The `arm64` run is emulated, which is slow but is the only way to execute a
`linux-arm64` build without arm64 hardware — that build is otherwise
cross-compiled and never run anywhere. It found a fifth defect: `posix_spawn`
reports a missing executable to the caller on x64 and lets the child exit 127 on
arm64, so the same missing agent would have produced a clear error on one
machine and silence on another. The launcher now checks before it spawns, which
also matches what Windows already did.

Emulation cannot build Debian or RPM packages: the `stat` that `tar
--no-recursion` depends on returns `EINVAL` under QEMU, and a two-file package
built by hand fails the same way. The script probes for that with a throwaway
package and skips the step with a reason rather than reporting a defect that is
not there. Packages for arm64 are built on an x86-64 host in CI, where `tar`
behaves; installing an arm64 package *on* arm64 is the one thing still covered
nowhere.

## Licence

MIT. See [LICENSE](LICENSE).

Every dependency is permissively licensed, and that is checked rather than
assumed:

```powershell
pwsh ./build/licences.ps1 -Detailed
```

It reads the licence of every restored package from that package's own
`.nuspec`, compares it against an allowlist of permissive SPDX identifiers, and
fails on anything else. Packages old enough to declare a licence URL instead of
an expression are listed explicitly in the script with what was found when
somebody checked, so the next person neither repeats the work nor takes it on
trust. CI runs it on every change.

The check is not ceremony. FluentAssertions is Apache-2.0 up to version 7 and a
paid commercial licence from version 8, so the reference is pinned to
`[6.12.2]` rather than floated — a routine bump would otherwise swap an
open-source test library for one this project cannot ship under, silently.
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) records every dependency and
its licence.

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
- **M4 — done, less macOS packaging.** Setup wizard, workspace save-on-exit,
  desktop integration, environments, security profiles, release packaging, the
  update system, an owned pseudo-terminal (ConPTY on Windows, `forkpty` on Linux
  and macOS) and native installers (MSI, `.deb`, `.rpm`) are in.
- **macOS packaging — paused.** The platform seam, paths, Keychain and bundle
  discovery stay in the build and in the test matrix; signing, notarisation and
  a `.pkg` are deferred until there is a Developer ID and a Mac to verify them
  on. Nothing about that is a code change, only work not yet done.
