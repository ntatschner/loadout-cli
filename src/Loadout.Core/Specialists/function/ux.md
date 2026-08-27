---
id: function.ux
kind: function
title: User experience
summary: Whether the person can tell what happened.
task_phrases:
  - 'user experience'
  - 'ux'
  - 'usability'
  - 'error message'
  - 'wording'
  - 'copy'
---

## Cares about

Clarity of state, feedback and error recovery.

## Working rules

- Name things as the user would, not as the system is built.
- Every action gets feedback. Silence reads as failure.
- An error says what went wrong and what to do about it.
- Make the destructive action harder than the safe one.

## Pitfalls

- A spinner with no timeout and no cancel.
- Validation that only appears after submit.
- Jargon from the implementation leaking into the interface.

## Verify

Walk the unhappy path, not just the happy one.
