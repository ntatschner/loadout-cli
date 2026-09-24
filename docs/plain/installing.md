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

### 2. Download Loadout

If you already use Homebrew on macOS or Linux, you can type this instead, press
Enter, and go straight to step 5:

```sh
brew install thecodesaiyan/loadout/loadout
```

Otherwise, go to the
[latest Loadout release](https://github.com/ntatschner/loadout-cli/releases/latest)
in your web browser.

Download the file for your computer:

- **Windows:** the file ending in `win-x64.msi`. If your computer has an ARM
  processor, take the one ending in `win-arm64.msi` instead. **Settings**, then
  **System**, then **About**, shows which you have.
- **macOS:** the file with `osx-arm64` in its name if your Mac has an Apple
  chip, or `osx-x64` if it has Intel. To check, open the Apple menu and choose
  **About This Mac**.
- **Linux:** the file ending in `.tar.gz`, with `linux-x64` in its name for most
  computers, or `linux-arm64` for ARM ones.

### 3. Install it on Windows

Double-click the `.msi` file you downloaded.

It installs for you alone. It doesn't ask for an administrator password.

### 4. Or install it on macOS or Linux

In the terminal, go to the folder your download is in. It's usually called
Downloads:

```sh
cd ~/Downloads
```

Unpack the file. Change the name to match the file you downloaded:

```sh
tar -xzf loadout-0.49.1-linux-x64.tar.gz
```

Run the installer:

```sh
./install.sh
```

The installer checks the file wasn't damaged or changed before it installs
anything. If the check fails, it stops. Download the file again.

### 5. Close the terminal and open a new one

The new terminal knows where Loadout is. The old one doesn't.

### 6. Check Loadout is there

Type this and press Enter:

```sh
loadout --version
```

You should see one line with a version number.

### 7. Check what's missing

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
  try step 6 again.
- **Anything else.** Type `loadout doctor` and press Enter. It tells you what
  to fix.

## What this doesn't do

- It doesn't install a coding agent.
- On macOS, Loadout isn't signed by Apple yet. The installer handles that for
  this one file, so you don't have to change any security settings.

## Next

[Your first launch](first-launch.md)
