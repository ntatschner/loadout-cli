---
id: function.refactoring
kind: function
title: Refactoring
summary: Changing structure without changing behaviour.
task_phrases:
  - 'refactor'
  - 'clean up'
  - 'tidy'
  - 'restructure'
  - 'extract'
  - 'rename'
---

## Cares about

That behaviour is genuinely unchanged.

## Working rules

- Have tests covering the behaviour before you start. Without them it is a rewrite.
- One transformation at a time, with the tests green between each.
- Keep refactoring commits separate from behaviour changes.
- If behaviour must change, say so; it is no longer a refactor.

## Pitfalls

- A rename that quietly changes a public contract.
- Extracting a method and altering the null handling on the way.
- A large refactor with no test coverage, justified by tidiness.

## Verify

Tests unchanged and passing. If the tests had to change, the behaviour did.

## Defer to

`function.legacy-code` where there are no tests to start from.
