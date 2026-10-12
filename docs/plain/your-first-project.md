# Your first project

**By the end of this page:** you'll have a small web page that you decided on
and an agent built. You'll be able to open it in your browser, and every version
of it will be saved.

## Before you start

Everything on [before you start](before-you-start.md): Git, your name in Git, a
coding agent you've signed in to, and Loadout, set up.

## How it works

You type one command, `loadout start`, and it takes you through the steps below.

Loadout does the steps that set things up on your computer, one at a time. The
agent does the two steps that need thinking: deciding what to build, and
building it. Each of those two is its own session, and you're asked before
each one starts, because each one uses your plan or account.

If you stop part-way, for example to install something, type `loadout start`
again. It carries on from where you stopped.

## Steps

### 1. Start the guide

In a terminal, type:

```sh
loadout start
```

### 2. Check what's needed

Loadout lists what a first project needs and ticks off what's there. If
something's missing, it shows the line to type for your computer and asks
whether to check again. Type the line, press Enter, and answer yes.

### 3. Choose what to make

You're offered three starters:

- **A web page.** One page about anything you like.
- **A checklist.** A list you can tick off.
- **My own idea.** An empty project. The agent helps you work out what it
  should be.

All three only need a browser. Then Loadout asks what to call it.

### 4. Loadout makes the project

It makes a folder, puts the starter's files in it, and starts Git, so every
version is kept from now on. It tells Loadout about the project and protects
it, so the agent's own working files never get saved into it by mistake.

You'll see where the folder is. Nothing has been sent anywhere.

### 5. Plan it with the agent

Loadout shows you which agent it'll start, in which folder, and roughly how big
its instructions are. Then it asks whether to start.

When you say yes, the agent begins by asking what you want. It asks one
question at a time. It doesn't change anything while you talk. When you agree,
it writes the plan to a file called `PLAN.md`. Then type `/exit` to end the
session. (That's the word in Claude Code. Other agents tell you theirs.)

Loadout saves the plan as a version. If the session also changed something it
shouldn't have, Loadout tells you and leaves it alone.

If you say no, nothing is spent. Type `loadout start` when you're ready.

### 6. Build it with the agent

Same again. Loadout shows you what it'll start and asks first. This time the
agent builds what `PLAN.md` describes, and says what each change does as it
goes. When it's finished, end the session.

### 7. See what changed

Loadout lists what the agent changed. It asks whether to save the changes as a
version, so you can always come back to them. Then it shows you a link to your
page. Copy it into your browser's address bar to see it.

## Afterwards

To work on the project again, with the agent, type `loadout` followed by the
project's name, for example `loadout my-first-page`. That's the same as
[your first launch](first-launch.md) describes. Your project is ready for it.

To make a second project from a starter, type `loadout start --again`.

## If something went wrong

- **The plan step finished, but there's no `PLAN.md`.** The session ended
  before the agent wrote it. Type `loadout start` to plan again.
- **The page looks wrong.** Type `loadout` and the project's name. Then tell
  the agent what you see. Every earlier version is still saved.
- **Anything else.** Type `loadout doctor` and press Enter. It tells you what to
  fix.

## What this doesn't do

- It never starts an agent without asking you first, even with `--yes`. That
  flag only skips the questions about your computer.
- It doesn't put your project on the internet. If the GitHub CLI is installed
  and signed in, it shows you the line that would. Typing it is up to you.
- It can't tell whether the agent got your page right. You can, by opening it.

## Next

[Your first launch](first-launch.md) explains what Loadout tells the agent, for
when you work on this project or another.
