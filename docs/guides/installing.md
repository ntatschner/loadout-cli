# Installing

**At the end:** `loadout` is on your `PATH`, and `loadout doctor` has told you
whether your coding agent was found.

## Before you start

- A coding agent: Claude Code or Codex. Loadout starts one; it doesn't install
  one. If neither is on your `PATH`, step 5 says so, which is the point of it.
- Git.
- The archive or installer for your platform, from the
  [latest release](https://github.com/ntatschner/loadout-cli/releases/latest).
  Loadout runs natively on Windows, Linux and macOS, so there's no VM or
  container to set up.

## Steps

### 1. Download the file for your platform

From the release page, take one of:

- Windows: `loadout-0.41.1-win-x64.msi`, or the `.zip` if you'd rather manage
  `PATH` yourself. On an ARM machine, take the `win-arm64` build.
- Linux: `loadout-0.41.1-linux-x64.tar.gz`, or the `.deb` or `.rpm`. On an ARM
  machine, take the `linux-arm64` build.
- macOS: the `osx-arm64` archive for Apple silicon, or `osx-x64` for an Intel
  Mac. macOS gets archives only, because a `.pkg` that isn't signed and
  notarised would spend the install fighting Gatekeeper.

### 2. Install it

On **Linux or macOS**, extract the archive and run the install script:

```sh
tar -xzf loadout-0.41.1-linux-x64.tar.gz
./install.sh
```

`install.sh` checks the SHA-256 before it extracts anything and refuses on a
mismatch, so a damaged or altered download never gets installed. It puts
`loadout` in `~/.local/bin` and needs no root. On macOS it also clears the
download quarantine attribute from that one binary, because the binary isn't
signed yet and Gatekeeper would otherwise block it. Nothing here will ever ask
you to turn Gatekeeper off.

On **Windows**, run the MSI:

```powershell
msiexec /i loadout-0.41.1-win-x64.msi
```

It installs per user, with no elevation, into `%LOCALAPPDATA%\Programs\loadout`,
adds that to your `PATH` and makes a Start Menu entry.

On **Debian, Ubuntu, Fedora or similar**, the packages are an alternative:

```sh
sudo dpkg -i loadout_0.41.1_amd64.deb
```

### 3. Open a new terminal

`PATH` changes reach terminals opened after the install, not the one you
installed from.

### 4. Check the version

```sh
loadout --version
```

You should see one line with the version you downloaded. If it's older, a
different `loadout` is earlier on your `PATH` — the one on `PATH` has been
found three releases behind the one somebody thought they were running.

### 5. Check what's missing

```sh
loadout doctor
```

`loadout doctor` on a new machine, naming which agent it found.

```text
$ loadout doctor
Loadout Diagnostics

Platform
+ Windows X64  Microsoft Windows 10.0.26200 (win-x64)
+ Machine  EXAMPLE-PC

Launcher
+ Configuration  C:\Users\Public\example\AppData\Roaming\Loadout\config.yaml
+ State  C:\Users\Public\example\AppData\Local\Loadout
+ Logs  C:\Users\Public\example\AppData\Local\Loadout\logs
+ Machine configuration  
C:\Users\Public\example\AppData\Local\Loadout\machines.yaml

Git
+ Installed  git version 2.54.0.windows.1
+ Credential helper  manager
! Global exclude file  not configured; agent files are not globally ignored 
(spec section 50) (fixable)

Workspace
+ Central workspace  not configured; running with local state only

Discovery
+ C:\Users\Public\example\src  case-insensitive

Secrets
+ Provider  credential-manager

Repository
+ Agent files  none tracked
! Pre-commit protection  not installed in this clone; hooks are per-clone 
(fixable)
! Global excludes  no global exclude file is configured, so agent files are only
kept out of repositories that ignore them individually (fixable)

Capabilities
+ NativeSecretStore  credential-manager
+ PseudoTerminal  ConPTY
+ PseudoTerminalWindowSize  ConPTY resize
! UnixFilePermissions  Windows has no Unix mode bits; restricted ACLs are 
applied instead.
+ DesktopIntegration  available, not installed
+ FileManagerIntegration  explorer
+ Clipboard  clip
+ TerminalSpawning  Windows Terminal, PowerShell, Windows PowerShell
! GraphicalSession  Output is redirected or no terminal is attached, so prompts 
are suppressed.
+ ChildProcessLifetime  a job object the kernel closes with the launcher, which 
takes its children with it however it ends

Instructions
+ Memory content  No credential-shaped content in project memory.
+ Instruction budget  No project loads an oversized instruction layer.
+ Tasks nobody is shown: storefront  2 task(s) are recorded for storefront and 
its sessions are not shown any of them. Carrying them costs a heading and a line
each. (fixable)
+ Specialist library  101 specialists loaded and valid.

Editor
+ code  C:\Program Files\Microsoft VS Code\bin\code.cmd
+ Profiles  could not be read, so nothing here is checked against them

Sessions
+ Running  none

Teams
! Daemon  1 schedule(s) are written down and nothing is firing them. Start one 
with: loadout team daemon

Agents
+ Claude Code  2.1.280 (Claude Code) at C:\Users\example\.local\bin\claude.exe
+ Codex  codex-cli 0.154.0 at C:\Users\example\AppData\Roaming\npm\codex.cmd

Overall: DEGRADED
2 of these can be put right for you: loadout doctor --fix
1 thing(s) here are available and switched off. Nothing is wrong with them: 
loadout doctor --fix
```

You should see a list of checks, each with its result in words, and the agent
it found named. If it says no agent was found, install Claude Code or Codex and
run it again.

## If it went wrong

- **`loadout` isn't found.** Open a new terminal (step 3). On Linux and macOS,
  check `~/.local/bin` is on your `PATH`.
- **`install.sh` refuses.** The checksum didn't match. Download the archive
  again rather than working round it.
- **Anything else.** `loadout doctor` names what's wrong, and
  `loadout doctor --fix` mends what it safely can.

## What this doesn't do

- It doesn't install Claude Code or Codex; see
  [Claude Code's install page](https://docs.claude.com/en/docs/claude-code/setup)
  or [Codex's install page](https://github.com/openai/codex) for what each needs.
- The macOS binary isn't signed or notarised yet.
- Nothing updates on its own. `loadout update` checks and installs when you ask;
  [the installing reference](../installing.md) explains how it verifies an
  update.

## Next

[Your first run](first-run.md)
