# Installing Loadout

**By the end of this page:** Loadout is on your computer, and it has checked
that your coding agent is there too.

## Before you start

You'll need a coding agent, Claude Code or Codex, installed first. Loadout
doesn't install it for you. [Loadout in plain words](README.md) explains why.

## Steps

### 1. Open a terminal

A **terminal** is a window where you type commands instead of clicking.

- **Windows:** press the Windows key, type `Terminal`, and press Enter.
- **macOS:** press Command and Space together, type `Terminal`, and press
  Enter.
- **Linux:** press Ctrl, Alt and T together.

You should see a window with a line of text and a blinking cursor.

### 2. Install Loadout

Type the line for your computer and press Enter. It works out which version
your computer needs, downloads it, checks it, and installs it.

On **macOS or Linux**:

```sh
curl -fsSL https://github.com/ntatschner/loadout-cli/releases/latest/download/install.sh | sh
```

On **Windows**:

```powershell
irm https://github.com/ntatschner/loadout-cli/releases/latest/download/install.ps1 | iex
```

It checks the download wasn't damaged or changed before it installs anything.
If the check fails, it stops and installs nothing. Run the line again.

If you already use Homebrew on macOS or Linux, this works too:

```sh
brew install thecodesaiyan/loadout/loadout
```

### 3. Close the terminal and open a new one

The new terminal knows where Loadout is. The old one doesn't.

### 4. Check Loadout is there

Type this and press Enter:

```sh
loadout --version
```

You should see one line with a version number.

### 5. Check what's missing

Type this and press Enter:

```sh
loadout doctor
```

You should see a list of checks. Each one says in words whether it passed. It
also names the coding agent it found.

If it says no agent was found, install Claude Code or Codex. Then type
`loadout doctor` again.

## If something went wrong

- **"loadout" isn't recognised.** Close the terminal and open a new one. Then
  try step 4 again.
- **Anything else.** Type `loadout doctor` and press Enter. It tells you what
  to fix.

## What this doesn't do

- It doesn't install a coding agent.
- On macOS, Loadout isn't signed by Apple yet. Installed with the line in
  step 2, macOS doesn't block it, so you don't have to change any security
  settings.

## Next

[Your first launch](first-launch.md)
