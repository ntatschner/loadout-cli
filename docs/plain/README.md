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

It does a few more things around the agent. It writes down what the agent
learned, so the next time starts from there instead of from nothing. It adds up
what the agent has cost you. And when one agent isn't enough, it can run a small
team of them on one job, and show you what they're doing in your web browser.
None of this changes the agent itself. Everything around your coding agent,
except the agent.

## What you need before you start

- **A computer running Windows, macOS or Linux.**
- **A coding agent.** You'll also need Claude Code or Codex. Loadout doesn't
  install them. [Claude Code's install page](https://code.claude.com/docs/en/setup)
  and [Codex's install page](https://github.com/openai/codex) say what each
  one needs.
- **Git.** Git is a program that keeps track of changes to files. Loadout uses
  it.
- **A project.** This is a folder of code you want the agent to work on. If
  you don't have one, `loadout start` helps you make your first.

Loadout itself needs no account and no online service.

## Where to go next

Take these in order:

1. [Before you start](before-you-start.md), if you don't have Git or a coding
   agent yet
2. [Installing Loadout](installing.md)
3. [Your first project](your-first-project.md), if you're starting from nothing
4. [Your first launch](first-launch.md), if you already have a folder of code
5. [Watching a team](watching-a-team.md), when you're ready for more than one
   agent at once
6. [Accessibility](accessibility.md), if you'd like Loadout to change how it
   shows or says things
7. [Studio](studio.md), if you'd rather start and use a session in your web
   browser

If something goes wrong at any point, type `loadout doctor` and press Enter. It
tells you what's missing.
