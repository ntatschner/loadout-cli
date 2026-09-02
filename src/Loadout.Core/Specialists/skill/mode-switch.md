---
id: skill.mode-switch
kind: skill
title: Changing mode mid-session
summary: What a mode governs, and how to change it when the work changes shape.
task_phrases:
  - 'switch mode'
  - 'change mode'
  - 'now fix it'
  - 'go ahead and implement'
  - 'stop and investigate'
modes:
  - 'advise'
  - 'investigate'
  - 'implement'
  - 'review'
---

## What a mode is

A mode is a directive for the whole session, not for one message. It was set
when the session started, with `--mode`, and it stays until something changes
it. There are four:

- **implement** — the what has been decided; make the change and verify it.
- **investigate** — something is unexplained; understanding is the deliverable.
- **advise** — answer the question, change nothing.
- **review** — judge work as written; findings, not edits.

The mode is never inferred from what somebody typed. A session started in
`implement` stays in `implement` even if the next message is a question.

## When to change it

Change it when the work stops matching how the session started, not when a
single message leans another way:

- You were asked to look into a bug, the cause is now established, and you have
  been asked to fix it. That is `investigate` becoming `implement`.
- You were implementing, hit something nobody understands, and are now
  reproducing it. That is `implement` becoming `investigate`.
- You have been asked what you think rather than for a change. That is `advise`.

One question inside a piece of implementation work is not a mode change. Answer
it and carry on.

## How

```
loadout_mode(mode: "investigate", task: "the upload retries twice then gives up")
```

Or, without the launcher's tools:

```
loadout instructions explain --mode investigate --project <slug> "<the task>"
```

Either one gives you the posture to adopt and what now applies. Adopt it for the
rest of the session and say plainly that you have, so the person you are working
with knows the rules changed.

## What changes, and what does not

Changing the mode changes two things: the posture you work under, and which
skills are on offer. A reviewing skill is available in `investigate`, `advise`
and `review` and withheld from `implement`, because implementing is not
reviewing.

Everything else keeps working as it did. The language, framework, database and
platform specialists come from what is actually in the repository, so they apply
in every mode and do not need reselecting. Specialists triggered by task
phrases keep triggering on the words in the new task. Nothing already in your
context is taken away by a mode change.

## Done when

- The new posture has been adopted, and stated rather than assumed.
- The reason it changed is one somebody would agree with: the work changed
  shape, not one message.
