# Before you start

**By the end of this page:** your computer has everything a first project
needs. That's Git, your name in Git, a coding agent you've signed in to, and
Loadout.

You don't need to have written any code. You don't need a GitHub account
either.

If you'd rather be walked through it, install Loadout first (see
[installing Loadout](installing.md)) and type `loadout start`. It checks each
of these in turn, shows you the line to type for your computer, and checks
again once you've typed it. It never installs anything itself. The lines it
shows are the same as the ones on this page.

## Open a terminal

A **terminal** is a window where you type commands instead of clicking.

- **Windows:** press the Windows key, type `Terminal`, and press Enter.
- **macOS:** press Command and Space together, type `Terminal`, and press
  Enter.
- **Linux:** press Ctrl, Alt and T together.

Every line below is typed into a terminal and finished by pressing Enter.

## Git

**Git** keeps every version of your project's files. If a change goes wrong,
you can go back to the version before it. Loadout uses Git for every project.

On **Windows**:

```powershell
winget install --id Git.Git -e --source winget
```

On **macOS**, this installs Apple's developer tools, which include Git. A
window opens and asks you to confirm:

```sh
xcode-select --install
```

On **Linux**, the line for Ubuntu and Debian is below. Other versions of Linux
have their own way to install Git, and
[Git's download page](https://git-scm.com/downloads/linux) lists them.

```sh
sudo apt install git
```

**Then close the terminal and open a new one.** A terminal that was already
open doesn't know Git has arrived. This catches out nearly everyone on
Windows.

Check it worked:

```sh
git --version
```

You should see one line with a version number.

## Your name in Git

Git writes a name and an email on every version it saves. Until you've given it
both, it won't save anything. They stay on your computer unless you publish the
project somewhere.

Put your own name and email between the quotes:

```sh
git config --global user.name "Your Name"
git config --global user.email "you@example.com"
```

The lines are the same on every computer. If you'd rather not use your real
email, any address will do. Git doesn't send anything to it.

## A coding agent

The **coding agent** is the AI that talks your project through with you and
then writes it. Loadout starts it, but it doesn't come with Loadout.

The lines below install **Claude Code**. It needs a Claude subscription or an
Anthropic API account, and using it uses up your plan or account.

On **Windows**, in PowerShell:

```powershell
irm https://claude.ai/install.ps1 | iex
```

On **macOS or Linux**:

```sh
curl -fsSL https://claude.ai/install.sh | bash
```

Then close the terminal, open a new one, and type `claude`. It asks you to sign
in the first time. Once you're signed in, type `/exit` to close it again.

[Claude Code's own setup page](https://code.claude.com/docs/en/setup) is the
final word if anything there has changed since this page was written.

If you'd rather use **Codex**, Loadout works with that too.
[Codex's install page](https://github.com/openai/codex) says how to get it.

## Installing Loadout

[Installing Loadout](installing.md) has the line for your computer.

## Setting up Loadout

Loadout needs to know where to keep what it learns about your projects. Type:

```sh
loadout setup
```

It asks you some questions. For a first project, keeping everything on this
computer is fine. You can change it later.

## Check everything at once

```sh
loadout start
```

The first thing it shows is a list of what a first project needs, with a tick
or a warning beside each. If everything is ticked, it carries on and makes your
project. [Your first project](your-first-project.md) follows it from there.

## What this doesn't cover

- **GitHub.** Your first project stays on your computer. If the GitHub CLI is
  already installed and signed in, `loadout start` shows you the line that would
  put a copy there, but it never does it for you.
- **Other programming tools.** A first project is a web page, which only needs
  a browser. Nothing else gets installed.

## Next

[Your first project](your-first-project.md)
