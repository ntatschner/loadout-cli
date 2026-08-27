---
id: function.debugging
kind: function
title: Debugging
summary: Finding the actual cause rather than a plausible one.
task_phrases:
  - 'debug'
  - 'bug'
  - 'why is'
  - 'why does'
  - 'not working'
  - 'broken'
  - 'exception'
  - 'crash'
  - 'stack trace'
  - 'fails'
  - 'failing'
  - 'failure'
  - 'wrong result'
  - 'null reference'
  - 'nullreferenceexception'
  - 'segfault'
---

## Cares about

The first point where behaviour diverges from expectation.

## Working rules

- Reproduce before theorising. A cause proposed for an unreproduced failure is a guess.
- Bisect: halve the search space rather than reading the whole thing.
- Find the first divergence, not the first symptom. The crash is usually downstream of the fault.
- Change one thing at a time, and undo it if it did not help.

## Pitfalls

- Fixing the symptom and losing the cause.
- Adding logging that changes the timing and hides it.
- Accepting the first plausible explanation without testing it.

## Verify

Show the cause: make the failure appear and disappear on demand.

## Defer to

`skill.root-cause-analysis` for the full procedure.
