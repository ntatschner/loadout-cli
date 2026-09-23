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

- Windows: `loadout-0.41.0-win-x64.msi`, or the `.zip` if you'd rather manage
  `PATH` yourself. On an ARM machine, take the `win-arm64` build.
- Linux: `loadout-0.41.0-linux-x64.tar.gz`, or the `.deb` or `.rpm`. On an ARM
  machine, take the `linux-arm64` build.
- macOS: the `osx-arm64` archive for Apple silicon, or `osx-x64` for an Intel
  Mac. macOS gets archives only, because a `.pkg` that isn't signed and
  notarised would spend the install fighting Gatekeeper.

### 2. Install it

On **Linux or macOS**, extract the archive and run the install script:

```sh
tar -xzf loadout-0.41.0-linux-x64.tar.gz
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
msiexec /i loadout-0.41.0-win-x64.msi
```

It installs per user, with no elevation, into `%LOCALAPPDATA%\Programs\loadout`,
adds that to your `PATH` and makes a Start Menu entry.

On **Debian, Ubuntu, Fedora or similar**, the packages are an alternative:

```sh
sudo dpkg -i loadout_0.41.0_amd64.deb
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

<!-- capture: docs/captures/doctor.txt — `loadout doctor` on a new machine, naming which agent it found. -->

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
