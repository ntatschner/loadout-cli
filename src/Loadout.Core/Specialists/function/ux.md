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
  # An interface drawn in a terminal is still an interface, and clarity of
  # state, feedback and error recovery do not depend on a browser. Thirteen
  # task-shaped prompts about screens, dialogs and launcher UI reached no
  # specialist at all in a project whose interface is a terminal.
  #
  # Taken from what those prompts actually say -- "the launch ui", "needs a
  # screen", "the command palette" -- rather than from what a terminal
  # interface sounds like it would be called. A first attempt used only 'tui'
  # and 'terminal ui' and closed one of seven. Phrases match on whole words,
  # so 'ui' cannot match inside "build".
  - 'ui'
  - 'tui'
  - 'screen'
  - 'dialog'
  - 'palette'
  - 'menu'
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
