# Your first launch

**By the end of this page:** you'll have started a coding agent on your project,
after seeing what it was told.

## Before you start

- Loadout is installed. See [installing Loadout](installing.md).
- You have a **project**: a folder of code. It should be a **Git repository**,
  which means Git is keeping track of its changes.

## Steps

### 1. Set Loadout up

In a terminal, type this and press Enter:

```sh
loadout setup
```

Loadout asks you some questions. The first is where to keep its **workspace**.
The workspace is a folder where Loadout keeps the agent's notes. Keeping them
there means they stay out of your project.

If you're not sure, choose to create a new one. If you use more than one
computer, choose the option that uses Git. Then your notes can move between
computers.

### 2. Go to your project

Type `cd`, a space, and the path to your project folder. Then press Enter.
For example:

```sh
cd Documents/my-project
```

### 3. Tell Loadout about your project

Type this and press Enter:

```sh
loadout project add .
```

The full stop means "this folder". Loadout gives your project a short name. You
can use that name later.

### 4. Keep the agent's files out of your project

Type this and press Enter:

```sh
loadout protect
```

This stops the agent's own files being saved into your project by mistake. It
warns you. It never stops you working.

### 5. Open the launcher

Type this and press Enter:

```sh
loadout
```

![The Loadout launcher. A list of three projects on the left, starstats selected. On the right, its branch, a clean working tree, how much instruction text loads, and nothing needing attention.](../images/launcher.svg)

You'll see a list of your projects on the left. On the right is information
about the one you've picked.

### 6. Pick your project

Use the up and down arrow keys to move to your project. Press Enter.

Nothing starts yet. You'll see the **launch sheet**. This is a short form about
the session you're about to start. A **session** is one stretch of work with
the agent.

### 7. Say what you want to do

In the box at the top, type one sentence about what you want. Be specific.
"The upload retries twice then gives up" is good. "Work on the code" is too
vague to help.

If the box already has a sentence in it, Loadout found it in your project's
notes. You can change it.

### 8. Choose how the agent should work

This is called the **mode**. There are four:

- **implement** — make a change you've already decided on.
- **investigate** — find out why something happens.
- **review** — look over a change.
- **advise** — give you a recommendation.

Loadout never guesses this from your sentence. You choose it.

### 9. Read what the agent will be told

At the bottom is a box headed *This session would load*. It lists the notes the
agent will get, and why each one was picked. It also shows how many tokens they
use. A **token** is a small piece of text. More tokens cost more.

### 10. Start

Press Enter on **Launch**. Your coding agent starts with exactly those notes.

If you change your mind, choose **Cancel**. Nothing starts.

## If something went wrong

- **Your project isn't in the list.** Go back to step 3.
- **A project has a mark next to it.** Something stops it starting. The right
  side says what.
- **Anything else.** Type `loadout doctor` and press Enter.

## What this doesn't do

- Loadout doesn't check the agent's work. You still read what it changed.
- The token number counts the notes, not the whole session.

## Next

[Watching a team](watching-a-team.md), when you're ready to try more than one
agent at once.
