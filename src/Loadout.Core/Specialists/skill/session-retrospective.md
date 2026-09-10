---
id: skill.session-retrospective
kind: skill
title: Session retrospective
summary: A procedure for closing a session honestly and keeping what it learned.
task_phrases:
  - 'finish up'
  - 'wrap up'
  - 'lessons learned'
  - 'retrospective'
  - 'retro'
  - 'end of session'
  - 'what did we learn'
---

## When to use

Work is finishing. The difference between a session that compounds and one that
does not is whether what it worked out survives it. `skill.repository-review`
covers arriving at a codebase; this covers leaving one.

## Reconstruct before reviewing

What you remember of a session is the least reliable account of it, because the
beliefs corrected along the way are the ones that felt true while you held
them. Start from what the repository can show:

```
git log --oneline <last-tag>..HEAD    # what landed
git status --short                    # what did not
git diff --stat                       # the shape of the uncommitted
```

Then treat every claim the session made as a claim:

- **A passing suite is a claim.** Unless the output is in front of you, either
  run the covering tests again or write "not re-verified". An earlier turn's
  green is not evidence now.
- **A fix is a claim.** Name what demonstrated it. If the demonstration was a
  theory that looked right, that is a hypothesis, not a fix.
- **A published or deployed state is a claim.** Check it or mark it unchecked.

## Draw the lessons

Against that evidence, not against recollection. Most sessions answer two or
three of these; a session that answers none has no lessons, and saying so is a
result.

1. What took more than one attempt, and why did the first one fail? The reason
   is the lesson; the eventual fix usually is not.
2. What turned out to be false? A disproved explanation is worth recording
   precisely because it was plausible — it will look plausible again.
3. What lied? A log, a status field, a tool's output, a test that passed for
   the wrong reason. Instruments that mislead are the highest-value finding
   there is.
4. What did the environment do that a fresh session would not expect?
5. What is true of the code that reading it does not reveal — intent behind a
   choice that looks arbitrary and is not?
6. What did the user correct, and what did the correction imply?
7. What would the next session rediscover from nothing? This one catches what
   the others miss.

## Sift

A candidate is worth recording only if all three hold. It will still be true
next month; a next session cannot get it free from the code, the history or the
instructions it already loads; and it changes what somebody does. Check the
third against `loadout memory find <query>` before assuming it is new.

Two rules on content. Record only what the session **observed** — anything
inferred goes in the report as worth confirming, because a memory arrives at
the next session as settled fact and carries no hedge with it. And never record
a secret value: the pattern, the location and the type, never the credential.

Then route it by what it is true of:

| Scope | True of | Example |
|---|---|---|
| `project` | this codebase | a constraint the code does not enforce |
| `user` | this person's work anywhere | how they want commits written |
| `machine` | only here | a local policy that breaks a tool |

```
loadout memory write <topic> --kind lesson --scope project
```

The store screens what it is given: a description that restates the topic's own
name is refused, because that line is all a future session sees; and a fact
that makes no standing claim is refused, because "investigated the build" gives
the next session nothing to rely on. Say what is true, what is required, or why
something is the way it is. Prefer extending an existing topic over adding a
second one that splits the same answer in two.

## Unfinished work is a handoff, not a memory

Anything that fails the first test — in-flight state, a half-made change, what
you were about to try next — belongs in a handoff, where it expires naturally
instead of ageing into a lie:

```
loadout handoff <project>
```

Worth doing whenever the work is unfinished. You know now what the next session
would otherwise have to work out again; after this turn, nobody does.

## Close with an honest report

Four parts, short: what landed; **what was not verified**, named plainly; what
was recorded and at what scope; and what was deliberately not recorded, with
the reason. The unverified section is the point of the exercise — an empty one
should be rare, and true.

## Done when

- Every claim was re-checked or explicitly marked unverified.
- What was recorded was observed, not inferred, and would still hold in a month.
- Existing topics were extended rather than duplicated.
- Unfinished work went to a handoff, not to memory.
- `loadout memory audit` passes, or what it reports has been fixed.
