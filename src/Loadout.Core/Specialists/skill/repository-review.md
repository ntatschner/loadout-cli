---
id: skill.repository-review
kind: skill
title: Repository review
summary: A procedure for learning a codebase and leaving what you learned behind.
task_phrases:
  - 'review the repo'
  - 'review this codebase'
  - 'learn this codebase'
  - 'onboard'
  - 'get up to speed'
  - 'what is this project'
modes:
  - 'investigate'
  - 'advise'
  - 'review'
---

## When to use

Meeting a codebase for the first time, or returning to one nobody has written
anything down about. The point is not the report. The point is that the next
session starts knowing what this one worked out.

## Measure before reading

Ask the launcher what it already knows, rather than deriving it:

- `loadout instructions explain --project <slug>` — what a session here is given
  and why. Tells you the languages and frameworks it detected from the files.
- `loadout instructions audit --project <slug>` — where the code departs from
  what its own specialists ask for.
- `loadout rules budget <slug>` — how much instruction text every session pays
  for, and whether it is out of hand.
- `loadout memory list <slug>` — what previous sessions already recorded. Read
  this first: re-deriving something already written down is the commonest waste.

## Procedure

1. Establish the shape: entry points, where a request or command enters and
   where it leaves. Name the directories that matter and what each is for.
2. Find the seams — where the code is meant to be extended, and where it is
   meant not to be. A seam nobody can name is a seam that will be broken.
3. Read the tests before the implementation. What they cover says what the
   authors were afraid of; what they skip says what nobody has been bitten by
   yet.
4. Trace one change end to end: pick something small and real, and follow what
   it would touch. This finds the coupling that no diagram shows.
5. Find what surprised you. A convention that is not obvious, a file that is not
   where it should be, a rule enforced somewhere unexpected.
6. Check the claims. Anything you would write down as true, confirm by running
   it or reading the code that does it — not by inferring from a name.

## Record what stays true

Write the findings down as you go, one topic per subject:

```
loadout memory write <topic>                  # this repository's project
loadout memory write <topic> --project <slug>
```

or `loadout_remember` if the launcher's tools are available in this session.

What belongs in memory:

- A decision and the reason behind it, especially where the reason is not
  visible in the code.
- A constraint that is not enforced anywhere: "this must stay in sync with X".
- A trap: something that looks wrong and is deliberate, or looks fine and is not.
- Where a thing lives when the name does not say so.

What does not:

- Anything that will be false next month: version numbers, current counts, what
  is on a branch today. State the rule, not the reading.
- What the code already says plainly. Memory is paid for on every launch;
  duplicating a file name earns nothing.
- Anything secret. The store screens for credentials and refuses them, and a
  refusal means you were about to record one.
- What you did. Memory is for what is true, not for a diary of the session.

## Updating rather than adding

Look at `loadout memory list` before writing. A second topic covering the same
ground splits the answer in two, and the next session reads whichever it finds
first. Extend the existing topic instead, and delete what has turned out to be
wrong — a memory that is confidently false costs far more than a missing one.

## Done when

- What the launcher already knew has been read, not re-derived.
- One change traced end to end.
- Every claim recorded was checked rather than inferred.
- The findings are in memory, not only in the conversation, and each one would
  still be true in a month.
