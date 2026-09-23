# Loadout in plain words

This page is for you if you're new to AI and don't write code for a living. It
explains what Loadout is before it asks you to do anything.

## What a coding agent is

A **coding agent** is an AI program that works on computer code. You tell it
what you want in ordinary words. It reads files, changes them, runs commands,
and looks at what happened. Then it decides what to do next.

Two coding agents are called **Claude Code** and **Codex**. Loadout works with
both.

Three things are worth knowing about any coding agent:

1. **It only knows what it's been told.** It can't see anything you didn't give
   it.
2. **It forgets.** When you close it, what it worked out is gone, unless
   something wrote it down.
3. **It can be wrong and sound sure.** It sounds the same when it's right and
   when it's wrong. So you check its work.

## What Loadout is

Loadout isn't the AI. It's the thing that starts the AI for you, with the right
notes for the job.

Think of it like packing a bag before a trip. Loadout looks at your project and
at what you said you want to do. Then it packs the notes the agent needs. It
shows you what's in the bag before you go, and how big it is.

Those notes are called **instructions**. How big they are is measured in
**tokens**, which are small pieces of text. More tokens cost more.

Loadout also keeps the agent's own files out of your project. Your project stays
tidy.

## What you need before you start

- **A computer running Windows, macOS or Linux.**
- **A coding agent.** You'll also need Claude Code or Codex. Loadout doesn't
  install them. [Claude Code's install page](https://docs.claude.com/en/docs/claude-code/setup)
  and [Codex's install page](https://github.com/openai/codex) say what each
  one needs.
- **Git.** Git is a program that keeps track of changes to files. Loadout uses
  it.
- **A project.** This is a folder of code you want the agent to work on.

Loadout itself needs no account and no online service.

## Where to go next

Take these in order:

1. [Installing Loadout](installing.md)
2. [Your first launch](first-launch.md)
3. [Watching a team](watching-a-team.md), when you're ready for more than one
   agent at once
4. [Accessibility](accessibility.md), if you'd like Loadout to change how it
   shows or says things

If something goes wrong at any point, type `loadout doctor` and press Enter. It
tells you what's missing.
